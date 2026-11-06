"""로그인 확인(스텁). 실제 토큰 검증은 계획 0015(서버 뼈대)에서 바꾼다.

스텁 규칙(Unity·Swagger에서 상태별 화면을 확인하려고):
- 헤더 없음·Bearer 아님 → 401 UNAUTHORIZED
- `Bearer expired` → 401 TOKEN_EXPIRED
- `Bearer signup` → 가입용 토큰(가입 전). 일반 API에 쓰면 403 SIGNUP_REQUIRED
- 그 밖의 아무 토큰 → 예시 사용자 usr_demo
"""
from dataclasses import dataclass
from typing import Optional

from fastapi import Depends
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from .errors import ApiError, ErrorCode

bearer = HTTPBearer(auto_error=False, description="POST /v1/auth/kakao·signup·refresh로 받은 토큰")


@dataclass
class Principal:
    user_id: Optional[str]
    signup_only: bool = False


def _principal(cred: Optional[HTTPAuthorizationCredentials]) -> Principal:
    if cred is None or cred.scheme.lower() != "bearer" or not cred.credentials:
        raise ApiError(ErrorCode.UNAUTHORIZED)
    if cred.credentials == "expired":
        raise ApiError(ErrorCode.TOKEN_EXPIRED)
    if cred.credentials == "signup":
        return Principal(user_id=None, signup_only=True)
    return Principal(user_id="usr_demo")


def current_user(cred: Optional[HTTPAuthorizationCredentials] = Depends(bearer)) -> Principal:
    """가입을 마친 사용자만."""
    p = _principal(cred)
    if p.signup_only:
        raise ApiError(ErrorCode.SIGNUP_REQUIRED)
    return p


def signup_or_user(cred: Optional[HTTPAuthorizationCredentials] = Depends(bearer)) -> Principal:
    """가입용 토큰도 허용(가입, 닉네임 확인)."""
    return _principal(cred)
