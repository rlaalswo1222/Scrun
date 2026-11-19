"""홈 한 번에(B-01에서 B-04 · BL-31, 계획 0057). 각 칸은 따로 있는 API와 같은 계산을 그대로 모은다(같은 식을 두 벌 두지 않음).

- 주간 목표 = GET /users/me/weekly-goal, 레벨 = GET /users/me/progress, 달리는 친구 = GET /friends/running
- 코스 = GET /courses 목록을 지금 달리는 사람 많은 순으로(같으면 짧은 코스부터). 홈 카드는 앞의 몇 개만 쓴다
- 최근 기록 = 끝낸 러닝(PENDING·VERIFIED·UNVERIFIED) 최근 것부터 3개. 앱은 비었으면 "첫 이용"(B-04)
"""
from datetime import datetime

from fastapi import APIRouter, Depends, Request
from sqlalchemy.orm import Session

from ..core import growth
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ErrorCode
from ..core.run_access import close_expired
from ..core.security import current_user
from ..db.models import User
from ..db.session import get_db
from ..schemas.home import Home
from .courses import course_rows
from .friends import running_rows
from .runs import recent_rows
from .users import get_now

router = APIRouter(prefix="/v1", tags=["User"])
E = ErrorCode
AUTH = (E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)   # 코드 묶음, 문서는 error_responses 한 번에
RECENT_RUNS = 3


@router.get("/home", summary="홈 한 번에",
            description="화면 B-01에서 B-04 · BL-31. 주간 목표·레벨·달리는 친구는 각 API와 같은 값. "
            f"courses는 지금 달리는 사람이 많은 순(같으면 짧은 코스부터), recentRuns는 끝낸 러닝 최근 {RECENT_RUNS}개"
            "(비었으면 첫 이용 B-04). 목표가 없으면 weeklyGoal.targetCount가 null(B-02)",
            response_model=Envelope[Home], responses=error_responses(*AUTH))
def home(request: Request, user: User = Depends(current_user), db: Session = Depends(get_db),
         now: datetime = Depends(get_now)):
    if close_expired(db, user_id=user.id):   # 마감 지난 인증 대기를 닫아야 주간 목표·최근 기록이 맞음
        db.commit()
    courses = sorted(course_rows(db, user), key=lambda c: -c["runnersNow"])   # 정렬이 안정적이라 같으면 목록 순서(짧은 코스부터)
    return ok(request, {
        "nickname": user.nickname,
        "weeklyGoal": growth.weekly_payload(db, user, now),
        "progress": growth.progress_payload(db, user.id),
        "runningFriends": running_rows(db, user),
        "recentRuns": recent_rows(db, user, RECENT_RUNS),
        "courses": courses,
    })
