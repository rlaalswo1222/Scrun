from fastapi import APIRouter, Depends, Response

from ..core.auth import Principal, stub_user
from ..core.envelope import error_responses
from ..core.errors import ErrorCode
from ..schemas.event import EventBatch

router = APIRouter(prefix="/v1", tags=["Event"])
E = ErrorCode


@router.post(
    "/events",
    status_code=204,
    summary="지표 이벤트 올리기",
    description="PRD 2.4 검증 지표(목표 완주율, 재방문율, 인증 성공률, 운동 직후 지루함). 앱이 모아서 한 번에 최대 100개. "
    "백로그에 없음 → 추가 제안. 이벤트 목록은 상준과 확정",
    responses=error_responses(E.VALIDATION_ERROR, E.UNAUTHORIZED, E.TOKEN_EXPIRED),
)
def post_events(body: EventBatch, _: Principal = Depends(stub_user)):
    return Response(status_code=204)
