"""서버 지표(계획 0087 2단계, app/core/metrics.py)·Grafana 알람 받기(app/routers/alerts.py)·관측 배포 설정.

로컬·테스트는 PROMETHEUS_MULTIPROC_DIR이 없어 한 프로세스 기본 레지스트리 → 값은 전역이라 전후 차이로 본다.
"""
import json
import re
from pathlib import Path

import pytest
import sentry_sdk
import yaml
from fastapi import FastAPI
from fastapi.testclient import TestClient
from prometheus_client import REGISTRY

from app.core import metrics
from app.core.config import ConfigError, ErrorReportSettings, load_settings
from app.core.error_report import init_error_report
from app.core.ocr import UNREADABLE, ReadStatus
from app.core.request_log import RequestContextMiddleware, is_internal
from app.main import app
from app.routers import alerts
from app.routers.verification import OcrUnavailable, _OcrRunner
from test_error_report import DSN, CaptureTransport

BACKEND = Path(__file__).resolve().parents[1]
OBS = BACKEND / "deploy" / "observability"
TOKEN = "t" * 40


def count(route, status, method="GET"):
    return REGISTRY.get_sample_value("scrun_http_requests_total", {"method": method, "route": route, "status": str(status)}) or 0.0


def test_requests_are_counted_by_route_template(db_client):
    before, unknown = count("/v1/agreements", 200), count("-", 404)
    db_client.get("/v1/agreements")
    db_client.get("/v1/does-not-exist")
    assert count("/v1/agreements", 200) == before + 1
    assert count("-", 404) == unknown + 1   # 라우트에 안 맞은 주소는 "-" 하나로(주소마다 줄이 늘지 않게)
    seconds = REGISTRY.get_sample_value("scrun_http_request_duration_seconds_count", {"method": "GET", "route": "/v1/agreements"})
    assert seconds and seconds >= 1


def test_metrics_endpoint_is_internal_and_not_counted(db_client):
    before = count("/metrics", 200)
    r = db_client.get("/metrics")
    assert r.status_code == 200 and r.headers["content-type"].startswith("text/plain")
    assert "scrun_http_requests_total" in r.text and "scrun_realtime_connections" in r.text
    assert count("/metrics", 200) == before   # 긁기 자체는 안 셈
    assert is_internal({"path": "/metrics"})  # 번호표·요청 로그 밖
    assert "/metrics" not in app.openapi()["paths"] and "/internal/alerts" not in app.openapi()["paths"]


def _small_app(log_requests: bool, count_metrics: bool) -> FastAPI:
    small = FastAPI()

    @small.get("/ping-metrics")
    def ping():
        return {"ok": True}

    small.add_middleware(RequestContextMiddleware, log_requests=log_requests, metrics=count_metrics)
    return small


def test_metrics_switch_is_independent_of_request_log():
    before = count("/ping-metrics", 200)
    TestClient(_small_app(log_requests=False, count_metrics=False)).get("/ping-metrics")
    assert count("/ping-metrics", 200) == before
    TestClient(_small_app(log_requests=False, count_metrics=True)).get("/ping-metrics")   # 요청 로그를 꺼도 지표는 셈
    assert count("/ping-metrics", 200) == before + 1


class _Engine:
    def read(self, photos):
        return UNREADABLE


def ocr(result):
    return REGISTRY.get_sample_value("scrun_ocr_batches_total", {"result": result}) or 0.0


def test_ocr_batches_are_counted_by_result():
    runner = _OcrRunner()
    before, seconds = ocr("unreadable"), REGISTRY.get_sample_value("scrun_ocr_batch_seconds_count") or 0.0
    assert runner.read(_Engine(), [], timeout_s=5) is UNREADABLE
    assert ocr("unreadable") == before + 1   # 라벨은 소문자로 맞춤(ReadStatus.UNREADABLE)
    assert ocr(ReadStatus.UNREADABLE.value) == 0.0
    assert REGISTRY.get_sample_value("scrun_ocr_batch_seconds_count") == seconds + 1
    busy = ocr("busy")
    runner.running = runner.WORKERS   # 읽기 자리가 다 참 → 503
    with pytest.raises(OcrUnavailable) as e:
        runner.read(_Engine(), [], timeout_s=5)
    assert e.value.status == 503 and e.value.outcome == "busy"
    assert ocr("busy") == busy + 1


def test_metrics_switch_off_stops_ocr_and_realtime_too():
    before, gauge = ocr("unreadable"), REGISTRY.get_sample_value("scrun_realtime_connections") or 0.0
    metrics.set_enabled(False)
    try:
        _OcrRunner().read(_Engine(), [], timeout_s=5)
        metrics.realtime_joined()
    finally:
        metrics.set_enabled(True)
    assert ocr("unreadable") == before and (REGISTRY.get_sample_value("scrun_realtime_connections") or 0.0) == gauge
    metrics.realtime_joined()
    assert REGISTRY.get_sample_value("scrun_realtime_connections") == gauge + 1
    metrics.realtime_left()
    assert REGISTRY.get_sample_value("scrun_realtime_connections") == gauge


