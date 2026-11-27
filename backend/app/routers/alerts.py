"""Grafana 알람 → Sentry → 메일(계획 0087 2단계, ADR-0015 "알림 = 이메일"). 새 메일 계정 없이 Sentry가 이미 보내는 메일을 씀.

- Grafana 연락처(deploy/observability/grafana/provisioning/alerting)가 서비스 안쪽 주소 http://api:8000/internal/alerts로 POST
- 밖에선 Caddy가 /internal/*을 막고, 여기서도 비밀(ALERT_WEBHOOK_TOKEN, Authorization: Bearer)이 맞아야 함. 틀리거나 설정이 없으면
  빈 404(있는지도 알리지 않음). 번호표·요청 로그 밖(request_log.is_internal)
- 울리는 알람(status firing)만 보냄, 풀린 알림(resolved)은 로그 한 줄만. 한 번에 최대 20개
- 한계: api가 죽으면 이 주소도 죽어 알람이 못 옴 → 그 경우는 UptimeRobot(/healthz/api)이 메일
"""
import hmac
import logging

from fastapi import APIRouter, Request
from fastapi.responses import Response

from ..core.config import settings
from ..core.error_report import report_alert

log = logging.getLogger("scrun.alerts")
router = APIRouter(include_in_schema=False)
MAX_ALERTS = 20


def _expected_token():
    return settings.alert_webhook_token


@router.post("/internal/alerts")
async def grafana_alerts(request: Request) -> Response:
    token = _expected_token()
    scheme, _, given = request.headers.get("authorization", "").partition(" ")
    if not token or scheme.lower() != "bearer" or not hmac.compare_digest(given.strip(), token):
        return Response(status_code=404)
    try:
        body = await request.json()
    except ValueError:
        return Response(status_code=400)
    alerts = body.get("alerts") if isinstance(body, dict) else None
    if not isinstance(alerts, list):
        return Response(status_code=400)
    for a in alerts[:MAX_ALERTS]:
        if not isinstance(a, dict):
            continue
        labels, notes = a.get("labels"), a.get("annotations")
        labels, notes = (labels if isinstance(labels, dict) else {}), (notes if isinstance(notes, dict) else {})
        name = str(labels.get("alertname", "이름 없는 알람"))[:120]
        summary = str(notes.get("summary", ""))[:500]
        if a.get("status") != "firing":
            log.info("[서버 알람 풀림] %s", name)
            continue
        # WARNING: ERROR로 남기면 로그 연동이 Sentry에 한 번 더 보냄(report_alert가 보냄)
        log.warning("[서버 알람] %s: %s", name, summary)
        if not report_alert(name, summary, str(a.get("startsAt", ""))):
            # Sentry가 꺼져 있으면(SENTRY_DSN 없음) 메일이 안 감 → 서버 로그에라도 오류로(조용히 사라지지 않게)
            log.error("[서버 알람] Sentry가 꺼져 있어 메일을 못 보냄: %s", name)
    return Response(status_code=204)
