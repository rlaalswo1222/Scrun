"""실시간 방 상태(계획 0029, ADR-0010). 맵(= 코스) 하나가 방 하나, 상태는 이 프로세스 메모리에만 둔다.

- 허브는 프로세스 하나에만 있다(ADR-0010). 일반 API를 프로세스 여러 개로 띄울 땐 실시간을 따로 한 프로세스로 두고 일반 API는
  내부 API로 묻는다(계획 0073, app/realtime/link.py). 실시간 자체를 여러 개로 늘릴 때 방 상태를 Redis 같은 공유 저장소로 옮긴다
- 잠금은 threading.Lock 하나. 연결 처리(이벤트 루프)와 입장·인원 API(스레드풀)가 같이 읽고 쓰기 때문.
  다른 연결에 알릴 것(joined·left)은 그 연결의 outbox(deque)에 넣고 wake()로 그 연결의 루프를 깨운다
  (연결마다 루프가 다를 수 있어 asyncio 객체를 공유하지 않음: 테스트 클라이언트는 연결마다 루프가 다름)
- 붙은 뒤 첫 state가 오기 전(섬을 불러오는 중)에는 다른 사람에게 안 보인다(placed). 첫 state 때 joined
- 끊긴 러너는 GRACE_S 동안 방에 남는다(그 사이 다시 붙으면 joined·left 없이 이어짐). 지나면 sweep이 left를 알린다.
  앱이 leave를 보내고 나가면 바로 left
- 페이스 봇(계획 0040, BL-23): 방마다 사람 + 봇 = BotPolicy.fill_to가 되게 채운다. 사람만 센다(고스트는 각자 폰에만 있어 방에 없음).
  봇 위치는 출발 위치 + 페이스 × (지금 − 출발 시각)을 welcome·states를 만들 때 계산한다(배경 작업 없음, 모두에게 같은 자리).
  사람 항목과 같은 모양에 bot: true. 봇은 누가 달리기 시작할 때(running이 처음 켜질 때) 그 사람 근처에 놓는다
  (섬에 나타날 때 놓으면 러닝 설정·카운트다운 1–2분 동안 봇이 멀리 가 버려 같은 페이스 봇을 영영 못 만남, 11/17 에디터 Play)
- 함께 달린 사람(계획 0043, 기록 상세 companions): POST /runs가 start_tracking(사람, 기록), 그 뒤 내가 달리는 중이라고
  보낼 때마다 같은 방에서 달리는 중이고 붙어 있는 사람·봇을 처음 본 순서로 모은다. 종료 API가 companions로 읽어 DB에 넣고
  커밋한 뒤 stop_tracking. 사람마다 지금 러닝 하나만(새로 시작하면 앞 것은 버림), 기록 ID가 다르면 빈 목록.
  방이 아니라 사람에 붙여 두어 끊겼다(30초 넘게) 다시 들어와도 이어진다. 서버가 다시 켜지면 그 러닝은 빈 목록.
  끝내지 않고 버려진 것은 TRACK_KEEP_S가 지나면 다음 start_tracking 때 지운다
"""
from __future__ import annotations

import math
import random
import threading
import time
from collections import deque
from dataclasses import dataclass, field
from typing import Callable, Optional

from ..core.config import BotPolicy, settings

GRACE_S = 30.0       # 끊긴 러너를 남겨 두는 시간(헬스장 와이파이 끊김 대비, 계획 0029)
MAX_KMH = 30.0       # 받아들이는 속도 상한(그 위는 잘라냄)
MAX_RUN_M = 300_000.0  # 이번 달리기 거리 상한(그 위는 잘라냄)
OUTBOX_MAX = 256     # 못 보낸 이벤트가 이만큼 쌓이면 오래된 것부터 버림(느린 연결이 메모리를 잡지 않게)
COMPANIONS_MAX = 20  # 한 러닝에 남기는 함께 달린 사람 수(맵 정원과 같음). 봇은 페이스 수만큼이라 따로 셈
TRACK_KEEP_S = 26 * 3600.0  # 모은 것을 남겨 두는 시간(러닝 복구 24시간 + 여유). 지나면 끝내도 빈 목록

