"""성장(계획 0048, BL-32·34): 반영 결과·레벨·임팩트 고르기. 반영은 기록이 정해질 때 core/growth.settle이 한 번만 한다."""
from fastapi import APIRouter, Depends, Request
from sqlalchemy.orm import Session

from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..core.growth import POLICY, locked_progress, progress_payload, rewards_for
from ..core.run_access import close_expired, my_run, now_utc
from ..core.security import current_user
from ..db.models import User, UserImpact
from ..db.session import get_db
from ..schemas.progress import ImpactSelect, Progress, Rewards
from .runs import RUN_ERRORS

router = APIRouter(prefix="/v1", tags=["Progress"])
E = ErrorCode
AUTH = (E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)   # 코드 묶음(runs.py와 같게, 문서는 error_responses 한 번에)
LEVELS = "·".join(f"{m / 1000:g}" for m in POLICY.level_m)


@router.get("/runs/{runId}/rewards", summary="이번 운동 반영 결과",
            description="화면 E-11 · BL-32, 계획 0048. 경험치 전후·레벨업·러닝복 단계·임팩트 해금·주간 목표(그 기록이 끝난 주기에서 "
            "그 기록까지 센 횟수). 인증 응답의 rewards와 같은 내용(결과 화면을 다시 열 때). 미인증이면 경험치·횟수 변화 없음. "
            "정해지기 전(달리는 중·인증 대기)이면 RUN_NOT_FINISHED(마감이 지난 인증 대기는 이 요청에서 미인증으로 닫고 답함)",
            response_model=Envelope[Rewards], responses=error_responses(*AUTH, *RUN_ERRORS, E.RUN_NOT_FINISHED))
def rewards(runId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    run = my_run(db, runId, user, lock=True)
    if close_expired(db, now_utc(), run_id=run.id):
        db.commit()
        db.refresh(run)
    out = rewards_for(db, run)
    if out is None:
        raise ApiError(E.RUN_NOT_FINISHED)
    return ok(request, out)


@router.get("/users/me/progress", summary="레벨·인증 마크",
            description=f"화면 H-02, H-03, H-04, B-01 · BL-34, BL-33, BL-35, 계획 0048. 경험치 = 인증 완료 기록의 누적 거리(줄지 않음), "
            f"레벨 임계값 {LEVELS} km(서버 설정 GROWTH_LEVEL_M), 러닝복 단계 = min(레벨, {POLICY.outfit_stages}). 마지막 레벨이면 "
            "nextLevelAtM·nextLevelRemainingM은 null. 임팩트는 목록이 정해지기 전(11/24 이후)이라 빈 목록",
            response_model=Envelope[Progress], responses=error_responses(*AUTH))
def progress(request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    return ok(request, progress_payload(db, user.id))


@router.put("/users/me/impact", summary="임팩트 고르기",
            description="화면 H-03 · BL-33, BL-35. 해금한 것만(아니면 VALIDATION_ERROR). null이면 끄기",
            response_model=Envelope[Progress], responses=error_responses(*AUTH, E.VALIDATION_ERROR))
def select_impact(body: ImpactSelect, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    if body.impact_id is not None and db.get(UserImpact, (user.id, body.impact_id)) is None:
        raise ApiError(E.VALIDATION_ERROR, "해금하지 않은 임팩트입니다.", {"fields": [{"field": "impactId", "reason": "잠김"}]})
    p = locked_progress(db, user.id)
    p.selected_impact_id = body.impact_id
    db.commit()
    return ok(request, progress_payload(db, user.id))