def test_odd_methods_share_one_label():
    before = count("/x", 200, method="OTHER")
    metrics.observe_request("BREW", "/x", 200, 0.01, 0, 0, 0)
    assert count("/x", 200, method="OTHER") == before + 1 and count("/x", 200, method="BREW") == 0.0


@pytest.fixture
def sent(monkeypatch):
    transport = CaptureTransport()
    init_error_report(ErrorReportSettings(dsn=DSN, environment="test", service="api"), transport=transport)
    monkeypatch.setattr(alerts, "_expected_token", lambda: TOKEN)
    yield transport
    sentry_sdk.get_global_scope().set_client(None)
    sentry_sdk.get_global_scope().remove_tag("service")


def _alert(status="firing", name="서버 CPU 85% 넘음", starts="2026-11-28T01:00:00Z"):
    return {"status": status, "alerts": [{"status": status, "labels": {"alertname": name},
                                          "annotations": {"summary": "CPU가 높음"}, "startsAt": starts}]}


def test_grafana_alert_becomes_one_sentry_error(sent):
    r = TestClient(app).post("/internal/alerts", json=_alert(), headers={"Authorization": f"Bearer {TOKEN}"})
    assert r.status_code == 204
    [event] = sent.events()   # WARNING 로그는 Sentry로 또 안 감
    assert event["level"] == "error" and "서버 CPU 85% 넘음" in json.dumps(event, ensure_ascii=False)
    assert event["fingerprint"] == ["grafana-alert", "서버 CPU 85% 넘음", "2026-11-28T01:00:00Z"]
    assert event["tags"]["source"] == "grafana"


def test_resolved_alert_sends_nothing(sent):
    r = TestClient(app).post("/internal/alerts", json=_alert(status="resolved"), headers={"Authorization": f"Bearer {TOKEN}"})
    assert r.status_code == 204 and sent.events() == []


@pytest.mark.parametrize("headers", [{}, {"Authorization": "Bearer wrong-token-wrong-token-wrong-token-1234"},
                                     {"Authorization": f"Basic {TOKEN}"}])
def test_alert_endpoint_hides_without_right_token(sent, headers):
    r = TestClient(app).post("/internal/alerts", json=_alert(), headers=headers)
    assert r.status_code == 404 and sent.events() == []


@pytest.mark.parametrize("body", [{"alerts": "x"}, ["x"], {"alerts": [{"status": "firing", "labels": "x"}]}])
def test_alert_endpoint_survives_odd_bodies(sent, body):
    r = TestClient(app).post("/internal/alerts", json=body, headers={"Authorization": f"Bearer {TOKEN}"})
    assert r.status_code in (204, 400)   # 500이 아님


def test_alert_is_logged_as_error_when_sentry_is_off(monkeypatch, caplog):
    monkeypatch.setattr(alerts, "_expected_token", lambda: TOKEN)
    r = TestClient(app).post("/internal/alerts", json=_alert(), headers={"Authorization": f"Bearer {TOKEN}"})
    assert r.status_code == 204
    assert any(rec.levelname == "ERROR" and "Sentry가 꺼져" in rec.getMessage() for rec in caplog.records)


def test_alert_endpoint_off_without_token(monkeypatch):
    monkeypatch.setattr(alerts, "_expected_token", lambda: None)
    assert TestClient(app).post("/internal/alerts", json=_alert(), headers={"Authorization": f"Bearer {TOKEN}"}).status_code == 404


BASE_ENV = {"JWT_SECRET": "x" * 48, "KAKAO_MODE": "fake"}


def test_settings_metrics_switch_and_alert_token():
    assert load_settings(BASE_ENV).metrics is True and load_settings({**BASE_ENV, "METRICS": "0"}).metrics is False
    assert load_settings({**BASE_ENV, "ALERT_WEBHOOK_TOKEN": TOKEN}).alert_webhook_token == TOKEN
    with pytest.raises(ConfigError):
        load_settings({**BASE_ENV, "ALERT_WEBHOOK_TOKEN": "short"})


def _used_metric_names() -> set:
    text = (OBS / "grafana/dashboards/scrun-server.json").read_text(encoding="utf-8")
    text += (OBS / "grafana/provisioning/alerting/scrun.yaml").read_text(encoding="utf-8")
    return set(re.findall(r"\bscrun_[a-z_]+", text))


def test_dashboard_and_alerts_use_metrics_that_exist():
    """대시보드·알람이 쓰는 scrun_ 이름이 app/core/metrics.py(또는 backup_metric.sh)에 있음 → 이름을 바꾸면 여기서 걸림"""
    defined = {m._name for m in vars(metrics).values() if hasattr(m, "_name") and hasattr(m, "_type")}
    full = set()
    for name in defined:
        full |= {name, f"{name}_total", f"{name}_bucket", f"{name}_count", f"{name}_sum"}
    backup = "scrun_backup_last_success_timestamp_seconds"
    assert backup in (BACKEND / "deploy/backup_metric.sh").read_text(encoding="utf-8")
    assert _used_metric_names() - full - {backup} == set()