# 봇을 놓는 자리: 기준 사람에서 BOT_NEAR_M 앞 + (가운데 페이스 − 봇 페이스) × BOT_SPREAD_M_PER_KMH.
# 느린 봇은 앞, 빠른 봇은 뒤에 서서 혼자 뛰어도 따라잡거나 추월당한다(가설: 코스 6.7 km에 흩어 놓으면 거의 안 마주침)
BOT_NEAR_M = 25.0
BOT_SPREAD_M_PER_KMH = 40.0
NO_BOTS = BotPolicy(fill_to=0)

Clock = Callable[[], float]


def lap_position(d: float, length_m: float) -> float:
    """코스 위 위치 [0, 길이), 소수 2자리. 6670.996 → round하면 6671.0(= 길이)이 되므로 0으로 감는다."""
    if length_m <= 0:
        return 0.0
    r = round(d % length_m, 2)
    return 0.0 if r >= length_m else r


def loop_gap(a: float, b: float, length_m: float) -> float:
    """고리 코스 위 두 점 사이 가까운 쪽 거리."""
    x = abs(a - b) % length_m if length_m > 0 else 0.0
    return min(x, length_m - x)


def server_time_ms() -> int:
    return int(time.time() * 1000)


@dataclass(eq=False)
class Conn:
    """연결 하나. 보낼 이벤트는 outbox에 쌓이고, 연결 루프가 꺼내 보낸다."""
    user_id: str
    map_id: str
    wake: Optional[Callable[[], None]] = None  # 연결 루프 깨우기(다른 스레드·루프에서 불려도 됨)
    outbox: deque = field(default_factory=lambda: deque(maxlen=OUTBOX_MAX))
    replaced: bool = False  # 같은 사람이 다른 연결로 붙었거나 방에서 빠짐 → 이 연결은 닫는다

    def push(self, msg: dict) -> None:
        self.outbox.append(msg)
        self.notify()

    def retire(self) -> None:
        self.replaced = True
        self.notify()

    def notify(self) -> None:
        if self.wake is not None:
            try:
                self.wake()
            except RuntimeError:
                pass  # 그 연결의 루프가 이미 닫힘


@dataclass(eq=False)
class Runner:
    user_id: str
    nickname: str
    d: float = 0.0          # 코스 시작점부터 m, [0, 코스 길이)
    v: float = 0.0          # km/h
    running: bool = False
    run: Optional[float] = None  # 이번 달리기 거리(m, 러너 카드 D-12, 계획 0041). 달리기 전·옛 앱은 없음
    level: Optional[int] = None  # 입장 때 레벨(이름표·카드 Lv 배지, 계획 0059). 인증은 맵 밖이라 입장 동안 안 바뀜
    placed: bool = False    # 첫 state를 받음(그 전엔 다른 사람에게 안 보임)
    conn: Optional[Conn] = None
    disconnected_at: Optional[float] = None  # hub clock 기준

    def public(self) -> dict:
        p = {"userId": self.user_id, "nickname": self.nickname, **self.motion()}
        if self.level is not None:
            p["level"] = self.level
        return p

    def motion(self) -> dict:
        m = {"userId": self.user_id, "d": round(self.d, 2), "v": round(self.v, 2), "running": self.running}
        if self.run is not None:
            m["run"] = round(self.run, 1)
        return m


@dataclass(eq=False)
class Bot:
    """서버가 돌리는 페이스 봇. 늘 달리는 중이고 속도는 일정하다."""
    user_id: str
    nickname: str
    kmh: float
    d0: float  # 출발 위치(m)
    t0: float  # 출발 시각(hub clock)

    def d_at(self, now: float, length_m: float) -> float:
        return lap_position(self.d0 + self.kmh / 3.6 * (now - self.t0), length_m)

    def public(self, now: float, length_m: float) -> dict:
        return {**self.motion(now, length_m), "nickname": self.nickname}

    def motion(self, now: float, length_m: float) -> dict:
        return {"userId": self.user_id, "d": self.d_at(now, length_m), "v": round(self.kmh, 2), "running": True,
                "bot": True}


RUNNER, BOT = "RUNNER", "BOT"  # run_companions.kind(친구인지는 읽을 때 친구 표로 정함)


