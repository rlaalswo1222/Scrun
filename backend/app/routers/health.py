"""운영용 주소: 서비스가 살아 있나(/healthz)·지표(/metrics). 둘 다 공개 API 목록(Swagger) 밖.

/healthz(계획 0087 1단계): 바깥 감시(UptimeRobot)가 Caddy의 /healthz/api·/healthz/realtime·/healthz/ocr로
5분마다 부르고, Caddy가 그 서비스의 이 주소로 넘긴다(deploy/Caddyfile). DB까지 한 번 다녀와야 200.

- 공개 API 목록(Swagger·계약 테스트)엔 안 넣음. 응답 껍데기 없이 {"status": "ok"}
- 번호표(0071)·요청 로그는 다른 요청과 같이 지남: 바쁘면 503이 되는 게 바깥에서 본 실제 상태라서
- DB가 죽으면 500 → Sentry에도 감(계획 0087)

/metrics(계획 0087 2단계, app/core/metrics.py): Alloy가 서비스 안쪽 주소(api:8000 등)로 15초마다 긁음. 밖에선 Caddy가 404.
번호표·요청 로그·지표 밖(request_log.is_internal). 다중 프로세스 파일을 읽는 일이라 스레드에서(def)
"""
from fastapi import APIRouter, Depends
from fastapi.responses import Response
from sqlalchemy import text
from sqlalchemy.orm import Session

from ..core import metrics
from ..core.config import settings
from ..db.session import get_db

router = APIRouter()


@router.get("/healthz", include_in_schema=False)
def healthz(db: Session = Depends(get_db)):
    db.execute(text("SELECT 1"))
    return {"status": "ok"}


@router.get("/metrics", include_in_schema=False)
def prometheus_metrics():
    if not settings.metrics:
        return Response(status_code=404)
    return Response(metrics.render(), media_type=metrics.CONTENT_TYPE)
