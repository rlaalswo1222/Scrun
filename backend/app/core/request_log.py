"""요청마다 서버 안에서 걸린 시간을 JSON 한 줄로 남긴다(계획 0070 관측).

    {"rid": "req_…", "method": "GET", "path": "/v1/runs/{runId}", "status": 200, "ms": 12.4,
     "db_n": 3, "db_ms": 8.1, "pool_ms": 0.2, "queue_ms": 0.0}

- 요청 ID(X-Request-Id)도 같은 미들웨어(RequestContextMiddleware)가 붙인다. 로그를 꺼도 요청 ID는 붙음
- ms: 미들웨어에 들어와서 응답 머리가 나갈 때까지(네트워크 왕복은 빠짐). 스트리밍 본문을 내보내는 시간·그동안의 쿼리는 빠짐
- db_n·db_ms: 그 안에서 DB에 보낸 쿼리 수·시간(실패한 쿼리 포함). pool_ms: 연결 풀에서 연결을 받기까지 기다린 시간(app/db/session.py의 풀이 잼)
- queue_ms: 입구 번호표를 기다린 시간(app/core/admission.py, 계획 0071). 못 받아 503이면 path는 "-"(라우팅 전)
- path는 라우트 틀(`/v1/runs/{runId}`)이라 기록 ID·사용자 ID가 남지 않는다. 라우트에 안 맞은 요청은 "-"
- 남기지 않는 것: WebSocket(/v1/realtime, HTTP 미들웨어를 안 지남), 컨테이너 안 헬스체크(루프백 + X-Scrun-Probe: 1, is_local_probe)
- 서버 로그(운영은 journald)로 본다: `journalctl CONTAINER_NAME=scrun-api-1 --since … | grep '"path"'`
- 끄기: 환경 변수 REQUEST_LOG=0
"""
import json
import logging
import sys
import time
import uuid
from contextvars import ContextVar
from typing import Optional

from sqlalchemy import event
from sqlalchemy.engine import Engine
from starlette.types import ASGIApp, Message, Receive, Scope, Send

log = logging.getLogger("scrun.request")
PROBE_HEADER = b"x-scrun-probe"
LOOPBACK = {"127.0.0.1", "::1"}


def is_local_probe(scope) -> bool:
    """컨테이너 안 헬스체크(compose healthcheck가 localhost로 X-Scrun-Probe: 1). 밖에서 붙인 헤더는 무시(Caddy도 지움):
    헤더만 보면 누구나 번호표·요청 로그를 건너뜀(0071 리뷰). uvicorn --proxy-headers라 Caddy를 거친 요청의 client는 실제 IP"""
    client = scope.get("client")
    if not client or client[0] not in LOOPBACK:
        return False
    return any(k == PROBE_HEADER and v == b"1" for k, v in scope.get("headers", ()))


class _RequestStats:
    __slots__ = ("started", "db_n", "db_ms", "pool_ms", "queue_ms")

    def __init__(self) -> None:
        self.started = time.perf_counter()
        self.db_n = 0
        self.db_ms = 0.0
        self.pool_ms = 0.0
        self.queue_ms = 0.0

    def elapsed_ms(self) -> float:
        return (time.perf_counter() - self.started) * 1000


# 지금 처리 중인 요청의 통계. 동기 라우터는 스레드풀에서 돌지만 컨텍스트가 복사되어 같은 객체를 가리킨다
_request_stats: ContextVar[Optional[_RequestStats]] = ContextVar("scrun_request_stats", default=None)


def _before_cursor_execute(conn, cursor, statement, parameters, context, executemany):
    if context is not None:
        context._scrun_t0 = time.perf_counter()


def _count_query(context) -> None:
    t0 = getattr(context, "_scrun_t0", None)
    stats = _request_stats.get()
    if t0 is None or stats is None:
        return
    stats.db_n += 1
    stats.db_ms += (time.perf_counter() - t0) * 1000


def _after_cursor_execute(conn, cursor, statement, parameters, context, executemany):
    _count_query(context)


def _handle_error(exception_context):
    _count_query(exception_context.execution_context)   # 실패한 쿼리도 시간은 썼다


