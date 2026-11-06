"""카카오 토큰 확인. 앱이 카카오 iOS SDK로 받은 access 토큰 → 카카오 회원번호.

KAKAO_MODE=fake(기본): 개발·테스트용. `k_123` → 회원번호 123, 그 밖은 INVALID_KAKAO_TOKEN
KAKAO_MODE=real: 카카오 API(사용자 정보 가져오기)로 확인. 카카오 개발자 앱 등록 뒤
"""
import re
from typing import Protocol

import httpx

from .config import settings
from .errors import ApiError, ErrorCode

KAKAO_ME_URL = "https://kapi.kakao.com/v2/user/me"


class KakaoClient(Protocol):
    def user_id(self, access_token: str) -> str: ...


class FakeKakao:
    def user_id(self, access_token: str) -> str:
        m = re.fullmatch(r"k_(\d+)", access_token)
        if not m:
            raise ApiError(ErrorCode.INVALID_KAKAO_TOKEN)
        return m.group(1)


class RealKakao:
    def user_id(self, access_token: str) -> str:
        try:
            r = httpx.get(KAKAO_ME_URL, headers={"Authorization": f"Bearer {access_token}"}, timeout=5.0)
        except httpx.HTTPError:
            raise ApiError(ErrorCode.SERVICE_UNAVAILABLE, "카카오에 연결하지 못했습니다.")
        if r.status_code == 401:
            raise ApiError(ErrorCode.INVALID_KAKAO_TOKEN)
        if r.status_code != 200 or "id" not in r.json():
            raise ApiError(ErrorCode.SERVICE_UNAVAILABLE, "카카오 응답이 올바르지 않습니다.")
        return str(r.json()["id"])


def get_kakao() -> KakaoClient:
    """라우터는 Depends(get_kakao). 테스트는 이 함수를 바꿔 끼운다."""
    return RealKakao() if settings.kakao_mode == "real" else FakeKakao()