@dataclass(frozen=True)
class Companion:
    kind: str                # RUNNER, BOT
    user_id: Optional[str]   # 사람이면 사용자 ID, 봇이면 None
    label: str               # 사람은 그때 닉네임(읽을 땐 지금 닉네임을 씀), 봇은 "페이스 6:40"


@dataclass(eq=False)
class Track:
    """한 사람이 지금 모으는 러닝."""
    run_id: str
    started: float  # hub clock
    seen: dict[str, Companion] = field(default_factory=dict)  # 키(사람 ID, 봇은 "bot:페이스 이름") → 처음 본 순서


@dataclass(frozen=True)
class RunningNow:
    """지금 달리는 사람 한 명(친구 탭)."""
    course_id: str
    run_m: Optional[float]  # 이번 달리기 거리. 옛 앱·첫 state 전이면 None
    kmh: float


def bot_id(index: int) -> str:
    return f"bot_{index + 1}"  # 사람 ID(ULID: 숫자·대문자만)와 겹치지 않음


def pace_name(kmh: float) -> str:
    """1 km 페이스 이름: 9 km/h → "페이스 6:40"."""
    sec = round(3600 / kmh)
    return f"페이스 {sec // 60}:{sec % 60:02d}"


@dataclass(eq=False)
class Room:
    map_id: str
    course_id: str
    length_m: float
    runners: dict[str, Runner] = field(default_factory=dict)
    bots: dict[int, Bot] = field(default_factory=dict)  # 페이스 목록 번호 → 봇

    def bot_list(self) -> list[Bot]:
        return [self.bots[i] for i in sorted(self.bots)]