def add_pool_wait(seconds: float) -> None:
    stats = _request_stats.get()
    if stats is not None:
        stats.pool_ms += seconds * 1000


def add_queue_wait(seconds: float) -> None:
    stats = _request_stats.get()
    if stats is not None:
        stats.queue_ms += seconds * 1000


def install_request_log() -> None:
    """모든 엔진(운영·테스트)의 쿼리 시간을 잰다. 로그는 이 로거만 stdout으로(다른 로거 설정과 상관없이)."""
    if not event.contains(Engine, "before_cursor_execute", _before_cursor_execute):
        event.listen(Engine, "before_cursor_execute", _before_cursor_execute)
        event.listen(Engine, "after_cursor_execute", _after_cursor_execute)
        event.listen(Engine, "handle_error", _handle_error)
    if not log.handlers:
        handler = logging.StreamHandler(sys.stdout)
        handler.setFormatter(logging.Formatter("%(message)s"))
        log.addHandler(handler)
        log.setLevel(logging.INFO)
        log.propagate = False


def _route_path(scope: Scope) -> str:
    route = scope.get("route")
    return getattr(route, "path", None) or "-"


def _emit(scope: Scope, status: int, stats: _RequestStats) -> None:
    line = {
        "rid": scope.get("state", {}).get("request_id"),
        "method": scope["method"],
        "path": _route_path(scope),
        "status": status,
        "ms": round(stats.elapsed_ms(), 1),
        "db_n": stats.db_n,
        "db_ms": round(stats.db_ms, 1),
        "pool_ms": round(stats.pool_ms, 1),
        "queue_ms": round(stats.queue_ms, 1),
    }
    log.info(json.dumps(line, ensure_ascii=False, separators=(",", ":")))


REQUEST_ID_HEADER = b"x-request-id"


class RequestContextMiddleware:
    """요청 ID(응답 머리 X-Request-Id, 껍데기 meta.requestId)와 요청 로그 한 줄. 순수 ASGI(계획 0073 측정).

    예전엔 둘이 `@app.middleware("http")`(BaseHTTPMiddleware) 두 겹이었다. 그 방식은 요청마다 안쪽 앱을 새 작업으로 띄우고
    응답을 메모리 통로로 옮겨 담아서, S1 100명 py-spy에서 이벤트 루프 CPU의 큰 몫(약 27%)이 그 처리였다.
    여기서는 send만 감싸 응답 머리가 지나갈 때 상태를 보고 머리를 하나 붙인다(새 작업·통로·복사 없음).
    - 요청 ID는 scope["state"]에 둠 → 라우터·오류 처리기의 request.state.request_id(Starlette Request.state가 같은 dict)
    - 로그 시간(ms)은 예전처럼 응답 머리가 나갈 때까지. 처리 못 한 예외는 바깥(ServerErrorMiddleware)이 500으로 바꾸므로
      머리가 나가기 전에 터지면 500으로 남기고 다시 던짐(그 500 응답엔 X-Request-Id가 없음, 예전과 같음)
    - WebSocket(/v1/realtime)은 그대로 통과. 컨테이너 헬스체크(is_local_probe)는 요청 ID만 붙이고 로그는 안 남김
    """

    def __init__(self, app: ASGIApp, log_requests: bool = True):
        self.app = app
        self.log_requests = log_requests

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        rid = "req_" + uuid.uuid4().hex[:12]
        scope.setdefault("state", {})["request_id"] = rid
        rid_header = (REQUEST_ID_HEADER, rid.encode("ascii"))
        stats = _RequestStats() if self.log_requests and not is_local_probe(scope) else None
        started = False

        async def send_with_id(message: Message) -> None:
            nonlocal started
            if message["type"] == "http.response.start":
                started = True
                message = {**message, "headers": [*message.get("headers", ()), rid_header]}
                if stats is not None:
                    _emit(scope, message["status"], stats)
            await send(message)

        if stats is None:
            await self.app(scope, receive, send_with_id)
            return
        token = _request_stats.set(stats)
        try:
            await self.app(scope, receive, send_with_id)
        except Exception:
            if not started:
                _emit(scope, 500, stats)
            raise
        finally:
            _request_stats.reset(token)
