from datetime import timezone
from typing import Optional

from fastapi import APIRouter, Depends, Query, Request, Response
from sqlalchemy import select
from sqlalchemy.orm import Session

from .. import stub_data as d
from ..core import nickname as nick
from ..core.auth import Principal, stub_user
from ..core.security import current_user, signup_or_user_id
from ..db.ids import new_id
from ..db.models import Calibration as CalibrationRow
from ..db.models import User
from ..db.session import get_db
from ..core.envelope import Envelope, error_responses, ok
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
    description="화면 A-06, H-01 · BL-1. 가입용 토큰으로도 됨. 가입·내 정보 수정과 같은 판정: "
    f"정규화(NFKC·앞뒤 공백 제거) → 형식({nick.FORMAT_RULE}, 아니면 INVALID) → 금칙어(NOT_ALLOWED) → 중복(DUPLICATED). "
    "로그인한 사람의 지금 닉네임은 사용 가능. 응답의 nickname은 정규화된 값",
    response_model=Envelope[NicknameCheck],
    responses=error_responses(E.VALIDATION_ERROR, E.UNAUTHORIZED, E.TOKEN_EXPIRED),
)
def nickname_check(request: Request, nickname: str = Query(min_length=1, max_length=nick.RAW_MAX_LEN),
                   user_id: Optional[str] = Depends(signup_or_user_id), db: Session = Depends(get_db)):
    name, problem = nick.check(db, nickname, exclude_user_id=user_id)
    return ok(request, {"nickname": name, "available": problem is None, "reason": problem.value if problem else None})


@router.get("/users/me", summary="내 정보", description="화면 B-01, H-01 · BL-36",
            response_model=Envelope[Me], responses=AUTH)
def get_me(request: Request, _: Principal = Depends(stub_user)):
    return ok(request, d.ME)


@router.patch(
    "/users/me",
    summary="내 정보 수정",
    description="화면 H-01 · BL-36. 보낸 칸만 바뀜.\n\n스텁: 닉네임 `taken` → NICKNAME_DUPLICATED",
    response_model=Envelope[Me],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR, E.NICKNAME_DUPLICATED, E.NICKNAME_NOT_ALLOWED)},
)
def patch_me(body: MeUpdate, request: Request, _: Principal = Depends(stub_user)):
    if body.nickname == "taken":
        raise ApiError(E.NICKNAME_DUPLICATED)
    return ok(request, {**d.ME, **body.model_dump(by_alias=True, exclude_none=True)})


@router.delete(
    "/users/me",
    status_code=204,
    summary="계정 삭제",
    description="화면 H-10 · BL-48 · 계획 0015. 모든 데이터 삭제 + 카카오 연결 해제, 유예 없이 바로. 카카오가 응답하지 않으면 아무것도 지우지 않고 503",
    responses={**AUTH, **error_responses(E.SERVICE_UNAVAILABLE)},
)
def delete_me(_: Principal = Depends(stub_user)):
    return Response(status_code=204)


@router.get("/users/me/settings", summary="설정 보기", description="화면 H-10 · BL-36. 알림은 폰 로컬이라 서버에 없음",
            response_model=Envelope[Settings], responses=AUTH)
def get_settings(request: Request, _: Principal = Depends(stub_user)):
    return ok(request, d.SETTINGS)


@router.patch("/users/me/settings", summary="설정 바꾸기", description="화면 H-10 · BL-36, BL-40",
              response_model=Envelope[Settings], responses={**AUTH, **error_responses(E.VALIDATION_ERROR)})
def patch_settings(body: SettingsUpdate, request: Request, _: Principal = Depends(stub_user)):
    return ok(request, {**d.SETTINGS, **body.model_dump(by_alias=True, exclude_none=True)})


@router.get(
    "/users/me/calibration",
    summary="내 보정값",
    description="화면 A-11, D-07, H-09 · BL-4, BL-10. 가장 최근에 올린 보정. 없으면 data=null → 맵 입장 전에 보정 화면으로",
    response_model=Envelope[Optional[Calibration]],
    responses=AUTH,
)
def get_calibration(request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    row = _latest_calibration(db, user.id)
    return ok(request, _calibration_out(row) if row else None)


@router.put(
    "/users/me/calibration",
    summary="보정값 올리기",
    description="화면 A-15, H-09 · BL-4. 계산은 폰, 서버는 보관만(model은 해석하지 않음). 이력으로 쌓이고 가장 최근 것이 현재 보정. "
    "올린 뒤부터 맵 입장(CALIBRATION_REQUIRED 해제). OCR 값은 받지 않음(독립성 원칙). "
    "보정 단계(6·8·10 km/h vs 걷기 1 + 달리기 3점)는 PRD 검토 대기",
    response_model=Envelope[Calibration],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR)},
)
def put_calibration(body: CalibrationUpload, request: Request, user: User = Depends(current_user),
                    db: Session = Depends(get_db)):
    # 이력으로 쌓는다(가장 최근 것이 현재 보정, docs/api/data_model.md). 덮어쓰지 않음
    up = body.model_dump(by_alias=True, mode="json")
    row = CalibrationRow(id=new_id(), user_id=user.id, points=up["points"], model=up["model"], device=up["device"])
    db.add(row)
    db.commit()
    db.refresh(row)
    return ok(request, _calibration_out(row))


def _latest_calibration(db: Session, user_id: str) -> Optional[CalibrationRow]:
    # ID가 시간순(ULID)이라 같은 시각에 두 번 올려도 나중 것이 위
    return db.scalar(select(CalibrationRow).where(CalibrationRow.user_id == user_id)
                     .order_by(CalibrationRow.created_at.desc(), CalibrationRow.id.desc()).limit(1))


def _calibration_out(row: CalibrationRow) -> dict:
    return {"points": row.points, "model": row.model, "device": row.device,
            "createdAt": row.created_at.astimezone(timezone.utc)}  # API 시각은 UTC(Z), DB 연결 시간대와 상관없이


@router.get(
    "/users/me/weekly-goal",
    summary="주간 목표 진행",
    description="화면 B-01, B-02, B-03, H-07 · BL-30. 주기는 설정일부터 7일, 1회 = 인증 완료 + 5분 이상(서버 집계). 목표를 안 정했으면 targetCount=null",
    response_model=Envelope[WeeklyGoal],
    responses=AUTH,
)
def get_weekly_goal(request: Request, _: Principal = Depends(stub_user)):
    return ok(request, d.WEEKLY_GOAL)


@router.put(
    "/users/me/weekly-goal",
    summary="주간 목표 설정·변경",
    description="화면 A-16, H-08 · BL-30. 1에서 7회. 처음 설정은 바로, 변경은 현재 주기가 끝난 뒤 적용(nextTargetCount)",
    response_model=Envelope[WeeklyGoal],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR)},
)
def put_weekly_goal(body: WeeklyGoalUpdate, request: Request, _: Principal = Depends(stub_user)):
    return ok(request, {**d.WEEKLY_GOAL, "nextTargetCount": body.target_count})


@router.get("/home", summary="홈 한 번에", description="화면 B-01에서 B-04 · BL-31. 각 항목 API도 따로 있음",
            response_model=Envelope[Home], responses=AUTH)
def home(request: Request, _: Principal = Depends(stub_user)):
    return ok(request, d.HOME)
