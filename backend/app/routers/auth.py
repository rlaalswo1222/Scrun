"""로그인 API 5개 — 진짜 구현(계획 0016). DB에 저장하고 토큰을 발급한다."""
import secrets
from datetime import datetime, timedelta, timezone

from fastapi import APIRouter, Depends, Request, Response
from sqlalchemy import func, select, update
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from ..core import nickname as nick
from ..core import tokens
from ..core.config import settings
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..core.kakao import KakaoClient, get_kakao
from ..core.security import current_user, signup_identity
from ..db.ids import new_id
from ..db.models import Agreement, AuthIdentity, RefreshToken, User, UserAgreement, UserProgress
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


def _now() -> datetime:
    return datetime.now(timezone.utc)


def _issue_tokens(db: Session, user_id: str) -> dict:
    """access + refresh 발급. refresh는 해시만 저장. 호출한 쪽이 commit."""
    raw, token_hash = tokens.new_refresh()
    db.add(RefreshToken(id=new_id(), user_id=user_id, token_hash=token_hash,
                        expires_at=_now() + timedelta(seconds=settings.refresh_token_ttl_s)))
    return {"accessToken": tokens.issue_access(user_id), "refreshToken": raw, "expiresInS": settings.access_token_ttl_s}


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
    "이미 가입했으면 우리 토큰, 처음이면 isNewUser=true + signupToken(30분) → POST /v1/auth/signup."
    "\n\n개발 서버(KAKAO_MODE=fake): 카카오 토큰 `k_123` → 카카오 회원번호 123",
    response_model=Envelope[LoginResult],
    responses=error_responses(E.VALIDATION_ERROR, E.INVALID_KAKAO_TOKEN, E.SERVICE_UNAVAILABLE),
)
def kakao_login(body: KakaoLoginRequest, request: Request, db: Session = Depends(get_db),
                kakao: KakaoClient = Depends(get_kakao)):
    kakao_id = kakao.user_id(body.kakao_access_token)
    identity = db.scalar(select(AuthIdentity).where(AuthIdentity.provider == KAKAO,
                                                    AuthIdentity.provider_user_id == kakao_id))
    if identity is None:
        return ok(request, {"isNewUser": True, "signupToken": tokens.issue_signup(KAKAO, kakao_id)})
    issued = _issue_tokens(db, identity.user_id)
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
    "같은 카카오 계정으로 두 번 가입하면 새로 만들지 않고 기존 계정 토큰을 줌",
    response_model=Envelope[TokenPair],
    responses=error_responses(E.VALIDATION_ERROR, E.UNAUTHORIZED, E.TOKEN_EXPIRED,
                              E.NICKNAME_DUPLICATED, E.NICKNAME_NOT_ALLOWED),
)
def signup(body: SignupRequest, request: Request, identity: tuple[str, str] = Depends(signup_identity),
           db: Session = Depends(get_db)):
    provider, provider_user_id = identity
    existing = db.scalar(select(AuthIdentity).where(AuthIdentity.provider == provider,
                                                    AuthIdentity.provider_user_id == provider_user_id))
    if existing is not None:
        issued = _issue_tokens(db, existing.user_id)
        db.commit()
        return ok(request, issued)

    all_agreements = {a.id: a for a in db.scalars(select(Agreement)).all()}
    unknown = sorted(set(body.agreed_ids) - set(all_agreements))
    missing = sorted(a.id for a in all_agreements.values() if a.required and a.id not in body.agreed_ids)
    if unknown or missing:
        reason = f"없는 약관: {unknown}" if unknown else f"필수 약관 빠짐: {missing}"
        raise ApiError(E.VALIDATION_ERROR, "약관 동의를 확인하세요.", {"fields": [{"field": "agreedIds", "reason": reason}]})

    name = nick.normalize(body.nickname)
    if not nick.is_allowed(name):
        raise ApiError(E.NICKNAME_NOT_ALLOWED)
    if db.scalar(select(User.id).where(func.lower(User.nickname) == name.lower())) is not None:
        raise ApiError(E.NICKNAME_DUPLICATED)

    user = User(id=new_id(), nickname=name, height_cm=body.height_cm, weight_kg=body.weight_kg,
                timezone=body.timezone, friend_code=_new_friend_code(db))
    db.add(user)
    db.flush()
    db.add(AuthIdentity(id=new_id(), user_id=user.id, provider=provider, provider_user_id=provider_user_id))
    for agreement_id in sorted(set(body.agreed_ids)):
        db.add(UserAgreement(user_id=user.id, agreement_id=agreement_id, version=all_agreements[agreement_id].version))
    db.add(UserProgress(user_id=user.id))
    issued = _issue_tokens(db, user.id)
    try:
        db.commit()
    except IntegrityError:  # 동시에 같은 닉네임으로 가입한 경우
        db.rollback()
        raise ApiError(E.NICKNAME_DUPLICATED)
    return ok(request, issued)


@router.post(
    "/auth/refresh",
    summary="토큰 재발급",
    description="BL-44. 401 TOKEN_EXPIRED를 받으면 앱이 자동 호출하고 원래 요청을 한 번 다시 보냄. "
    "쓴 refresh 토큰은 폐기되고 새 refresh 토큰이 나옴(한 번만 쓸 수 있음). "
    "이미 폐기된 refresh 토큰이 다시 오면 탈취로 보고 그 사용자의 refresh 토큰을 전부 폐기(다시 로그인).",
    response_model=Envelope[TokenPair],
    responses=error_responses(E.VALIDATION_ERROR, E.UNAUTHORIZED, E.TOKEN_EXPIRED),
)
def refresh(body: RefreshRequest, request: Request, db: Session = Depends(get_db)):
    row = db.scalar(select(RefreshToken).where(RefreshToken.token_hash == tokens.hash_refresh(body.refresh_token)))
    if row is None:
        raise ApiError(E.UNAUTHORIZED)
    now = _now()
    if row.revoked_at is not None:
        db.execute(update(RefreshToken).where(RefreshToken.user_id == row.user_id, RefreshToken.revoked_at.is_(None))
                   .values(revoked_at=now))
        db.commit()
        raise ApiError(E.UNAUTHORIZED, "다시 로그인하세요.")
    if row.expires_at <= now:
        raise ApiError(E.TOKEN_EXPIRED, "refresh 토큰이 만료되었습니다. 다시 로그인하세요.")
    row.revoked_at = now
    issued = _issue_tokens(db, row.user_id)
    db.commit()
    return ok(request, issued)


@router.post(
    "/auth/logout",
    status_code=204,
    summary="로그아웃",
    description="화면 H-10 · BL-36. 이 기기의 refresh 토큰 폐기. 이미 폐기됐거나 남의 토큰이어도 204(알려 주지 않음)",
    responses=error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED),
)
def logout(body: LogoutRequest, user: User = Depends(current_user), db: Session = Depends(get_db)):
    db.execute(update(RefreshToken)
               .where(RefreshToken.token_hash == tokens.hash_refresh(body.refresh_token),
                      RefreshToken.user_id == user.id, RefreshToken.revoked_at.is_(None))
               .values(revoked_at=_now()))
    db.commit()
    return Response(status_code=204)
