from typing import Optional

from fastapi import APIRouter, Depends, Query, Request, Response

from .. import stub_data as d
from ..core.auth import Principal, current_user, signup_or_user
from ..core.envelope import Envelope, error_responses, ok, utc_now
from ..core.errors import ApiError, ErrorCode
from ..schemas.home import Home
from ..schemas.user import (
    Calibration,
    CalibrationUpload,
    Me,
    MeUpdate,
    NicknameCheck,
    Settings,
    SettingsUpdate,
    WeeklyGoal,
    WeeklyGoalUpdate,
)

router = APIRouter(prefix="/v1", tags=["User"])
E = ErrorCode
AUTH = error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)


@router.get(
    "/users/nickname/check",
    summary="닉네임 사용 가능 여부",
    description="화면 A-06, H-01 · BL-1. 가입용 토큰으로도 됨. 중복·금칙어·길이.\n\n스텁: `taken` → DUPLICATED, `admin` → NOT_ALLOWED",
    response_model=Envelope[NicknameCheck],
    responses=error_responses(E.VALIDATION_ERROR, E.UNAUTHORIZED),
)
def nickname_check(request: Request, nickname: str = Query(min_length=2, max_length=12), _: Principal = Depends(signup_or_user)):
    reason = {"taken": "DUPLICATED", "admin": "NOT_ALLOWED"}.get(nickname)
    return ok(request, {"nickname": nickname, "available": reason is None, "reason": reason})


@router.get("/users/me", summary="내 정보", description="화면 B-01, H-01 · BL-25",
            response_model=Envelope[Me], responses=AUTH)
def get_me(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.ME)


@router.patch(
    "/users/me",
    summary="내 정보 수정",
    description="화면 H-01 · BL-25. 보낸 칸만 바뀜.\n\n스텁: 닉네임 `taken` → NICKNAME_DUPLICATED",
    response_model=Envelope[Me],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR, E.NICKNAME_DUPLICATED, E.NICKNAME_NOT_ALLOWED)},
)
def patch_me(body: MeUpdate, request: Request, _: Principal = Depends(current_user)):
    if body.nickname == "taken":
        raise ApiError(E.NICKNAME_DUPLICATED)
    return ok(request, {**d.ME, **body.model_dump(by_alias=True, exclude_none=True)})


@router.delete(
    "/users/me",
    status_code=204,
    summary="계정 삭제",
    description="화면 H-10 · BL-39 · 계획 0015. 모든 데이터 삭제 + 카카오 연결 해제, 유예 없이 바로. 카카오가 응답하지 않으면 아무것도 지우지 않고 503",
    responses={**AUTH, **error_responses(E.SERVICE_UNAVAILABLE)},
)
def delete_me(_: Principal = Depends(current_user)):
    return Response(status_code=204)


@router.get("/users/me/settings", summary="설정 보기", description="화면 H-10 · BL-25. 알림은 폰 로컬이라 서버에 없음",
            response_model=Envelope[Settings], responses=AUTH)
def get_settings(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.SETTINGS)


@router.patch("/users/me/settings", summary="설정 바꾸기", description="화면 H-10 · BL-25, BL-31",
              response_model=Envelope[Settings], responses={**AUTH, **error_responses(E.VALIDATION_ERROR)})
def patch_settings(body: SettingsUpdate, request: Request, _: Principal = Depends(current_user)):
    return ok(request, {**d.SETTINGS, **body.model_dump(by_alias=True, exclude_none=True)})


@router.get(
    "/users/me/calibration",
    summary="내 보정값",
    description="화면 A-11, D-07, H-09 · BL-4, BL-10. 없으면 data=null → 맵 입장 전에 보정 화면으로",
    response_model=Envelope[Optional[Calibration]],
    responses=AUTH,
)
def get_calibration(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.CALIBRATION)


@router.put(
    "/users/me/calibration",
    summary="보정값 올리기",
    description="화면 A-15, H-09 · BL-4. 계산은 폰, 서버는 보관만. OCR 값은 받지 않음(독립성 원칙). "
    "보정 단계(6·8·10 km/h vs 걷기 1 + 달리기 3점)는 PRD 검토 대기",
    response_model=Envelope[Calibration],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR)},
)
def put_calibration(body: CalibrationUpload, request: Request, _: Principal = Depends(current_user)):
    return ok(request, {**body.model_dump(by_alias=True, mode="json"), "createdAt": utc_now()})


@router.get(
    "/users/me/weekly-goal",
    summary="주간 목표 진행",
    description="화면 B-01, B-02, B-03, H-07 · BL-17. 주기는 설정일부터 7일, 1회 = 인증 완료 + 5분 이상(서버 집계). 목표를 안 정했으면 targetCount=null",
    response_model=Envelope[WeeklyGoal],
    responses=AUTH,
)
def get_weekly_goal(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.WEEKLY_GOAL)


@router.put(
    "/users/me/weekly-goal",
    summary="주간 목표 설정·변경",
    description="화면 A-16, H-08 · BL-17. 1에서 7회. 처음 설정은 바로, 변경은 현재 주기가 끝난 뒤 적용(nextTargetCount)",
    response_model=Envelope[WeeklyGoal],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR)},
)
def put_weekly_goal(body: WeeklyGoalUpdate, request: Request, _: Principal = Depends(current_user)):
    return ok(request, {**d.WEEKLY_GOAL, "nextTargetCount": body.target_count})


@router.get("/home", summary="홈 한 번에", description="화면 B-01에서 B-04 · BL-19. 각 항목 API도 따로 있음",
            response_model=Envelope[Home], responses=AUTH)
def home(request: Request, _: Principal = Depends(current_user)):
    return ok(request, d.HOME)
