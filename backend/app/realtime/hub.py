"""실시간 방 상태(계획 0029, ADR-0010). 맵(= 코스) 하나가 방 하나, 상태는 이 프로세스 메모리에만 둔다.

- 서버는 uvicorn 한 프로세스(ADR-0010). 여러 대로 늘릴 때 방 상태를 Redis 같은 공유 저장소로 옮긴다
- 잠금은 threading.Lock 하나. 연결 처리(이벤트 루프)와 입장·인원 API(스레드풀)가 같이 읽고 쓰기 때문.
  다른 연결에 알릴 것(joined·left)은 그 연결의 outbox(deque)에 넣고 wake()로 그 연결의 루프를 깨운다
  (연결마다 루프가 다를 수 있어 asyncio 객체를 공유하지 않음: 테스트 클라이언트는 연결마다 루프가 다름)
- 붙은 뒤 첫 state가 오기 전(섬을 불러오는 중)에는 다른 사람에게 안 보인다(placed). 첫 state 때 joined
- 끊긴 러너는 GRACE_S 동안 방에 남는다(그 사이 다시 붙으면 joined·left 없이 이어짐). 지나면 sweep이 left를 알린다.
  앱이 leave를 보내고 나가면 바로 left
"""
from __future__ import annotations

import math
import threading
import time
from collections import deque
from dataclasses import dataclass, field
from typing import Callable, Optional

GRACE_S = 30.0       # 끊긴 러너를 남겨 두는 시간(헬스장 와이파이 끊김 대비, 계획 0029)
MAX_KMH = 30.0       # 받아들이는 속도 상한(그 위는 잘라냄)
OUTBOX_MAX = 256     # 못 보낸 이벤트가 이만큼 쌓이면 오래된 것부터 버림(느린 연결이 메모리를 잡지 않게)

Clock = Callable[[], float]


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
    placed: bool = False    # 첫 state를 받음(그 전엔 다른 사람에게 안 보임)
    conn: Optional[Conn] = None
    disconnected_at: Optional[float] = None  # hub clock 기준

    def public(self) -> dict:
        return {"userId": self.user_id, "nickname": self.nickname, "d": round(self.d, 2), "v": round(self.v, 2),
                "running": self.running}

    def motion(self) -> dict:
        return {"userId": self.user_id, "d": round(self.d, 2), "v": round(self.v, 2), "running": self.running}


@dataclass(eq=False)
class Room:
    map_id: str
    course_id: str
    length_m: float
    runners: dict[str, Runner] = field(default_factory=dict)


class Hub:
    def __init__(self, clock: Clock = time.monotonic, grace_s: float = GRACE_S):
        self._clock = clock
        self._grace_s = grace_s
        self._lock = threading.Lock()
        self._rooms: dict[str, Room] = {}
        self._where: dict[str, str] = {}  # 사용자 → 지금 있는 방(사람은 한 방에만)

    # ---------- 연결 ----------
    def join(self, map_id: str, course_id: str, length_m: float, user_id: str, nickname: str,
             wake: Optional[Callable[[], None]] = None) -> tuple[Conn, dict]:
        """방에 붙는다. (새 연결, welcome 메시지). 같은 사람의 앞 연결은 닫게 표시(닫는 건 그 연결 루프가)."""
        conn = Conn(user_id=user_id, map_id=map_id, wake=wake)
        with self._lock:
            self._sweep_locked()
            old_map = self._where.get(user_id)
            if old_map is not None and old_map != map_id:
                self._remove_locked(old_map, user_id)  # 다른 맵으로 옮김 → 그 방엔 left
            room = self._rooms.get(map_id)
            if room is None:
                room = self._rooms[map_id] = Room(map_id=map_id, course_id=course_id, length_m=float(length_m))
            runner = room.runners.get(user_id)
            if runner is None:
                runner = room.runners[user_id] = Runner(user_id=user_id, nickname=nickname)
            else:  # 다시 붙음(끊긴 뒤 30초 안, 또는 앞 연결이 아직 안 닫힘): 알림 없이 이어짐
                if runner.conn is not None:
                    runner.conn.retire()
                runner.nickname = nickname
            runner.conn = conn
            runner.disconnected_at = None
            self._where[user_id] = map_id
            welcome = {"type": "welcome", "you": user_id, "mapInstanceId": map_id, "courseLengthM": room.length_m,
                       "serverTime": server_time_ms(),
                       "runners": [r.public() for r in room.runners.values() if r.user_id != user_id and r.placed]}
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

    def update(self, conn: Conn, d: float, v: float, running: bool) -> bool:
        """내 상태. 숫자가 아니거나(NaN 등) 이 연결이 이미 바뀌었으면 False. 첫 상태면 다른 사람에게 joined."""
        if not (_finite(d) and _finite(v)):
            return False
        with self._lock:
            room = self._rooms.get(conn.map_id)
            runner = room.runners.get(conn.user_id) if room else None
            if runner is None or runner.conn is not conn:
                return False
            runner.d = d % room.length_m if room.length_m > 0 else 0.0
            runner.v = min(max(v, 0.0), MAX_KMH)
            runner.running = bool(running)
            if not runner.placed:
                runner.placed = True
                self._broadcast_locked(room, {"type": "joined", "runner": runner.public()}, except_user=runner.user_id)
        return True

    def states(self, conn: Conn) -> Optional[dict]:
        """이 연결에 보낼 states(나 빼고 방의 보이는 러너). 방에서 빠졌으면 None."""
        with self._lock:
            self._sweep_locked()
            room = self._rooms.get(conn.map_id)
            if room is None or conn.user_id not in room.runners:
                return None
            return {"type": "states", "serverTime": server_time_ms(),
                    "runners": [r.motion() for r in room.runners.values() if r.user_id != conn.user_id and r.placed]}

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

    def reset(self) -> None:
        """테스트용."""
        with self._lock:
            self._rooms.clear()
            self._where.clear()

    # ---------- 안쪽(잠금을 잡은 채로만) ----------
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
            del self._rooms[map_id]

    def _broadcast_locked(self, room: Room, msg: dict, except_user: Optional[str] = None) -> None:
        for r in room.runners.values():
            if r.conn is not None and r.user_id != except_user:
                r.conn.push(msg)


def _finite(x) -> bool:
    return isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x)


hub = Hub()