class Hub:
    def __init__(self, clock: Clock = time.monotonic, grace_s: float = GRACE_S, bots: BotPolicy = NO_BOTS,
                 rng: Optional[random.Random] = None):
        self._clock = clock
        self._grace_s = grace_s
        self._bots = bots
        self._rng = rng or random.Random()
        self._lock = threading.Lock()
        self._rooms: dict[str, Room] = {}
        self._where: dict[str, str] = {}  # 사용자 → 지금 있는 방(사람은 한 방에만)
        # 사용자 → 지금 러닝에서 같이 달린 상대. start_tracking 뒤에만 모음
        self._tracks: dict[str, Track] = {}
        # 지운 계정(계획 0058). 삭제 순간 다시 붙던 연결이 방에 되살아나지 않게 join이 바로 닫는다(ULID는 다시 안 쓰임)
        self._gone: set[str] = set()

    # ---------- 연결 ----------
    def join(self, map_id: str, course_id: str, length_m: float, user_id: str, nickname: str,
             wake: Optional[Callable[[], None]] = None, level: Optional[int] = None) -> tuple[Conn, dict]:
        """방에 붙는다. (새 연결, welcome 메시지). 같은 사람의 앞 연결은 닫게 표시(닫는 건 그 연결 루프가)."""
        conn = Conn(user_id=user_id, map_id=map_id, wake=wake)
        with self._lock:
            if user_id in self._gone:   # 지운 계정: 방에 넣지 않고 연결을 닫게 표시
                conn.replaced = True
                return conn, {"type": "welcome", "you": user_id, "mapInstanceId": map_id, "courseLengthM": float(length_m),
                              "serverTime": server_time_ms(), "runners": []}
            self._sweep_locked()
            old_map = self._where.get(user_id)
            if old_map is not None and old_map != map_id:
                self._remove_locked(old_map, user_id)  # 다른 맵으로 옮김 → 그 방엔 left
            room = self._rooms.get(map_id)
            if room is None:
                room = self._rooms[map_id] = Room(map_id=map_id, course_id=course_id, length_m=float(length_m))
            runner = room.runners.get(user_id)
            if runner is None:
                runner = room.runners[user_id] = Runner(user_id=user_id, nickname=nickname, level=level)
            else:  # 다시 붙음(끊긴 뒤 30초 안, 또는 앞 연결이 아직 안 닫힘): 알림 없이 이어짐
                if runner.conn is not None:
                    runner.conn.retire()
                runner.nickname = nickname
                runner.level = level
            runner.conn = conn
            runner.disconnected_at = None
            self._where[user_id] = map_id
            self._fill_bots_locked(room, except_user=user_id)  # 사람이 늘었으면 봇을 뺌(새로 붙은 사람은 welcome으로 받음)
            now = self._clock()
            welcome = {"type": "welcome", "you": user_id, "mapInstanceId": map_id, "courseLengthM": room.length_m,
                       "serverTime": server_time_ms(),
                       "runners": [r.public() for r in room.runners.values() if r.user_id != user_id and r.placed]
                       + [b.public(now, room.length_m) for b in room.bot_list()]}
        return conn, welcome

    def leave(self, conn: Conn, intentional: bool = False) -> bool:
        """연결이 끝남. 끊김이면 러너를 GRACE_S 동안 남기고, 앱이 leave로 나갔으면 바로 뺀다.
        다른 연결로 바뀐 경우(앞 연결)는 아무것도 안 함. 반환: leave로 방에서 뺐는지(그때만 자리를 지움)."""
        with self._lock:
            room = self._rooms.get(conn.map_id)
            runner = room.runners.get(conn.user_id) if room else None
            if runner is None or runner.conn is not conn:
                return False
            if intentional:
                runner.conn = None
                self._remove_locked(conn.map_id, conn.user_id)
                return True
            runner.conn = None
            runner.disconnected_at = self._clock()
            if self._grace_s <= 0:
                self._sweep_locked()
            return False

    def update(self, conn: Conn, d: float, v: float, running: bool, run=None) -> bool:
        """내 상태. 숫자가 아니거나(NaN 등) 이 연결이 이미 바뀌었으면 False. 첫 상태면 다른 사람에게 joined.
        run(이번 달리기 거리 m)은 선택: 숫자가 아니면 모르는 것으로(옛 앱·달리기 전)."""
        if not (_finite(d) and _finite(v)):
            return False
        with self._lock:
            room = self._rooms.get(conn.map_id)
            runner = room.runners.get(conn.user_id) if room else None
            if runner is None or runner.conn is not conn:
                return False
            started = bool(running) and not runner.running
            runner.d = lap_position(d, room.length_m)
            runner.v = min(max(v, 0.0), MAX_KMH)
            runner.running = bool(running)
            runner.run = min(max(float(run), 0.0), MAX_RUN_M) if _finite(run) else None
            if not runner.placed:
                runner.placed = True
                self._broadcast_locked(room, {"type": "joined", "runner": runner.public()}, except_user=runner.user_id)
            if started:
                self._fill_bots_locked(room)  # 달리는 사람이 생김 → 모자란 봇을 그 근처에
            if runner.running:
                self._note_companions_locked(room, runner)
        return True

    # ---------- 함께 달린 사람(기록 상세) ----------
    def start_tracking(self, user_id: str, run_id: str) -> None:
        """새 러닝을 시작함(POST /runs). 앞 러닝에서 모은 것은 버린다. 오래 버려진 다른 사람 것도 같이 지운다."""
        with self._lock:
            now = self._clock()
            for uid in [u for u, t in self._tracks.items() if now - t.started > TRACK_KEEP_S]:
                del self._tracks[uid]
            self._tracks[user_id] = Track(run_id=run_id, started=now)

    def companions(self, user_id: str, run_id: str) -> list[Companion]:
        """그 러닝에서 같이 달린 사람·봇(처음 본 순서). 지금 모으는 러닝이 아니면 빈 목록. 지우지 않음(커밋 뒤 stop_tracking)."""
        with self._lock:
            t = self._tracks.get(user_id)
            return list(t.seen.values()) if t is not None and t.run_id == run_id else []

    def stop_tracking(self, user_id: str, run_id: str) -> None:
        """그 러닝을 끝냈거나 버림. 그 사이 새 러닝을 시작했으면 그대로 둠."""
        with self._lock:
            t = self._tracks.get(user_id)
            if t is not None and t.run_id == run_id:
                del self._tracks[user_id]

    def forget_user(self, user_id: str) -> None:
        """계정 삭제(계획 0058): 방에서 바로 빼고(연결은 닫힘) 내 러닝 모으기를 버린다. 다시 붙어도 방에 넣지 않음.
        남의 러닝에 모인 나는 저장할 때 걸러짐(runs._save_companions가 없는 사용자를 뺌)."""
        with self._lock:
            self._gone.add(user_id)
            map_id = self._where.get(user_id)
            if map_id is not None:
                self._remove_locked(map_id, user_id)
            self._tracks.pop(user_id, None)

    def states(self, conn: Conn) -> Optional[dict]:
        """이 연결에 보낼 states(나 빼고 방의 보이는 러너). 방에서 빠졌으면 None."""
        with self._lock:
            self._sweep_locked()
            room = self._rooms.get(conn.map_id)
            if room is None or conn.user_id not in room.runners:
                return None
            now = self._clock()
            return {"type": "states", "serverTime": server_time_ms(),
                    "runners": [r.motion() for r in room.runners.values() if r.user_id != conn.user_id and r.placed]
                    + [b.motion(now, room.length_m) for b in room.bot_list()]}

    def drain(self, conn: Conn) -> list[dict]:
        out = []
        with self._lock:
            while conn.outbox:
                out.append(conn.outbox.popleft())
        return out

    # ---------- 입장·인원 API가 읽는 것 ----------
    def present_user_ids(self) -> set[str]:
        """어느 방이든 지금 붙어 있는 사람(끊긴 지 GRACE_S 안 포함). 어느 코스로 셀지는 자리(course_entries)가 정한다."""
        with self._lock:
            self._sweep_locked()
            return set(self._where.keys())

    def running_positions(self, course_id: str, except_user: Optional[str] = None) -> list[float]:
        """그 코스에서 붙어 있고 달리는 중인 사람들의 위치(m). 입장 위치 고르기용(끊긴 사람은 곧 빠질 수 있어 뺌)."""
        with self._lock:
            return [r.d for room in self._rooms.values() if room.course_id == course_id
                    for r in room.runners.values()
                    if r.placed and r.running and r.conn is not None and r.user_id != except_user]

    def in_map(self, user_id: str) -> Optional[tuple[str, float]]:
        """그 사람이 섬에 서 있으면(첫 state를 보냄) (코스, 지금 위치 m). 같이 달리기 입장(계획 0047): 잠깐 멈춤·3·2·1 전도,
        끊긴 지 GRACE_S 안도 포함(다른 폰엔 마지막 d·v로 계속 보이고 앱도 그렇게 내다봄, 러닝머신은 계속 돈다고 봄)."""
        with self._lock:
            self._sweep_locked()
            room = self._rooms.get(self._where.get(user_id, ""))
            r = room.runners.get(user_id) if room else None
            return (room.course_id, r.d) if r is not None and r.placed else None

    def running_now(self, user_ids) -> dict[str, RunningNow]:
        """그중 지금 섬에서 달리는 중인 사람(친구 탭, 계획 0044). 끊긴 지 GRACE_S 안이면 마지막 상태로 남긴다
        (헬스장 와이파이가 잠깐 끊길 때마다 친구 목록에서 깜빡이지 않게. 그 뒤엔 sweep이 뺌)."""
        wanted = set(user_ids)
        out: dict[str, RunningNow] = {}
        with self._lock:
            self._sweep_locked()
            for uid in wanted & self._where.keys():
                room = self._rooms.get(self._where[uid])
                r = room.runners.get(uid) if room else None
                if r is not None and r.placed and r.running:
                    out[uid] = RunningNow(course_id=room.course_id, run_m=r.run, kmh=r.v)
        return out

    def reset(self) -> None:
        """테스트용."""
        with self._lock:
            self._rooms.clear()
            self._where.clear()
            self._tracks.clear()
            self._gone.clear()

    # ---------- 안쪽(잠금을 잡은 채로만) ----------
    def _note_companions_locked(self, room: Room, me: Runner) -> None:
        """내가 달리는 중일 때 같은 방에서 달리는 중이고 붙어 있는 사람(섬에 보이는)·봇을 모은다.
        끊긴 사람은 30초 동안 마지막 자리에 멈춰 있어 뺀다(running_positions와 같은 이유)."""
        track = self._tracks.get(me.user_id)
        if track is None:
            return
        seen = track.seen
        people = sum(1 for c in seen.values() if c.kind == RUNNER)
        for r in room.runners.values():
            if people >= COMPANIONS_MAX:
                break
            if r.user_id != me.user_id and r.placed and r.running and r.conn is not None and r.user_id not in seen:
                seen[r.user_id] = Companion(kind=RUNNER, user_id=r.user_id, label=r.nickname)
                people += 1
        for b in room.bots.values():
            key = "bot:" + b.nickname  # 봇 id는 방마다 같은 번호를 다시 씀 → 페이스 이름으로
            if key not in seen:
                seen[key] = Companion(kind=BOT, user_id=None, label=b.nickname)

    def _sweep_locked(self) -> None:
        now = self._clock()
        for room in list(self._rooms.values()):
            gone = [uid for uid, r in room.runners.items()
                    if r.conn is None and r.disconnected_at is not None and now - r.disconnected_at >= self._grace_s]
            for uid in gone:
                self._remove_locked(room.map_id, uid)

    def _remove_locked(self, map_id: str, user_id: str) -> None:
        room = self._rooms.get(map_id)
        if room is None:
            return
        runner = room.runners.pop(user_id, None)
        if runner is None:
            return
        if runner.conn is not None:
            runner.conn.retire()
        if self._where.get(user_id) == map_id:
            del self._where[user_id]
        if runner.placed:
            self._broadcast_locked(room, {"type": "left", "userId": user_id})
        if not room.runners:
            del self._rooms[map_id]  # 봇도 같이 사라짐(알릴 사람이 없음)
        else:
            self._fill_bots_locked(room)  # 사람이 줄었으면 봇을 넣음

    def _fill_bots_locked(self, room: Room, except_user: Optional[str] = None) -> None:
        """사람 + 봇 = fill_to로 맞춘다.
        빼는 건 바로: 섬에 있고 붙어 있는 사람 모두에게서 가장 먼 봇부터(누가 옆에서 같이 뛰는 봇은 남김).
        넣는 건 달리는 중이고 붙어 있는 사람이 있을 때만(그 사람 근처에 놓으려고), 비어 있는 페이스 중 목록 앞쪽부터.
        없으면 누가 달리기 시작할 때(update) 넣는다."""
        paces = self._bots.paces_kmh
        want = min(len(paces), max(0, self._bots.fill_to - len(room.runners)))
        # 빼기 기준: 섬에 있고 붙어 있는 사람(끊긴 사람은 30초 동안 마지막 자리에 멈춰 있어 뺌, running_positions와 같은 이유)
        present = [r for r in room.runners.values() if r.placed and r.conn is not None]
        # 놓기 기준: 그중 달리는 사람(서 있는 사람 근처에 놓으면 그 사이 봇이 멀리 감)
        anchors = [r for r in present if r.running]
        now = self._clock()
        while len(room.bots) > want:
            def far(i: int) -> tuple[float, int]:
                d = room.bots[i].d_at(now, room.length_m)
                return (min((loop_gap(d, a.d, room.length_m) for a in present), default=0.0), i)
            gone = room.bots.pop(max(room.bots, key=far))
            self._broadcast_locked(room, {"type": "left", "userId": gone.user_id}, except_user=except_user)
        if len(room.bots) >= want or not anchors:
            return
        mid = sorted(paces)[len(paces) // 2]  # 이보다 느린 봇은 앞, 빠른 봇은 뒤
        for i in [i for i in range(len(paces)) if i not in room.bots][:want - len(room.bots)]:
            kmh = paces[i]
            anchor = self._rng.choice(anchors)
            bot = Bot(user_id=bot_id(i), nickname=pace_name(kmh), kmh=kmh,
                      d0=lap_position(anchor.d + BOT_NEAR_M + (mid - kmh) * BOT_SPREAD_M_PER_KMH, room.length_m),
                      t0=now)
            room.bots[i] = bot
            self._broadcast_locked(room, {"type": "joined", "runner": bot.public(now, room.length_m)},
                                   except_user=except_user)

    def _broadcast_locked(self, room: Room, msg: dict, except_user: Optional[str] = None) -> None:
        for r in room.runners.values():
            if r.conn is not None and r.user_id != except_user:
                r.conn.push(msg)


def _finite(x) -> bool:
    if not isinstance(x, (int, float)) or isinstance(x, bool):
        return False
    try:
        return math.isfinite(x)
    except OverflowError:  # 아주 큰 정수(JSON엔 자릿수 제한이 없음): float로 못 바꿈 → 숫자 아님으로
        return False


hub = Hub(bots=settings.bots)
