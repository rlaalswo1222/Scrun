"""요청마다 서버 안에서 걸린 시간을 JSON 한 줄로 남긴다(계획 0070 관측).

    {"rid": "req_…", "method": "GET", "path": "/v1/runs/{runId}", "status": 200, "ms": 12.4,
     "db_n": 3, "db_ms": 8.1, "pool_ms": 0.2, "queue_ms": 0.0}

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
from contextvars import ContextVar
from typing import Optional

from fastapi import Request
from sqlalchemy import event
from sqlalchemy.engine import Engine

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


def _route_path(request: Request) -> str:
    route = request.scope.get("route")
    return getattr(route, "path", None) or "-"


def _emit(request: Request, status: int, stats: _RequestStats) -> None:
    line = {
        "rid": getattr(request.state, "request_id", None),
        "method": request.method,
        "path": _route_path(request),
        "status": status,
        "ms": round(stats.elapsed_ms(), 1),
        "db_n": stats.db_n,
        "db_ms": round(stats.db_ms, 1),
        "pool_ms": round(stats.pool_ms, 1),
        "queue_ms": round(stats.queue_ms, 1),
    }
    log.info(json.dumps(line, ensure_ascii=False, separators=(",", ":")))


async def request_log_middleware(request: Request, call_next):
    if is_local_probe(request.scope):   # 컨테이너 헬스체크(10초마다)는 기준선을 흐리므로 남기지 않음
        return await call_next(request)
    stats = _RequestStats()
    token = _request_stats.set(stats)
    try:
        response = await call_next(request)
    except Exception:
        _emit(request, 500, stats)   # 처리 못 한 예외는 바깥(ServerErrorMiddleware)이 500으로 바꿈
        raise
    finally:
        _request_stats.reset(token)
    _emit(request, response.status_code, stats)
    return response
