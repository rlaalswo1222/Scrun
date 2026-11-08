"""진짜 카카오 확인(RealKakao)을 가짜 HTTP 응답으로 검사. 실제 카카오에는 요청하지 않는다."""
import httpx
import pytest

from app.core.errors import ApiError, ErrorCode
from app.core.kakao import KAKAO_TOKEN_INFO_URL, RealKakao

APP_ID = 1641065


def kakao_with(handler) -> RealKakao:
    return RealKakao(APP_ID, client=httpx.Client(transport=httpx.MockTransport(handler)))


def respond(status: int, body=None, text: str | None = None):
    def handler(request: httpx.Request) -> httpx.Response:
        assert str(request.url) == KAKAO_TOKEN_INFO_URL
        assert request.headers["Authorization"] == "Bearer tok"
        if text is not None:
            return httpx.Response(status, text=text)
        return httpx.Response(status, json=body)
    return handler


def code_of(kakao: RealKakao) -> ErrorCode:
    with pytest.raises(ApiError) as e:
        kakao.user_id("tok")
    return e.value.code


def test_ok_returns_member_id():
    assert kakao_with(respond(200, {"id": 123456789, "expires_in": 7199, "app_id": APP_ID})).user_id("tok") == "123456789"


def test_token_from_other_app_rejected():
    assert code_of(kakao_with(respond(200, {"id": 1, "expires_in": 7199, "app_id": 999}))) == ErrorCode.INVALID_KAKAO_TOKEN


@pytest.mark.parametrize("status,body", [
    (401, {"code": -401, "msg": "InvalidTokenException"}),
    (400, {"code": -2, "msg": "bad argument"}),
])
def test_invalid_token(status, body):
    assert code_of(kakao_with(respond(status, body))) == ErrorCode.INVALID_KAKAO_TOKEN


@pytest.mark.parametrize("status,body,text", [
    (400, {"code": -1, "msg": "internal"}, None),  # 카카오 내부 오류(재시도 대상)
    (500, None, "oops"),
    (503, {"code": -9798, "msg": "점검"}, None),
    (200, None, "<html>not json</html>"),
    (200, ["not", "an", "object"], None),
    (200, {"id": 1, "expires_in": 7199}, None),  # app_id 없음
    (200, {"id": "1", "app_id": APP_ID}, None),  # id가 숫자가 아님
    (200, {"id": True, "app_id": APP_ID}, None),
])
def test_bad_kakao_response_is_503_not_500(status, body, text):
    assert code_of(kakao_with(respond(status, body, text))) == ErrorCode.SERVICE_UNAVAILABLE


def test_connection_error_is_503():
    def handler(request):
        raise httpx.ConnectTimeout("timeout", request=request)
    assert code_of(kakao_with(handler)) == ErrorCode.SERVICE_UNAVAILABLE
