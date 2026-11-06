from fastapi import APIRouter, Depends, Request, Response

from .. import stub_data as d
from ..core.auth import Principal, current_user, signup_or_user
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..schemas.auth import (
    Agreement,
    KakaoLoginRequest,
    LoginResult,
    LogoutRequest,
    RefreshRequest,
    SignupRequest,
    TokenPair,
)

router = APIRouter(prefix="/v1", tags=["Auth"])
E = ErrorCode


@router.post(
    "/auth/kakao",
    summary="카카오 로그인",
    description="화면 A-05 · BL-1. 카카오 iOS SDK 토큰을 보내면 서버가 카카오에서 확인. 처음이면 isNewUser=true + signupToken."
    "\n\n스텁: 토큰 `new` → 새 사용자, `invalid` → INVALID_KAKAO_TOKEN, 그 밖 → 기존 사용자",
    response_model=Envelope[LoginResult],
    responses=error_responses(E.VALIDATION_ERROR, E.INVALID_KAKAO_TOKEN),
)
def kakao_login(body: KakaoLoginRequest, request: Request):
    if body.kakao_access_token == "invalid":
        raise ApiError(E.INVALID_KAKAO_TOKEN)
    return ok(request, d.LOGIN_NEW if body.kakao_access_token == "new" else d.LOGIN_EXISTING)


@router.get(
    "/agreements",
    summary="가입 약관 목록",
    description="화면 A-06 · BL-1, BL-31. 로그인 없이",
    response_model=Envelope[list[Agreement]],
)
def agreements(request: Request):
    return ok(request, d.AGREEMENTS)


@router.post(
    "/auth/signup",
    status_code=201,
    summary="가입",
    description="화면 A-06 · BL-1. 가입용 토큰 필요. 약관 동의·닉네임·키·몸무게·시간대.\n\n스텁: 닉네임 `taken` → NICKNAME_DUPLICATED",
    response_model=Envelope[TokenPair],
    responses=error_responses(E.VALIDATION_ERROR, E.UNAUTHORIZED, E.NICKNAME_DUPLICATED, E.NICKNAME_NOT_ALLOWED),
)
def signup(body: SignupRequest, request: Request, _: Principal = Depends(signup_or_user)):
    required = {a["agreementId"] for a in d.AGREEMENTS if a["required"]}
    missing = sorted(required - set(body.agreed_ids))
    if missing:
        raise ApiError(E.VALIDATION_ERROR, "필수 약관에 동의해야 합니다.", {"fields": [{"field": "agreedIds", "reason": f"빠짐: {missing}"}]})
    if body.nickname == "taken":
        raise ApiError(E.NICKNAME_DUPLICATED)
    return ok(request, d.TOKENS)


@router.post(
    "/auth/refresh",
    summary="토큰 재발급",
    description="BL-43. 401 TOKEN_EXPIRED를 받으면 앱이 자동 호출하고 원래 요청을 한 번 다시 보냄.\n\n스텁: `expired` → TOKEN_EXPIRED(refresh도 만료 → 다시 로그인)",
    response_model=Envelope[TokenPair],
    responses=error_responses(E.VALIDATION_ERROR, E.TOKEN_EXPIRED),
)
def refresh(body: RefreshRequest, request: Request):
    if body.refresh_token == "expired":
        raise ApiError(E.TOKEN_EXPIRED)
    return ok(request, d.TOKENS)


@router.post(
    "/auth/logout",
    status_code=204,
    summary="로그아웃",
    description="화면 H-10 · BL-25. refresh 토큰 폐기",
    responses=error_responses(E.UNAUTHORIZED),
)
def logout(body: LogoutRequest, _: Principal = Depends(current_user)):
    return Response(status_code=204)
