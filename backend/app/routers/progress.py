from fastapi import APIRouter, Depends, Request

from .. import stub_data as d
from ..core.auth import Principal, current_user
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..schemas.progress import ImpactSelect, Progress, Rewards
from .runs import KNOWN, RUN_ERRORS, STUB_NOTE, _check_run

router = APIRouter(prefix="/v1", tags=["Progress"])
E = ErrorCode
AUTH = error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)


@router.get("/runs/{runId}/rewards", summary="이번 운동 반영 결과",
            description="화면 E-11 · BL-21. 경험치 전후·레벨업·러닝복 단계·임팩트 해금·주간 목표. 결과 화면을 다시 열 때(인증 응답에도 같은 내용)" + STUB_NOTE,
            response_model=Envelope[Rewards], responses={**AUTH, **RUN_ERRORS})
def rewards(runId: str, request: Request, _: Principal = Depends(current_user)):
    _check_run(runId, KNOWN)
    return ok(request, d.REWARDS)


@router.get("/users/me/progress", summary="레벨·인증 마크",
            description="화면 H-02, H-03, H-04, B-01 · BL-23, BL-35, BL-37. 레벨 임계값 3·10·25·50·100 km는 서버 설정",
            response_model=Envelope[Progress], responses=AUTH)
def progress(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.PROGRESS)


@router.put("/users/me/impact", summary="임팩트 고르기",
            description="화면 H-03 · BL-35, BL-37. 해금한 것만. null이면 끄기",
            response_model=Envelope[Progress], responses={**AUTH, **error_responses(E.VALIDATION_ERROR)})
def select_impact(body: ImpactSelect, request: Request, _: Principal = Depends(current_user)):
    unlocked = {i["impactId"] for i in d.IMPACTS if i["unlocked"]}
    if body.impact_id is not None and body.impact_id not in unlocked:
        raise ApiError(E.VALIDATION_ERROR, "해금하지 않은 임팩트입니다.", {"fields": [{"field": "impactId", "reason": "잠김"}]})
    return ok(request, {**d.PROGRESS, "selectedImpactId": body.impact_id})
