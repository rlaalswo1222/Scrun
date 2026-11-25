"""일반 API가 실시간 방 상태(허브)를 읽고 쓰는 곳(계획 0073). 라우터는 hub 대신 hub_link를 쓴다.

- REALTIME_URL이 없으면(로컬 개발·테스트·한 프로세스 운영) 같은 프로세스의 허브 그대로(hub_link is hub)
- 있으면(일반 API를 프로세스 여러 개로 띄움) 허브는 실시간 프로세스 하나에만 있고, 여기서는 그 프로세스의 내부 API
  (app/realtime/internal.py)를 HTTP로 부른다. 허브 함수와 같은 이름·같은 반환 형식
- 실시간 프로세스가 죽었거나 느리면(TIMEOUT_S) 기능을 멈추지 않고 "방에 아무도 없음"으로 진행한다. 실시간이 죽었으면 실제로
  방에 붙은 사람도 없다(연결이 끊김): 정원은 DB의 입장 2분 안 사람만(occupancy), 친구 "달리는 중"·입장 위치는 빈 값,
  함께 달린 사람은 빈 목록. 쓰기(start·stop_tracking, forget_user)는 못 하면 기록만 남김
"""
import hashlib
import hmac
import logging
from typing import Optional

import httpx

from ..core.config import settings
from .hub import Companion, RunningNow, hub

log = logging.getLogger("scrun.realtime.link")
INTERNAL_HEADER = "X-Scrun-Internal"
TIMEOUT_S = 1.0   # 같은 기계 안 왕복은 ms 단위. 넘으면 실시간 프로세스가 막힌 것으로 보고 허브 없이 진행


def internal_token(jwt_secret: str) -> str:
    """내부 API 비밀. 두 프로세스가 같은 JWT_SECRET을 가지므로 새 환경 변수 없이 같은 값을 만든다(토큰 서명 키와는 다른 값)."""
    return hmac.new(jwt_secret.encode(), b"scrun-internal-hub-v1", hashlib.sha256).hexdigest()


class _BadAnswer(Exception):
    """내부 API가 200이 아닌 답(틀린 비밀·인자, 실시간 프로세스 오류)."""


class RemoteHub:
    """실시간 프로세스의 허브를 HTTP로. httpx.Client는 스레드끼리 같이 써도 됨(동기 라우터는 스레드풀에서 부름)."""

    def __init__(self, base_url: str, token: str, timeout_s: float = TIMEOUT_S, client: Optional[httpx.Client] = None):
        """client: 테스트가 앱에 바로 붙는 클라이언트(TestClient)를 넣을 때."""
        self._client = client or httpx.Client(base_url=base_url.rstrip("/"), timeout=timeout_s)
        self._headers = {INTERNAL_HEADER: token}

    def _call(self, op: str, fallback, **args):
        try:
            r = self._client.post(f"/internal/hub/{op}", json=args, headers=self._headers)
            if r.status_code != 200:   # raise_for_status 대신: 테스트 클라이언트는 다른 패키지의 예외를 던짐
                raise _BadAnswer(f"HTTP {r.status_code}")
            return r.json()
        except (httpx.HTTPError, _BadAnswer, ValueError) as e:
            level = logging.ERROR if op == "forget_user" else logging.WARNING
            log.log(level, "실시간 프로세스에 %s를 못 물음(%s: %s) — 허브 없이 진행", op, type(e).__name__, e)
            return fallback

    # ---------- 읽기 ----------
    def present_user_ids(self) -> set[str]:
        return set(self._call("present_user_ids", {"ids": []})["ids"])

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
