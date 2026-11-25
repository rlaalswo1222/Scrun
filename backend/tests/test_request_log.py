"""요청 처리 시간 로그(계획 0070, app/core/request_log.py)."""
import json
import logging

import pytest
from sqlalchemy import text
from sqlalchemy.exc import ProgrammingError

from app.core import request_log as rl
from app.core.request_log import log as request_log


class _Lines(logging.Handler):
    def __init__(self):
        super().__init__()
        self.lines: list[dict] = []

    def emit(self, record):
        self.lines.append(json.loads(record.getMessage()))


@pytest.fixture
def lines():
    h = _Lines()
    request_log.addHandler(h)
    yield h.lines
    request_log.removeHandler(h)


def test_logs_route_template_status_and_db_queries(db_client, lines):
    r = db_client.get("/v1/agreements")
    assert r.status_code == 200
    (line,) = [x for x in lines if x["path"] == "/v1/agreements"]
    assert line["method"] == "GET" and line["status"] == 200
    assert line["db_n"] >= 1 and line["db_ms"] >= 0 and line["pool_ms"] >= 0
    assert line["ms"] >= line["db_ms"] + line["pool_ms"]
    assert line["rid"] == r.headers["X-Request-Id"]


def test_path_is_template_not_id(db_client, lines):
    r = db_client.get("/v1/runs/01M7PRTB5P0X2VCYW1YPSC6RNY")   # 로그인 없이 → 401이어도 틀로 남김
    assert r.status_code == 401
    assert lines[-1]["path"] == "/v1/runs/{runId}"
    assert "01M7PRTB5P0X2VCYW1YPSC6RNY" not in json.dumps(lines)


def test_unmatched_path_and_no_db(db_client, lines):
    r = db_client.get("/nope")
    assert r.status_code == 404
    assert lines[-1]["path"] == "-" and lines[-1]["status"] == 404 and lines[-1]["db_n"] == 0


def test_probe_header_is_not_logged(db_client, lines):
    r = db_client.get("/v1/agreements", headers={"X-Scrun-Probe": "1"})   # compose 헬스체크
    assert r.status_code == 200
    assert lines == []


def test_failed_query_is_counted(test_db):
    stats = rl._RequestStats()
    token = rl._request_stats.set(stats)
    try:
        with test_db.engine.connect() as conn:
            conn.execute(text("SELECT 1"))
            with pytest.raises(ProgrammingError):
                conn.execute(text("SELECT * FROM no_such_table"))
    finally:
        rl._request_stats.reset(token)
    assert stats.db_n == 2
