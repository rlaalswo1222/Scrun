"""진짜 로그인 확인(토큰 서명·만료·사용자 존재). 진짜가 된 API는 여기 의존성을 쓴다.
아직 스텁인 API는 core/auth.py(아무 토큰이나 통과)를 그대로 쓴다.
"""
from typing import Optional

from fastapi import Depends
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from sqlalchemy.orm import Session

from ..db.models import User
from ..db.session import get_db
from . import tokens
from .errors import ApiError, ErrorCode

bearer = HTTPBearer(auto_error=False, description="POST /v1/auth/kakao·signup·refresh로 받은 토큰")


def _claims(cred: Optional[HTTPAuthorizationCredentials]) -> dict:
    if cred is None or cred.scheme.lower() != "bearer" or not cred.credentials:
        raise ApiError(ErrorCode.UNAUTHORIZED)
    return tokens.decode(cred.credentials)


def _user(claims: dict, db: Session) -> User:
    """access 토큰의 사용자. 사용자 검사(탈퇴 등)는 여기 한 곳에만 둔다. 없으면 401."""
    user = db.get(User, claims["sub"])
    if user is None:
        raise ApiError(ErrorCode.UNAUTHORIZED)
    return user


def current_user(cred: Optional[HTTPAuthorizationCredentials] = Depends(bearer), db: Session = Depends(get_db)) -> User:
    """가입을 마친 사용자. 가입용 토큰이면 403 SIGNUP_REQUIRED, 탈퇴한 사용자 토큰이면 401."""
    claims = _claims(cred)
    if claims["typ"] == tokens.SIGNUP:
        raise ApiError(ErrorCode.SIGNUP_REQUIRED)
    return _user(claims, db)


def signup_identity(cred: Optional[HTTPAuthorizationCredentials] = Depends(bearer)) -> tuple[str, str]:
    """가입용 토큰 → (제공자, 제공자 쪽 회원번호). access 토큰이면 401(이미 가입한 사람)."""
    claims = _claims(cred)
    if claims["typ"] != tokens.SIGNUP:
        raise ApiError(ErrorCode.UNAUTHORIZED, "가입용 토큰이 아닙니다.")
    provider, _, provider_user_id = claims["sub"].partition(":")
    return provider, provider_user_id


def signup_or_user_id(cred: Optional[HTTPAuthorizationCredentials] = Depends(bearer),
                      db: Session = Depends(get_db)) -> Optional[str]:
    """가입용 토큰 또는 가입한 사용자(닉네임 확인). 가입용이면 None, 사용자면 그 id. 탈퇴한 사용자 토큰이면 401."""
    claims = _claims(cred)
    if claims["typ"] == tokens.SIGNUP:
        return None
    return _user(claims, db).id
