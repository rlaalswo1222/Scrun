from typing import Optional

from pydantic import Field

from ..core.model import CamelModel


class KakaoLoginRequest(CamelModel):
    kakao_access_token: str = Field(min_length=1, description="앱이 카카오 iOS SDK로 받은 토큰")


class LoginResult(CamelModel):
    is_new_user: bool = Field(description="true면 가입 화면으로. 그때는 signupToken만 있음")
    access_token: Optional[str] = None
    refresh_token: Optional[str] = None
    expires_in_s: Optional[int] = Field(default=None, description="access 토큰 수명(초)")
    signup_token: Optional[str] = Field(default=None, description="가입·닉네임 확인에만 쓰는 토큰")


class Agreement(CamelModel):
    agreement_id: str
    title: str
    required: bool
    version: str
    url: str = Field(description="약관 본문 주소")


class SignupRequest(CamelModel):
    agreed_ids: list[str] = Field(description="동의한 약관 ID. 필수 약관이 빠지면 400")
    nickname: str = Field(min_length=2, max_length=12)
    height_cm: int = Field(ge=100, le=230, description="아바타 크기에 씀")
    weight_kg: int = Field(ge=20, le=250, description="속도 추정엔 안 씀. 용도는 개인정보처리방침에 명시")
    timezone: str = Field(default="Asia/Seoul", description="주간 목표 주기 계산용")


class TokenPair(CamelModel):
    access_token: str
    refresh_token: str
    expires_in_s: int


class RefreshRequest(CamelModel):
    refresh_token: str = Field(min_length=1)


class LogoutRequest(CamelModel):
    refresh_token: str = Field(min_length=1)
