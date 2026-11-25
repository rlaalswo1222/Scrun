"""우리 토큰.

- access 토큰(JWT, 1시간): API 호출용. 서버에 저장 안 함, 서명으로 확인
- 가입용 토큰(JWT, 30분): 카카오 확인은 됐지만 가입 전인 사람. 가입·닉네임 확인에만
- refresh 토큰(무작위 문자열, 30일): access 재발급용. DB엔 해시만(원문이 새도 못 씀)
- 실시간 토큰(JWT, 6시간): 맵 입장 때 그 맵 하나에만 붙을 수 있게 준다(계획 0029). API 호출에는 못 씀
"""
import hashlib
import secrets
import time

import jwt

from .config import settings
from .errors import ApiError, ErrorCode

ALGORITHM = "HS256"
ACCESS = "access"
SIGNUP = "signup"
REALTIME = "rt"


def _encode(sub: str, typ: str, ttl_s: int, **extra) -> str:
    now = int(time.time())
    return jwt.encode({"sub": sub, "typ": typ, "iat": now, "exp": now + ttl_s, **extra}, settings.jwt_secret,
                      algorithm=ALGORITHM)


def issue_access(user_id: str) -> str:
    return _encode(user_id, ACCESS, settings.access_token_ttl_s)


def issue_signup(provider: str, provider_user_id: str) -> str:
    return _encode(f"{provider}:{provider_user_id}", SIGNUP, settings.signup_token_ttl_s)


def issue_realtime(user_id: str, map_id: str) -> str:
    return _encode(user_id, REALTIME, settings.realtime_token_ttl_s, map=map_id)


# 시계 차이 여유(초). 발급 시각(iat)이 검사하는 쪽 시계보다 미래면 PyJWT가 거절하는데, 시계가 잠깐 뒤로 보정되면(가상 머신 시계 맞추기)
# 방금 받은 토큰이 401이 됨 — 0073 측정에서 로그인 직후 가상 사용자 2명이 한 바퀴씩 401(DB까지 안 감, 가설: Hyper-V TimeSync).
# 서버가 여럿이면 서버끼리 시계도 조금씩 다름. 여유는 발급 시각에만: PyJWT의 leeway는 만료(exp)에도 걸려 만료된 토큰이
# 30초 더 통하게 되므로(계정 삭제 같은 API까지, test_expired_token_cannot_delete) 만료는 여유 없이 따로 본다
CLOCK_SKEW_S = 30


def _decode(token: str) -> dict:
    try:
        claims = jwt.decode(token, settings.jwt_secret, algorithms=[ALGORITHM], leeway=CLOCK_SKEW_S,
                            options={"verify_exp": False, "require": ["exp", "iat"]})
    except jwt.InvalidTokenError:
        raise ApiError(ErrorCode.UNAUTHORIZED)
    if not isinstance(claims["exp"], (int, float)) or claims["exp"] <= time.time():
        raise ApiError(ErrorCode.TOKEN_EXPIRED)
    return claims


def decode_realtime(token: str) -> dict:
    """실시간 토큰 확인(sub·map 있음). 만료 → TOKEN_EXPIRED, 그 밖 → UNAUTHORIZED."""
    claims = _decode(token)
    if claims.get("typ") != REALTIME or not claims.get("sub") or not claims.get("map"):
        raise ApiError(ErrorCode.UNAUTHORIZED)
    return claims


def decode(token: str) -> dict:
    """서명·만료 확인. 만료 → TOKEN_EXPIRED, 그 밖의 문제 → UNAUTHORIZED."""
    claims = _decode(token)
    if claims.get("typ") not in (ACCESS, SIGNUP) or not claims.get("sub"):
        raise ApiError(ErrorCode.UNAUTHORIZED)
    return claims


def new_refresh() -> tuple[str, str]:
    """(원문, 해시). 원문은 앱에만 주고 DB엔 해시만."""
    raw = secrets.token_urlsafe(32)
    return raw, hash_refresh(raw)


def hash_refresh(raw: str) -> str:
    return hashlib.sha256(raw.encode()).hexdigest()
