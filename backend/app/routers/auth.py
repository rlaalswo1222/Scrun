"""로그인 API 5개 — 진짜 구현(계획 0016). DB에 저장하고 토큰을 발급한다."""
import logging
import secrets
from typing import Optional

from fastapi import APIRouter, Depends, Request, Response
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from ..core import nickname as nick
from ..core import sessions, tokens
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..core.kakao import KakaoClient, get_kakao
from ..core.security import current_user, signup_identity
from ..db.ids import new_id
from ..db.models import Agreement, AuthIdentity, User, UserAgreement, UserProgress
from ..db.session import get_db
from ..schemas.auth import (
    Agreement as AgreementOut,
    KakaoLoginRequest,
    LoginResult,
    LogoutRequest,
    RefreshRequest,
    SignupRequest,
    TokenPair,
)

router = APIRouter(prefix="/v1", tags=["Auth"])
E = ErrorCode
KAKAO = "KAKAO"
FRIEND_CODE_ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"  # 헷갈리는 0·O·1·I 뺌
SIGNUP_ATTEMPTS = 3  # 친구 코드가 동시에 겹치면 다시 시도
log = logging.getLogger("scrun.auth")


def _identity(db: Session, provider: str, provider_user_id: str) -> Optional[AuthIdentity]:
    return db.scalar(select(AuthIdentity).where(AuthIdentity.provider == provider,
                                                AuthIdentity.provider_user_id == provider_user_id))


def _new_friend_code(db: Session) -> str:
    for _ in range(10):
        code = "SCR-" + "".join(secrets.choice(FRIEND_CODE_ALPHABET) for _ in range(4))
        if db.scalar(select(User.id).where(User.friend_code == code)) is None:
            return code
    raise ApiError(E.INTERNAL_ERROR, "친구 코드를 만들지 못했습니다.")


@router.post(
    "/auth/kakao",
    summary="카카오 로그인",
    description="화면 A-05 · BL-1. 카카오 iOS SDK 토큰을 보내면 서버가 카카오에서 확인. "
    "이미 가입했으면 우리 토큰, 처음이면 isNewUser=true + signupToken(30분) → POST /v1/auth/signup. "
    "deviceId를 보내면 같은 기기의 예전 로그인은 폐기, 살아 있는 로그인은 사용자당 5개까지(넘으면 오래된 것부터 폐기)."
    "\n\n개발 서버(KAKAO_MODE=fake): 카카오 토큰 `k_123` → 카카오 회원번호 123",
    response_model=Envelope[LoginResult],
    responses=error_responses(E.VALIDATION_ERROR, E.INVALID_KAKAO_TOKEN, E.SERVICE_UNAVAILABLE),
)
def kakao_login(body: KakaoLoginRequest, request: Request, db: Session = Depends(get_db),
                kakao: KakaoClient = Depends(get_kakao)):
    kakao_id = kakao.user_id(body.kakao_access_token)
    identity = _identity(db, KAKAO, kakao_id)
    if identity is None:
        return ok(request, {"isNewUser": True, "signupToken": tokens.issue_signup(KAKAO, kakao_id)})
    issued = sessions.start(db, identity.user_id, body.device_id)
    db.commit()
    return ok(request, {"isNewUser": False, **issued})


@router.get(
    "/agreements",
    summary="가입 약관 목록",
    description="화면 A-06 · BL-1, BL-40. 로그인 없이",
    response_model=Envelope[list[AgreementOut]],
)
def agreements(request: Request, db: Session = Depends(get_db)):
    rows = db.scalars(select(Agreement).order_by(Agreement.sort)).all()
    return ok(request, [{"agreementId": a.id, "title": a.title, "required": a.required, "version": a.version, "url": a.url}
                        for a in rows])


