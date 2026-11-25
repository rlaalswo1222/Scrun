"""요청마다 서버 안에서 걸린 시간을 JSON 한 줄로 남긴다(계획 0070 관측).

    {"rid": "req_…", "method": "GET", "path": "/v1/runs/{runId}", "status": 200, "ms": 12.4, "db_n": 3, "db_ms": 8.1}

- ms: 미들웨어에 들어와서 응답 머리가 나갈 때까지(네트워크 왕복은 빠짐). db_n·db_ms: 그 안에서 DB에 보낸 쿼리 수·시간
- path는 라우트 틀(`/v1/runs/{runId}`)이라 기록 ID·사용자 ID가 남지 않는다. 라우트에 안 맞은 요청은 "-"
- WebSocket(/v1/realtime)은 HTTP 미들웨어를 지나지 않아 남지 않는다
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


class _DbStats:
    __slots__ = ("n", "ms")

    def __init__(self) -> None:
        self.n = 0
        self.ms = 0.0


# 요청 하나의 DB 통계. 동기 라우터는 스레드풀에서 돌지만 컨텍스트가 복사되어 같은 객체를 가리킨다
_current: ContextVar[Optional[_DbStats]] = ContextVar("scrun_request_db", default=None)


def _before_cursor_execute(conn, cursor, statement, parameters, context, executemany):
    conn.info.setdefault("scrun_query_start", []).append(time.perf_counter())


def _after_cursor_execute(conn, cursor, statement, parameters, context, executemany):
    starts = conn.info.get("scrun_query_start")
    if not starts:
        return
    elapsed = (time.perf_counter() - starts.pop()) * 1000
    stats = _current.get()
    if stats is not None:
        stats.n += 1
        stats.ms += elapsed


def install() -> None:
    """모든 엔진(운영·테스트)의 쿼리 시간을 잰다. 로그는 이 로거만 stdout으로(다른 로거 설정과 상관없이)."""
    if not event.contains(Engine, "before_cursor_execute", _before_cursor_execute):
        event.listen(Engine, "before_cursor_execute", _before_cursor_execute)
        event.listen(Engine, "after_cursor_execute", _after_cursor_execute)
    if not log.handlers:
        handler = logging.StreamHandler(sys.stdout)
        handler.setFormatter(logging.Formatter("%(message)s"))
        log.addHandler(handler)
        log.setLevel(logging.INFO)
        log.propagate = False


def _route_path(request: Request) -> str:
    route = request.scope.get("route")
    return getattr(route, "path", None) or "-"


def _emit(request: Request, status: int, started: float, stats: _DbStats) -> None:
    line = {
        "rid": getattr(request.state, "request_id", None),
        "method": request.method,
        "path": _route_path(request),
        "status": status,
        "ms": round((time.perf_counter() - started) * 1000, 1),
        "db_n": stats.n,
        "db_ms": round(stats.ms, 1),
    }
    log.info(json.dumps(line, ensure_ascii=False, separators=(",", ":")))


async def request_log_middleware(request: Request, call_next):
    started = time.perf_counter()
    stats = _DbStats()
    token = _current.set(stats)
    try:
        response = await call_next(request)
    except Exception:
        _emit(request, 500, started, stats)   # 처리 못 한 예외는 바깥(ServerErrorMiddleware)이 500으로 바꿈
        raise
    finally:
        _current.reset(token)
    _emit(request, response.status_code, started, stats)
    return response
