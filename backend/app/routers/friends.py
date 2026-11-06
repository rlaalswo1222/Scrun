from typing import Optional

from fastapi import APIRouter, Depends, Request, Response

from .. import stub_data as d
from ..core.auth import Principal, current_user
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..schemas.common import CursorPage
from ..schemas.course import MapEntry
from ..schemas.friend import FeedItem, Friend, FriendAddRequest, FriendProfile, RunningFriend

router = APIRouter(prefix="/v1", tags=["Friend"])
E = ErrorCode
AUTH = error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)
KNOWN_FRIENDS = {f["userId"] for f in d.FRIENDS}


def _check_friend(user_id: str) -> None:
    if user_id not in KNOWN_FRIENDS:
        raise ApiError(E.USER_NOT_FOUND)


@router.post(
    "/friends",
    status_code=201,
    summary="친구 코드로 추가",
    description="화면 G-05에서 G-08(Figma 확인 필요) · BL-27. 최대 50명."
    "\n\n스텁: 내 코드 `SCR-7K2Q` → SELF_FRIEND_CODE, `SCR-NONE` → FRIEND_CODE_NOT_FOUND, "
    "`SCR-FRND` → ALREADY_FRIEND, `SCR-FULL` → FRIEND_LIMIT_EXCEEDED, 그 밖 → 추가됨",
    response_model=Envelope[Friend],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR, E.SELF_FRIEND_CODE, E.FRIEND_CODE_NOT_FOUND,
                                         E.ALREADY_FRIEND, E.FRIEND_LIMIT_EXCEEDED, E.RATE_LIMITED)},
)
def add_friend(body: FriendAddRequest, request: Request, _: Principal = Depends(current_user)):
    code = {d.ME["friendCode"]: E.SELF_FRIEND_CODE, "SCR-NONE": E.FRIEND_CODE_NOT_FOUND,
            "SCR-FRND": E.ALREADY_FRIEND, "SCR-FULL": E.FRIEND_LIMIT_EXCEEDED}.get(body.friend_code)
    if code:
        raise ApiError(code)
    return ok(request, d.FRIENDS[1])


@router.get("/friends", summary="친구 목록", description="화면 G-04, G-09 · BL-27. 없으면 빈 배열(G-04 빈 상태)",
            response_model=Envelope[list[Friend]], responses=AUTH)
def list_friends(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.FRIENDS)


@router.get("/friends/running", summary="지금 달리는 친구", description="화면 G-01, G-02, B-01 · BL-28, BL-31",
            response_model=Envelope[list[RunningFriend]], responses=AUTH)
def running_friends(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.RUNNING_FRIENDS)


@router.get("/friends/{userId}", summary="친구 프로필",
            description="화면 G-09(Figma 확인 필요) · BL-27. 최근 인증 러닝·인증 마크·레벨",
            response_model=Envelope[FriendProfile], responses={**AUTH, **error_responses(E.USER_NOT_FOUND)})
def friend_profile(userId: str, request: Request, _: Principal = Depends(current_user)):
    _check_friend(userId)
    return ok(request, {**d.FRIEND_PROFILE, **next(f for f in d.FRIENDS if f["userId"] == userId)})


@router.delete("/friends/{userId}", status_code=204, summary="친구 삭제", description="화면 G-10 · BL-27",
               responses={**AUTH, **error_responses(E.USER_NOT_FOUND)})
def delete_friend(userId: str, _: Principal = Depends(current_user)):
    _check_friend(userId)
    return Response(status_code=204)


@router.post(
    "/friends/{userId}/run-together",
    status_code=201,
    summary="같이 달리기",
    description="화면 G-03, D-20 · BL-29. 친구가 있는 맵 인스턴스로 입장, 친구 근처 위치(Q-22). 응답은 맵 입장과 같은 모양."
    "\n\n스텁: 달리지 않는 친구(`usr_f2`) → FRIEND_NOT_RUNNING(앱은 일반 입장으로)",
    response_model=Envelope[MapEntry],
    responses={**AUTH, **error_responses(E.USER_NOT_FOUND, E.FRIEND_NOT_RUNNING, E.COURSE_FULL, E.CALIBRATION_REQUIRED)},
)
def run_together(userId: str, request: Request, _: Principal = Depends(current_user)):
    _check_friend(userId)
    if not next(f for f in d.FRIENDS if f["userId"] == userId)["runningNow"]:
        raise ApiError(E.FRIEND_NOT_RUNNING)
    return ok(request, {**d.MAP_ENTRY, "entryPositionM": 1795.0})


@router.get("/feed", summary="친구 러닝 피드",
            description="화면 G-01 · BL-28. 커서 방식. 미인증 기록도 공유됨(앱이 '미인증' 마크). 친구에게만 공개",
            response_model=Envelope[CursorPage[FeedItem]], responses=AUTH)
def feed(request: Request, cursor: Optional[str] = None, _: Principal = Depends(current_user)):
    return ok(request, {"items": d.FEED, "nextCursor": None})
