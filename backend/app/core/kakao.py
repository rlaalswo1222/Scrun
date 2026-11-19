"""카카오 토큰 확인. 앱이 카카오 iOS SDK로 받은 access 토큰 → 카카오 회원번호.

KAKAO_MODE=real(기본): 카카오 "액세스 토큰 정보 조회"(GET /v1/user/access_token_info)로 회원번호와 app_id를 받고,
    app_id가 우리 앱(KAKAO_APP_ID)이 아니면 거절한다(다른 앱에서 받은 토큰으로 남의 계정에 로그인하는 것을 막음).
    https://developers.kakao.com/docs/latest/ko/kakaologin/rest-api (액세스 토큰 정보 조회)
    https://developers.kakao.com/docs/latest/ko/rest-api/error-code (-401: 토큰 무효, -1: 카카오 내부 오류, -2: 인자 오류)
KAKAO_MODE=fake: 로컬·테스트용. `k_123` → 회원번호 123, 그 밖은 INVALID_KAKAO_TOKEN

연결 끊기(계정 삭제, 계획 0058): POST /v1/user/unlink, 어드민 키(KAKAO_ADMIN_KEY)로 회원번호를 지정.
    https://developers.kakao.com/docs/latest/ko/kakaologin/rest-api (연결 끊기). 이미 끊긴 사용자(-101)는 성공으로 봄
"""
import logging
import re
import threading
from typing import Optional, Protocol

import httpx

from .config import settings
from .errors import ApiError, ErrorCode

KAKAO_TOKEN_INFO_URL = "https://kapi.kakao.com/v1/user/access_token_info"
KAKAO_UNLINK_URL = "https://kapi.kakao.com/v1/user/unlink"
KAKAO_NOT_REGISTERED = -101  # 이 앱에 연결되지 않은 사용자(이미 끊김)
KAKAO_TIMEOUT_S = 5.0
KAKAO_BAD_ARGUMENT = -2  # 보낸 값(우리가 보내는 건 토큰뿐)이 형식에 안 맞음
log = logging.getLogger("scrun.kakao")


class KakaoClient(Protocol):
    def user_id(self, access_token: str) -> str: ...

    def unlink(self, kakao_user_id: str) -> None:
        """연결 끊기. 실패하면 SERVICE_UNAVAILABLE(부른 쪽은 아무것도 지우지 않음)."""
        ...


class FakeKakao:
    def user_id(self, access_token: str) -> str:
        m = re.fullmatch(r"k_(\d+)", access_token)
        if not m:
            raise ApiError(ErrorCode.INVALID_KAKAO_TOKEN)
        return m.group(1)

    def unlink(self, kakao_user_id: str) -> None:
        log.info("가짜 카카오 연결 끊기: %s", kakao_user_id)


def _unavailable(detail: str) -> ApiError:
    return ApiError(ErrorCode.SERVICE_UNAVAILABLE, f"카카오 {detail}")


def _json(r: httpx.Response) -> Optional[dict]:
    try:
        body = r.json()
    except ValueError:
        return None
    return body if isinstance(body, dict) else None


def _is_int(v) -> bool:
    return isinstance(v, int) and not isinstance(v, bool)


class RealKakao:
    def __init__(self, app_id: int, client: Optional[httpx.Client] = None, admin_key: Optional[str] = None):
        self.app_id = app_id
        self.client = client or _shared_client()
        self.admin_key = admin_key

    def unlink(self, kakao_user_id: str) -> None:
        if not self.admin_key:
            log.error("KAKAO_ADMIN_KEY가 없어 카카오 연결을 끊지 못함(계정 삭제 막힘)")
            raise _unavailable("연결 끊기를 할 수 없습니다(서버 설정). 잠시 뒤 다시 시도하세요.")
        try:
            r = self.client.post(KAKAO_UNLINK_URL, headers={"Authorization": f"KakaoAK {self.admin_key}"},
                                 data={"target_id_type": "user_id", "target_id": kakao_user_id})
        except httpx.HTTPError:
            raise _unavailable("서버에 연결하지 못했습니다.")
        body = _json(r)
        if r.status_code == 200 and body is not None and str(body.get("id")) == str(kakao_user_id):
            return
        if body is not None and body.get("code") == KAKAO_NOT_REGISTERED:
            return  # 이미 끊김(사용자가 카카오 쪽에서 먼저 끊었는데 웹훅을 놓침)
        log.warning("카카오 연결 끊기 실패: HTTP %s %s", r.status_code, (body or {}).get("code"))
        raise _unavailable("서버가 응답하지 않습니다. 잠시 뒤 다시 시도하세요.")

    def user_id(self, access_token: str) -> str:
        try:
            r = self.client.get(KAKAO_TOKEN_INFO_URL, headers={"Authorization": f"Bearer {access_token}"})
        except httpx.HTTPError:
            raise _unavailable("서버에 연결하지 못했습니다.")
        body = _json(r)
        if r.status_code == 401:
            raise ApiError(ErrorCode.INVALID_KAKAO_TOKEN)
        if r.status_code == 400 and body is not None and body.get("code") == KAKAO_BAD_ARGUMENT:
            raise ApiError(ErrorCode.INVALID_KAKAO_TOKEN)
        if r.status_code != 200:
            log.warning("카카오 토큰 확인 실패: HTTP %s %s", r.status_code, (body or {}).get("code"))
            raise _unavailable("서버가 응답하지 않습니다. 잠시 뒤 다시 시도하세요.")
        if body is None or not _is_int(body.get("id")) or not _is_int(body.get("app_id")):
            log.warning("카카오 토큰 확인 응답 형식이 다름: %r", sorted(body) if body else r.text[:200])
            raise _unavailable("응답이 올바르지 않습니다.")
        if body["app_id"] != self.app_id:
            log.warning("다른 앱에서 발급된 카카오 토큰 거절: app_id=%s", body["app_id"])
            raise ApiError(ErrorCode.INVALID_KAKAO_TOKEN)
        return str(body["id"])


_client: Optional[httpx.Client] = None
_client_lock = threading.Lock()  # 동기 엔드포인트는 스레드 풀에서 돌므로 첫 요청들이 동시에 만들 수 있음


def _shared_client() -> httpx.Client:
    """연결 재사용(요청마다 TLS 연결을 새로 맺지 않게). 서버 종료 때 close_client()."""
    global _client
    with _client_lock:
        if _client is None:
            _client = httpx.Client(timeout=KAKAO_TIMEOUT_S)
        return _client


def close_client() -> None:
    global _client
    with _client_lock:
        if _client is not None:
            _client.close()
            _client = None


def get_kakao() -> KakaoClient:
    """라우터는 Depends(get_kakao). 테스트는 이 함수를 바꿔 끼운다."""
    if settings.kakao_mode == "fake":
        return FakeKakao()
    return RealKakao(settings.kakao_app_id, admin_key=settings.kakao_admin_key)
