"""러닝 기록을 여러 라우터(runs·verification)가 같이 쓰는 것: 상태 이름, 내 기록 찾기, 인증 마감.

상태: ACTIVE(달리는 중) → PENDING(끝, 인증 대기) → VERIFIED·UNVERIFIED(인증, 계획 0038). 복구 안 하고 버리면 DISCARDED.
"""
from datetime import datetime, timedelta, timezone
from typing import Optional

from sqlalchemy import select
from sqlalchemy.orm import Session

from ..db.models import Run, User
from .config import settings
from .errors import ApiError, ErrorCode

ACTIVE, PENDING, DISCARDED = "ACTIVE", "PENDING", "DISCARDED"
VERIFIED, UNVERIFIED = "VERIFIED", "UNVERIFIED"


def invalid(field: str, reason: str) -> ApiError:
    return ApiError(ErrorCode.VALIDATION_ERROR, details={"fields": [{"field": field, "reason": reason}]})


def my_run(db: Session, run_id: str, user: User, lock: bool = False) -> Run:
    """내 기록. 없으면 RUN_NOT_FOUND, 남의 것이면 FORBIDDEN. lock이면 이 요청이 끝날 때까지 줄을 잠그고
    DB 값을 새로 읽는다(같은 요청에서 먼저 잠금 없이 읽은 옛 값이 세션에 남아 있어도 덮어씀)."""
    q = select(Run).where(Run.id == run_id)
    if lock:
        q = q.with_for_update().execution_options(populate_existing=True)
    run = db.scalar(q)
    if run is None:
        raise ApiError(ErrorCode.RUN_NOT_FOUND)
    if run.user_id != user.id:
        raise ApiError(ErrorCode.FORBIDDEN)
    return run


def verification_deadline(run: Run) -> Optional[datetime]:
    """인증 마감. 서버가 종료를 받은 때 + 설정 시간(finish가 넣음). 칸이 생기기 전(11/16 이전 배포)에 끝난 기록은
    마지막 갱신(= 종료)부터. 끝나지 않았으면 None."""
    if run.status == ACTIVE or run.status == DISCARDED:
        return None
    return run.verification_deadline or run.updated_at + timedelta(seconds=settings.verification.deadline_s)


def verification_of(run: Run, now: Optional[datetime] = None) -> str:
    """화면에 보일 인증 상태. 마감이 지난 PENDING은 앱이 아무것도 안 보냈어도 UNVERIFIED(BL-16 ⑤)
    — 기록 조회(BL-19·20)·인증 API가 같이 쓴다. DB에는 다음 인증 요청 때 TIMEOUT으로 닫힘."""
    if run.status == PENDING and (now or datetime.now(timezone.utc)) > verification_deadline(run):
        return UNVERIFIED
    return run.status if run.status in (VERIFIED, UNVERIFIED) else PENDING