def test_provisioning_files_parse_and_point_at_alert_route():
    json.loads((OBS / "grafana/dashboards/scrun-server.json").read_text(encoding="utf-8"))
    alerting = yaml.safe_load((OBS / "grafana/provisioning/alerting/scrun.yaml").read_text(encoding="utf-8"))
    [contact] = alerting["contactPoints"][0]["receivers"]
    assert contact["settings"]["url"] == "http://api:8000/internal/alerts"
    assert contact["settings"]["authorization_credentials"] == "$ALERT_WEBHOOK_TOKEN"
    rules = alerting["groups"][0]["rules"]
    assert len({r["uid"] for r in rules}) == len(rules) >= 8
    assert "scrun-scrape-stopped" in {r["uid"] for r in rules}   # 수집이 멈춘 걸 따로 알림
    for name in ("datasources/prometheus.yaml", "dashboards/scrun.yaml"):
        yaml.safe_load((OBS / "grafana/provisioning" / name).read_text(encoding="utf-8"))
    sources = yaml.safe_load((OBS / "grafana/provisioning/datasources/prometheus.yaml").read_text(encoding="utf-8"))["datasources"]
    assert {(s["uid"], s["url"]) for s in sources} == {("prometheus", "http://prometheus:9090"), ("loki", "http://loki:3100")}


def test_server_compose_observability_is_private_and_pinned():
    services = yaml.safe_load((BACKEND / "deploy/docker-compose.server.yml").read_text(encoding="utf-8"))["services"]
    for name in ("alloy", "prometheus", "grafana", "loki"):
        image = services[name]["image"]
        assert re.search(r":v?\d+\.\d+\.\d+$", image), image   # 판 고정(latest 아님)
        assert "mem_limit" in services[name]
    published = {n: s.get("ports", []) for n, s in services.items() if s.get("ports")}
    assert set(published) == {"caddy", "grafana"}
    assert all(p.startswith("127.0.0.1:") for p in published["grafana"])   # Grafana는 서버 안에서만(SSH 터널)
    for name in ("api", "realtime", "ocr"):
        assert services[name]["environment"]["PROMETHEUS_MULTIPROC_DIR"] == "/run/scrun-metrics"
        assert "/run/scrun-metrics" in services[name]["tmpfs"]
    assert services["grafana"]["environment"]["ALERT_WEBHOOK_TOKEN"].startswith("${ALERT_WEBHOOK_TOKEN:?")
    caddy = (BACKEND / "deploy/Caddyfile").read_text(encoding="utf-8")
    assert re.search(r"handle /metrics \{\s*respond 404\s*\}", caddy)


def test_logs_pipeline_reads_only_scrun_containers_and_keeps_30_days():
    """3단계: Alloy가 journald의 scrun- 컨테이너 줄만 Loki로(헬스체크 줄은 버림), Loki는 30일 보관, 로그 화면 쿼리는 service 라벨로"""
    alloy = (OBS / "config.alloy").read_text(encoding="utf-8")
    assert 'loki.source.journal "host"' in alloy and 'url = "http://loki:3100/loki/api/v1/push"' in alloy
    assert 'regex         = "scrun-(.+)-[0-9]+"' in alloy
    # scrun- 아닌 줄은 relabel keep으로 버림. stage.drop { source = "service" value = "" }는 Alloy에서 거꾸로 동작해(11/28 리뷰) 쓰지 않음
    assert re.search(r'regex\s*=\s*"scrun-\.\+"\s*action\s*=\s*"keep"', alloy)
    assert 'value  = ""' not in alloy
    assert "GET /v1/agreements HTTP/1[.]1" in alloy   # 컨테이너 헬스체크 접속 줄은 버림
    loki = yaml.safe_load((OBS / "loki.yaml").read_text(encoding="utf-8"))
    assert loki["limits_config"]["retention_period"] == "720h" and loki["compactor"]["retention_enabled"] is True
    assert loki["analytics"]["reporting_enabled"] is False
    logs = json.loads((OBS / "grafana/dashboards/scrun-logs.json").read_text(encoding="utf-8"))
    assert {v["name"] for v in logs["templating"]["list"]} == {"service", "search"}
    for panel in logs["panels"]:
        for target in panel["targets"]:
            assert target["expr"].startswith('sum by (service)') or target["expr"].startswith('{service=~"$service"}')
    services = yaml.safe_load((BACKEND / "deploy/docker-compose.server.yml").read_text(encoding="utf-8"))["services"]
    assert "/var/log/journal:/var/log/journal:ro" in services["alloy"]["volumes"]
    assert "ports" not in services["loki"]   # 밖에 안 엶
    for name in ("db", "caddy", "api", "realtime", "ocr", "alloy", "prometheus", "grafana", "loki"):
        assert services[name]["logging"]["driver"] == "journald", name   # 모든 컨테이너 로그가 journald → Loki
