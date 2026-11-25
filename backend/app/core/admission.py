"""동시 처리 제한·빠른 거절(계획 0071, 설명 docs/plans/0071-overload.html).

HTTP 요청은 번호표(비동기 세마포어) N장 중 하나를 받아야 라우터로 간다. 번호표를 기다리는 동안은 스레드·DB 연결을 쥐지 않는다.
- 안에 있는 요청 ≤ N ≤ DB 연결 수 → "연결을 쥔 요청은 다음 단계 스레드를, 스레드는 연결을" 서로 기다리는 멈춤이 생길 수 없음
  (HTTP 요청은 get_db 세션 하나 = 연결 하나. 실시간 WebSocket은 이 미들웨어를 안 지나고 남은 연결을 짧게 씀)
- W초 안에 못 받으면 바로 503 SERVICE_UNAVAILABLE + Retry-After: 1 → 받아 둔 요청이 끝없이 쌓이지 않음(기다림은 최대 W초)
- 헬스체크(X-Scrun-Probe: 1)는 제한 밖: 몰려도 컨테이너가 죽은 것으로 보이지 않게
기다린 시간은 요청 로그 queue_ms(app/core/request_log.py). 설정 ADMISSION_LIMIT(0이면 끔)·ADMISSION_WAIT_S
"""
import asyncio
import time
import weakref

from fastapi import Request

from .config import settings
from .envelope import error_response
from .errors import ErrorCode
from .request_log import PROBE_HEADER, add_queue_wait

RETRY_AFTER_S = "1"


class Admission:
    def __init__(self, limit: int, wait_s: float):
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

    async def __call__(self, request: Request, call_next):
        if request.headers.get(PROBE_HEADER) == "1":
            return await call_next(request)
        gate = self._gate()
        t0 = time.perf_counter()
        try:
            await asyncio.wait_for(gate.acquire(), timeout=self.wait_s)
        except asyncio.TimeoutError:
            add_queue_wait(time.perf_counter() - t0)
            response = error_response(request, ErrorCode.SERVICE_UNAVAILABLE, {"reason": "BUSY"})
            response.headers["Retry-After"] = RETRY_AFTER_S
            return response
        add_queue_wait(time.perf_counter() - t0)
        try:
            return await call_next(request)
        finally:
            gate.release()


admission = Admission(settings.admission.limit, settings.admission.wait_s)
