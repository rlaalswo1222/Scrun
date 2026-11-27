"""서비스가 살아 있나(계획 0087 1단계). 바깥 감시(UptimeRobot)가 Caddy의 /healthz/api·/healthz/realtime·/healthz/ocr로
5분마다 부르고, Caddy가 그 서비스의 이 주소로 넘긴다(deploy/Caddyfile). DB까지 한 번 다녀와야 200.

- 공개 API 목록(Swagger·계약 테스트)엔 안 넣음. 응답 껍데기 없이 {"status": "ok"}
- 번호표(0071)·요청 로그는 다른 요청과 같이 지남: 바쁘면 503이 되는 게 바깥에서 본 실제 상태라서
- DB가 죽으면 500 → Sentry에도 감(계획 0087)
"""
from fastapi import APIRouter, Depends
from sqlalchemy import text
from sqlalchemy.orm import Session

from ..db.session import get_db

router = APIRouter()


@router.get("/healthz", include_in_schema=False)
def healthz(db: Session = Depends(get_db)):
    db.execute(text("SELECT 1"))
    return {"status": "ok"}
