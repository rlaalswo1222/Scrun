"""서버 오류를 Sentry로(계획 0087 1단계, app/core/error_report.py)·헬스체크(app/routers/health.py)·배포 설정.

진짜 Sentry로는 보내지 않는다: 보낼 내용을 받아 두는 가짜 transport를 끼워 무엇이 나가는지 본다.
"""
import json
import logging
import re
from pathlib import Path

import pytest
import sentry_sdk
import yaml
from fastapi import FastAPI, Request
from fastapi.testclient import TestClient
from sentry_sdk.transport import Transport

from app.core.config import ConfigError, ErrorReportSettings, load_error_report
from app.core.envelope import unhandled_error_handler
from app.core.error_report import init_error_report
from app.core.request_log import RequestContextMiddleware
from app.main import app

DSN = "https://0123456789abcdef0123456789abcdef@o1.ingest.sentry.io/1"
BACKEND = Path(__file__).resolve().parents[1]


class CaptureTransport(Transport):
    def __init__(self, options=None):
        super().__init__(options)
        self.envelopes = []

    def capture_envelope(self, envelope):
        self.envelopes.append(envelope)

    def events(self):
        return [e.get_event() for e in self.envelopes if e.get_event() is not None]

    def raw(self) -> str:
        return "".join(e.serialize().decode("utf-8", "replace") for e in self.envelopes)


@pytest.fixture
def sent():
    transport = CaptureTransport()
    assert init_error_report(ErrorReportSettings(dsn=DSN, environment="test", release="abc1234", service="ocr"),
                             transport=transport)
    yield transport
    sentry_sdk.get_global_scope().set_client(None)   # 다른 테스트에 Sentry가 켜진 채 남지 않게
    sentry_sdk.get_global_scope().remove_tag("service")


def _boom_app(store: dict) -> FastAPI:
    """요청 ID 미들웨어 + 우리 500 처리기만 붙인 작은 앱(DB 없이)."""
    small = FastAPI()
    small.add_middleware(RequestContextMiddleware, log_requests=False)
    small.add_exception_handler(Exception, unhandled_error_handler)

    @small.get("/v1/runs/{runId}/boom")
    def boom(runId: str, request: Request):
        auth = request.headers.get("authorization")   # 지역 변수(토큰)는 보내면 안 됨. 소스 줄은 공개 코드라 보내도 됨
        try:
            raise RuntimeError("처리 못 한 예외")
        except RuntimeError as exc:
            store["exc"] = exc
            raise

    return small


def test_unhandled_500_goes_to_sentry_with_request_id_and_nothing_private(sent):
    store = {}
    r = TestClient(_boom_app(store), raise_server_exceptions=False).get(
        "/v1/runs/01ABC/boom", headers={"Authorization": "Bearer secret-token-in-header", "Cookie": "session=secret-cookie"})
    assert r.status_code == 500 and r.json()["error"]["code"] == "INTERNAL_ERROR"
    [event] = sent.events()
    assert event["exception"]["values"][-1]["type"] == "RuntimeError"
    # 서버 로그 줄과 이어 봄. 500 응답엔 X-Request-Id 머리가 없어(request_log.py) 껍데기 meta로
    assert event["tags"]["request_id"] == r.json()["meta"]["requestId"] and event["tags"]["request_id"].startswith("req_")
    assert event["tags"]["route"] == "/v1/runs/{runId}/boom"           # 기록 ID가 아니라 라우트 틀
    assert event["tags"]["method"] == "GET" and event["tags"]["service"] == "ocr"
    assert event["environment"] == "test" and event["release"] == "abc1234" and event["server_name"] == "ocr"
    assert "request" not in event and "user" not in event
    frames = event["exception"]["values"][-1]["stacktrace"]["frames"]
    assert all("vars" not in f for f in frames)
    assert "secret-token" not in sent.raw() and "secret-cookie" not in sent.raw() and "01ABC" not in sent.raw()