@router.post(
    "/auth/signup",
    status_code=201,
    summary="가입",
    description="화면 A-06 · BL-1. 헤더에 가입용 토큰(signupToken). 약관 동의·닉네임·키·몸무게·시간대 → 계정 생성 + 우리 토큰. "
    "같은 카카오 계정으로 두 번 가입하면(동시에 와도) 새로 만들지 않고 기존 계정 토큰을 줌. "
    "닉네임은 서버가 정규화해서 검사·저장(GET /v1/users/nickname/check와 같은 판정)",
    response_model=Envelope[TokenPair],
    responses=error_responses(E.VALIDATION_ERROR, E.UNAUTHORIZED, E.TOKEN_EXPIRED,
                              E.NICKNAME_DUPLICATED, E.NICKNAME_NOT_ALLOWED, E.SERVICE_UNAVAILABLE),
)
def signup(body: SignupRequest, request: Request, identity: tuple[str, str] = Depends(signup_identity),
           db: Session = Depends(get_db)):
    provider, provider_user_id = identity
    for _ in range(SIGNUP_ATTEMPTS):
        existing = _identity(db, provider, provider_user_id)
        if existing is not None:  # 같은 가입을 다시 보냄(응답 유실 재시도·두 번 누름)
            issued = sessions.start(db, existing.user_id, body.device_id)
            db.commit()
            return ok(request, issued)
        try:
            issued = _create_user(db, body, provider, provider_user_id)
            db.commit()
            return ok(request, issued)
        except IntegrityError:
            # 동시에 들어온 다른 요청과 겹침. 무엇이 겹쳤는지 다시 조회해서 판단
            db.rollback()
            if _identity(db, provider, provider_user_id) is not None:
                continue  # 같은 카카오 계정이 먼저 가입됨 → 다음 바퀴에서 그 계정 토큰
            if nick.is_taken(db, body.nickname):
                raise ApiError(E.NICKNAME_DUPLICATED)
            log.warning("가입 중 충돌(친구 코드 추정) → 다시 시도")
    raise ApiError(E.SERVICE_UNAVAILABLE, "가입을 마치지 못했습니다. 다시 시도하세요.")


def _create_user(db: Session, body: SignupRequest, provider: str, provider_user_id: str) -> dict:
    """계정 + 제공자 연결 + 약관 동의 + 진행 상태 + 첫 로그인. 검사에 걸리면 ApiError, 동시 충돌이면 commit 때 IntegrityError."""
    all_agreements = {a.id: a for a in db.scalars(select(Agreement)).all()}
    unknown = sorted(set(body.agreed_ids) - set(all_agreements))
    missing = sorted(a.id for a in all_agreements.values() if a.required and a.id not in body.agreed_ids)
    if unknown or missing:
        reason = f"없는 약관: {unknown}" if unknown else f"필수 약관 빠짐: {missing}"
        raise ApiError(E.VALIDATION_ERROR, "약관 동의를 확인하세요.", {"fields": [{"field": "agreedIds", "reason": reason}]})

    name = nick.require_usable(db, body.nickname)  # 요청 모델에서도 형식을 보지만, 여기가 최종 판정(내 정보 수정과 같은 함수)

    user = User(id=new_id(), nickname=name, height_cm=body.height_cm, weight_kg=body.weight_kg,
                timezone=body.timezone, friend_code=_new_friend_code(db))
    db.add(user)
    db.flush()
    db.add(AuthIdentity(id=new_id(), user_id=user.id, provider=provider, provider_user_id=provider_user_id))
    for agreement_id in sorted(set(body.agreed_ids)):
        db.add(UserAgreement(user_id=user.id, agreement_id=agreement_id, version=all_agreements[agreement_id].version))
    db.add(UserProgress(user_id=user.id))
    return sessions.start(db, user.id, body.device_id)


@router.post(
    "/auth/refresh",
    summary="토큰 재발급",
    description="BL-44. 401 TOKEN_EXPIRED를 받으면 앱이 자동 호출하고 원래 요청을 한 번 다시 보냄. "
    "쓴 refresh 토큰은 폐기되고 새 refresh 토큰이 나옴(한 번만 쓸 수 있음). 앱은 재발급을 한 번에 하나만 보낸다. "
    "응답을 못 받아 30초 안에 같은 토큰으로 다시 보내면 새 토큰을 다시 줌. "
    "그 밖에 이미 쓴 토큰이 다시 오면 탈취로 보고 그 로그인(이 기기)만 폐기 → 401, 다시 로그인.",
    response_model=Envelope[TokenPair],
    responses=error_responses(E.VALIDATION_ERROR, E.UNAUTHORIZED, E.TOKEN_EXPIRED),
)
def refresh(body: RefreshRequest, request: Request, db: Session = Depends(get_db)):
    return ok(request, sessions.rotate(db, body.refresh_token))


@router.post(
    "/auth/logout",
    status_code=204,
    summary="로그아웃",
    description="화면 H-10 · BL-36. 이 기기의 로그인(그 refresh 토큰과 거기서 이어진 토큰) 폐기. 이미 폐기됐거나 남의 토큰이어도 204(알려 주지 않음)",
    responses=error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED),
)
def logout(body: LogoutRequest, user: User = Depends(current_user), db: Session = Depends(get_db)):
    sessions.end(db, user.id, body.refresh_token)
    return Response(status_code=204)
