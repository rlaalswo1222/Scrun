"""동시 처리 제한·빠른 거절(계획 0071, 설명 docs/plans/0071-overload.html).

HTTP 요청은 번호표(비동기 세마포어) N장 중 하나를 받아야 라우터로 간다. 번호표를 기다리는 동안은 스레드·DB 연결을 쥐지 않는다.
- 번호표는 라우터가 **응답을 다 보내고 get_db 세션까지 닫은 뒤** 반납한다(순수 ASGI: `await self.app(...)`이 끝난 뒤).
  BaseHTTPMiddleware의 call_next는 응답 머리에서 돌아와, 그때 반납하면 연결을 쥔 요청이 번호표 밖에 생김(0071 리뷰)
- 그래서 HTTP 요청이 쥔 DB 연결 ≤ N. HTTP 요청은 get_db 세션 하나 = 연결 하나. 실시간 WebSocket은 이 미들웨어를 안 지나
  풀의 나머지(풀 합계 − N)를 입장·퇴장 때 짧게 씀 — 몰리면 실시간이 그 몫을 넘을 수 있음(가설, S3로 확인)
- W초 안에 못 받으면 바로 503 SERVICE_UNAVAILABLE(details.reason BUSY) + Retry-After: 1 → 받아 둔 요청이 W초치 넘게 쌓이지 않음
- 컨테이너 안 헬스체크(루프백 + X-Scrun-Probe: 1)는 제한 밖: 몰려도 컨테이너가 죽은 것으로 보이지 않게. 밖에서 온 헤더는 무시
- 내부 API(/internal/*, 계획 0073)도 제한 밖: 일반 API 요청이 이미 번호표를 쥐고 부르므로 두 번 줄 세우지 않음
기다린 시간은 요청 로그 queue_ms(app/core/request_log.py). 설정 ADMISSION_LIMIT(0이면 끔)·ADMISSION_WAIT_S
"""
import asyncio
import time
import weakref

from fastapi import Request
from starlette.types import ASGIApp, Receive, Scope, Send

from .config import settings
from .envelope import error_response
from .errors import ErrorCode
from .request_log import add_queue_wait, is_internal, is_local_probe

RETRY_AFTER_S = "1"


class AdmissionMiddleware:
    def __init__(self, app: ASGIApp, limit: int, wait_s: float):
        self.app = app
        self.limit = limit
        self.wait_s = wait_s
        # 이벤트 루프마다 번호표 묶음 하나. asyncio.Semaphore는 처음 기다린 루프에 묶여서, 루프가 여럿인 곳(테스트의 TestClient
        # 여러 개)에서 하나를 같이 쓰면 오류가 남. 운영은 프로세스마다 uvicorn 루프 하나·연결 풀 하나라 결과가 같음
        self._gates: "weakref.WeakKeyDictionary[asyncio.AbstractEventLoop, asyncio.Semaphore]" = weakref.WeakKeyDictionary()

    def _gate(self) -> asyncio.Semaphore:
        loop = asyncio.get_running_loop()
        gate = self._gates.get(loop)
        if gate is None:
            gate = self._gates[loop] = asyncio.Semaphore(self.limit)
        return gate

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http" or is_local_probe(scope) or is_internal(scope):
            await self.app(scope, receive, send)
            return
        gate = self._gate()
        t0 = time.perf_counter()
        try:
            await asyncio.wait_for(gate.acquire(), timeout=self.wait_s)
        except asyncio.TimeoutError:
            add_queue_wait(time.perf_counter() - t0)
            response = error_response(Request(scope), ErrorCode.SERVICE_UNAVAILABLE, {"reason": "BUSY"})
            response.headers["Retry-After"] = RETRY_AFTER_S
            await response(scope, receive, send)
            return
        add_queue_wait(time.perf_counter() - t0)
        try:
            await self.app(scope, receive, send)
        finally:
            gate.release()


ADMISSION_KWARGS = {"limit": settings.admission.limit, "wait_s": settings.admission.wait_s}