def test_same_exception_logged_by_uvicorn_is_not_sent_twice(sent):
    """uvicorn은 처리기가 500을 보낸 뒤 같은 예외를 "Exception in ASGI application"으로 ERROR 로그에 남긴다."""
    store = {}
    TestClient(_boom_app(store), raise_server_exceptions=False).get("/v1/runs/x/boom")
    logging.getLogger("uvicorn.error").error("Exception in ASGI application", exc_info=store["exc"])
    assert len(sent.events()) == 1


def test_error_logs_are_sent_info_logs_are_not(sent):
    log = logging.getLogger("app.console_ocr.pool")
    log.info("요청 한 줄")       # INFO는 오류도 빵부스러기도 아님
    log.warning("경고")
    log.error("계기판 OCR 프로세스가 죽음 — 새로 띄움")
    [event] = sent.events()
    assert event["level"] == "error" and "프로세스가 죽음" in json.dumps(event, ensure_ascii=False)
    assert not event.get("breadcrumbs", {}).get("values")


def test_off_without_dsn_sends_nothing():
    assert init_error_report(ErrorReportSettings()) is False
    assert not sentry_sdk.get_client().is_active()
    r = TestClient(_boom_app({}), raise_server_exceptions=False).get("/v1/runs/x/boom")
    assert r.status_code == 500   # 꺼져 있어도 500 응답은 그대로


@pytest.mark.parametrize("env,error", [
    ({"SENTRY_DSN": "http://abc@o1.ingest.sentry.io/1"}, True),          # https만
    ({"SENTRY_DSN": "https://key:secret@o1.ingest.sentry.io/1"}, True),  # 비밀 키 붙은 옛 모양
    ({"SENTRY_DSN": DSN, "SENTRY_ENVIRONMENT": "Prod uction"}, True),
    ({"SCRUN_SERVICE": "api;rm"}, True),
    ({}, False),
    ({"SENTRY_DSN": DSN, "SENTRY_ENVIRONMENT": "production", "SENTRY_RELEASE": "a1b2c3d", "SCRUN_SERVICE": "realtime"}, False),
])
def test_load_error_report(env, error):
    if error:
        with pytest.raises(ConfigError):
            load_error_report(env)
        return
    cfg = load_error_report(env)
    assert cfg.dsn == env.get("SENTRY_DSN")
    assert cfg.environment == env.get("SENTRY_ENVIRONMENT", "local") and cfg.service == env.get("SCRUN_SERVICE", "api")


def test_db_errors_do_not_carry_bound_values():
    """DB 오류 예외 메시지에 바인드 값(카카오 회원번호·닉네임·토큰 해시)이 안 들어감 → 500이 Sentry·로그로 가도 값은 안 남음"""
    from app.db.session import engine
    assert engine.hide_parameters is True


def test_healthz_checks_db_and_is_not_public_api(db_client):
    r = db_client.get("/healthz")
    assert r.status_code == 200 and r.json() == {"status": "ok"}
    assert "/healthz" not in app.openapi()["paths"]


def test_server_compose_names_each_service_and_passes_sentry():
    services = yaml.safe_load((BACKEND / "deploy/docker-compose.server.yml").read_text(encoding="utf-8"))["services"]
    for name in ("api", "realtime", "ocr"):
        env = services[name]["environment"]
        assert env["SCRUN_SERVICE"] == name
        assert env["SENTRY_DSN"] == "${SENTRY_DSN:-}" and env["SENTRY_ENVIRONMENT"] == "production"
        assert env["SENTRY_RELEASE"] == "${SCRUN_RELEASE:-}"


def test_caddy_sends_each_healthz_to_its_service():
    caddy = (BACKEND / "deploy/Caddyfile").read_text(encoding="utf-8")
    for name in ("api", "realtime", "ocr"):
        block = re.search(r"handle /healthz/" + name + r" \{(.*?)\n\t\}", caddy, re.S)
        assert block, name
        assert "rewrite * /healthz" in block.group(1) and f"reverse_proxy {name}:8000" in block.group(1)
