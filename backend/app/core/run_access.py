"""러닝 기록을 여러 라우터(runs·verification)가 같이 쓰는 것: 상태 이름, 내 기록 찾기, 인증 마감.

상태: ACTIVE(달리는 중) → PENDING(끝, 인증 대기) → VERIFIED·UNVERIFIED(인증, 계획 0038). 복구 안 하고 버리면 DISCARDED.
인증 마감이 지난 PENDING은 DB에서 UNVERIFIED(TIMEOUT)로 닫는다(close_expired). 기록 목록·상세·통계(BL-19·20)는 읽기 전에
close_expired를 불러 DB 상태만 보면 된다(같은 계산을 곳곳에 넣지 않게).
"""
from datetime import datetime, timedelta, timezone
from typing import Optional

from sqlalchemy import and_, func, select, update
from sqlalchemy.orm import Session

from ..db.models import Run, User
from .config import settings
from .errors import ApiError, ErrorCode

ACTIVE, PENDING, DISCARDED = "ACTIVE", "PENDING", "DISCARDED"
VERIFIED, UNVERIFIED = "VERIFIED", "UNVERIFIED"
TIMEOUT = "TIMEOUT"


def now_utc() -> datetime:
    return datetime.now(timezone.utc)


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


def _grace() -> timedelta:
    return timedelta(seconds=settings.verification.deadline_s)


def verification_deadline(run: Run) -> Optional[datetime]:
    """인증 마감. 서버가 종료를 받은 때 + 설정 시간(finish가 넣음). 칸이 생기기 전(11/16 이전 배포)에 끝난 기록은
    마지막 갱신(= 종료)부터. 끝나지 않았으면 None."""
    if run.status in (ACTIVE, DISCARDED):
        return None
    return run.verification_deadline or run.updated_at + _grace()


def is_expired(run: Run, at: datetime) -> bool:
    """at(요청이 도착한 때)에 마감이 지난 인증 대기 기록인지. 마감 판단은 여기 한 곳."""
    return run.status == PENDING and at > verification_deadline(run)


def close_expired(db: Session, at: Optional[datetime] = None, user_id: Optional[str] = None,
                  run_id: Optional[str] = None) -> int:
    """마감이 지난 PENDING을 UNVERIFIED(TIMEOUT)로 닫는다(커밋은 부른 쪽). 닫은 개수.
    is_expired와 같은 식을 SQL로: 마감 칸이 없으면 마지막 갱신 + 설정 시간."""
    at = at or now_utc()
    deadline = func.coalesce(Run.verification_deadline, Run.updated_at + _grace())
    cond = [Run.status == PENDING, deadline < at]
    if user_id is not None:
        cond.append(Run.user_id == user_id)
    if run_id is not None:
        cond.append(Run.id == run_id)
    res = db.execute(update(Run).where(and_(*cond)).values(status=UNVERIFIED, unverified_reason=TIMEOUT)
                     .execution_options(synchronize_session="fetch"))
    return res.rowcount or 0
