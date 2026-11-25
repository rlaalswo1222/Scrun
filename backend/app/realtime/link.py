"""일반 API가 실시간 방 상태(허브)를 읽고 쓰는 곳(계획 0073). 라우터는 `link.hub_link`로 부른다(테스트가 이 한 이름만 바꿔 끼움).

- REALTIME_URL이 없으면(로컬 개발·테스트·한 프로세스 운영) 같은 프로세스의 허브 그대로(hub_link is hub)
- 있으면(일반 API를 프로세스 여러 개로 띄움) 허브는 실시간 프로세스 하나에만 있고, 여기서는 그 프로세스의 내부 API
  (app/realtime/internal.py)를 HTTP로 부른다. 허브 함수와 같은 이름·같은 반환 형식
- 실패는 둘로 나눈다(리뷰: 느릴 때와 죽었을 때를 같게 보면 정원을 넘김)
  - 죽음(연결 안 됨): 실시간이 죽었으면 방에 붙은 사람도 없다(연결이 끊김) → "방에 아무도 없음"으로 진행. 정원은 DB의 입장 2분 안
    사람만(occupancy), 친구 "달리는 중"·입장 위치는 빈 값, 함께 달린 사람은 빈 목록, 쓰기는 기록만 남김
  - 바쁨(시간 초과·200이 아닌 답): 방에 사람이 있을 수 있음 → 보통은 같은 대체 값, 단 strict 호출(맵 입장 정원)은 HubBusy를 던져
    503으로 잠시 거절(정원 20을 넘겨 들이지 않게)
- 서킷 브레이커: 한 번 실패하면 잠깐(DOWN_S·BUSY_S) 부르지 않고 바로 그 결과로 답함. 실시간이 죽은 동안 허브를 부르는 요청마다
  연결 실패를 기다리지 않게(0073 측정: 실시간을 끈 채 S1에서 홈 p50 37 → 80 ms)
"""
import hashlib
import hmac
import logging
import threading
import time
from typing import Callable, Optional

import httpx

from ..core.config import settings
from .hub import Companion, RunningNow, hub

log = logging.getLogger("scrun.realtime.link")
INTERNAL_HEADER = "X-Scrun-Internal"
INTERNAL_PREFIX = "/internal/hub/"
CONNECT_TIMEOUT_S = 0.3   # 같은 기계 안. 이보다 길면 죽은 것으로
READ_TIMEOUT_S = 1.0      # 허브 함수는 ms 단위. 넘으면 막힌 것(바쁨)으로
DOWN_S = 5.0              # 연결이 안 되면 이만큼 부르지 않음
BUSY_S = 2.0              # 시간 초과·오류 답이면 이만큼 부르지 않음


def internal_token(jwt_secret: str) -> str:
    """내부 API 비밀. 두 프로세스가 같은 JWT_SECRET을 가지므로 새 환경 변수 없이 같은 값을 만든다(토큰 서명 키와는 다른 값)."""
    return hmac.new(jwt_secret.encode(), b"scrun-internal-hub-v1", hashlib.sha256).hexdigest()


class HubBusy(Exception):
    """실시간 프로세스가 살아 있는데 제때 답하지 않음(strict 호출만 받음). 라우터가 503으로."""


class _BadAnswer(Exception):
    """내부 API가 200이 아닌 답(틀린 비밀·인자, 실시간 프로세스 오류)."""


DOWN, BUSY = "down", "busy"


class RemoteHub:
    """실시간 프로세스의 허브를 HTTP로. httpx.Client는 스레드끼리 같이 써도 됨(동기 라우터는 스레드풀에서 부름)."""

    def __init__(self, base_url: str, token: str, client: Optional[httpx.Client] = None,
                 clock: Callable[[], float] = time.monotonic):
        """client: 테스트가 앱에 바로 붙는 클라이언트(TestClient)를 넣을 때."""
        self._client = client or httpx.Client(
            base_url=base_url.rstrip("/"), timeout=httpx.Timeout(READ_TIMEOUT_S, connect=CONNECT_TIMEOUT_S))
        self._headers = {INTERNAL_HEADER: token}
        self._clock = clock
        self._lock = threading.Lock()
        self._open_until = 0.0
        self._open_kind: Optional[str] = None

    def _failed(self, kind: str) -> None:
        with self._lock:
            self._open_kind = kind
            self._open_until = self._clock() + (DOWN_S if kind == DOWN else BUSY_S)

    def _open(self) -> Optional[str]:
        with self._lock:
            return self._open_kind if self._clock() < self._open_until else None

    def _call(self, op: str, fallback, strict: bool = False, **args):
        kind = self._open()
        if kind is None:
            try:
                r = self._client.post(INTERNAL_PREFIX + op, json=args, headers=self._headers)
                if r.status_code != 200:   # raise_for_status 대신: 테스트 클라이언트는 다른 패키지의 예외를 던짐
                    raise _BadAnswer(f"HTTP {r.status_code}")
                return r.json()
            except (httpx.ConnectError, httpx.ConnectTimeout) as e:
                kind = DOWN
                self._failed(kind)
                self._log(op, kind, e)
            except (httpx.HTTPError, _BadAnswer, ValueError) as e:
                kind = BUSY
                self._failed(kind)
                self._log(op, kind, e)
        if strict and kind == BUSY:
            raise HubBusy(op)
        return fallback

    @staticmethod
    def _log(op: str, kind: str, e: Exception) -> None:
        level = logging.ERROR if op == "forget_user" else logging.WARNING
        log.log(level, "실시간 프로세스에 %s를 못 물음(%s, %s: %s) — %.0f초 동안 허브 없이 진행", op,
                "연결 안 됨" if kind == DOWN else "시간 초과·오류", type(e).__name__, e, DOWN_S if kind == DOWN else BUSY_S)

    # ---------- 읽기 ----------
    def present_user_ids(self, strict: bool = False) -> set[str]:
        """strict: 맵 입장 정원 검사. 실시간이 바쁘면 HubBusy(빈 값으로 정원을 넘기지 않게)."""
        return set(self._call("present_user_ids", {"ids": []}, strict=strict)["ids"])

    def running_positions(self, course_id: str, except_user: Optional[str] = None) -> list[float]:
        return self._call("running_positions", {"positions": []}, course_id=course_id, except_user=except_user)["positions"]

    def in_map(self, user_id: str) -> Optional[tuple[str, float]]:
        where = self._call("in_map", {"where": None}, user_id=user_id)["where"]
        return (where[0], where[1]) if where else None

    def running_now(self, user_ids) -> dict[str, RunningNow]:
        got = self._call("running_now", {"running": {}}, user_ids=sorted(set(user_ids)))["running"]
        return {uid: RunningNow(course_id=r["course_id"], run_m=r["run_m"], kmh=r["kmh"]) for uid, r in got.items()}

    def companions(self, user_id: str, run_id: str) -> list[Companion]:
        got = self._call("companions", {"companions": []}, user_id=user_id, run_id=run_id)["companions"]
        return [Companion(kind=c["kind"], user_id=c["user_id"], label=c["label"]) for c in got]

    # ---------- 쓰기 ----------
    def start_tracking(self, user_id: str, run_id: str) -> None:
        self._call("start_tracking", {}, user_id=user_id, run_id=run_id)

    def stop_tracking(self, user_id: str, run_id: str) -> None:
        self._call("stop_tracking", {}, user_id=user_id, run_id=run_id)

    def forget_user(self, user_id: str) -> None:
        self._call("forget_user", {}, user_id=user_id)


hub_link = hub if settings.realtime_url is None else RemoteHub(settings.realtime_url, internal_token(settings.jwt_secret))
