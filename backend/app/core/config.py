"""환경 변수 설정. 값은 backend/.env(git 제외) 또는 실제 환경 변수에서. 예시는 .env.example.

잘못된 설정이면 import 시점에 ConfigError로 멈춘다(서버가 위험한 설정으로 뜨지 않게):
- JWT_SECRET: 꼭 있어야 하고 32자 이상, 저장소에 공개된 예전 기본값 금지
- KAKAO_MODE: real(기본) 또는 fake. fake는 적어야만 켜짐(아무 `k_숫자`로 로그인되므로 로컬·테스트 전용)
- KAKAO_APP_ID: real일 때 꼭 있어야 함(카카오 개발자 앱 ID, 숫자). 다른 앱에서 받은 토큰을 거르는 데 씀
"""
import logging
import os
import re
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Optional

from .envfile import DEFAULT_DATABASE_URL, load_env_file

log = logging.getLogger("scrun.config")

MIN_JWT_SECRET_LEN = 32
# 저장소(공개)에 한 번이라도 적힌 값. 이 값으로 서명하면 누구나 토큰을 만들 수 있다
PUBLIC_JWT_SECRETS = frozenset({"local-dev-secret-change-me-0123456789abcdef"})
KAKAO_MODES = ("real", "fake")
GENERATE_HINT = '../.venv/Scripts/python -c "import secrets; print(secrets.token_urlsafe(48))"'


class ConfigError(RuntimeError):
    pass


@dataclass(frozen=True)
class Settings:
    database_url: str
    jwt_secret: str
    kakao_mode: str
    kakao_app_id: Optional[int]
    access_token_ttl_s: int = 3600
    signup_token_ttl_s: int = 1800
    refresh_token_ttl_s: int = 30 * 24 * 3600
    refresh_reuse_grace_s: int = 30  # 교체 직후 같은 refresh 토큰 재시도 허용(응답 유실)
    max_sessions_per_user: int = 5  # 살아 있는 로그인(기기) 수 상한


def load_settings(env: Mapping[str, str]) -> Settings:
    jwt_secret = env.get("JWT_SECRET", "").strip()
    if not jwt_secret:
        raise ConfigError(f"JWT_SECRET이 없습니다. backend/.env에 넣으세요. 만들기: {GENERATE_HINT}")
    if jwt_secret in PUBLIC_JWT_SECRETS:
        raise ConfigError(f"JWT_SECRET이 저장소에 공개된 예전 기본값입니다. 새로 만드세요: {GENERATE_HINT}")
    if len(jwt_secret) < MIN_JWT_SECRET_LEN:
        raise ConfigError(f"JWT_SECRET은 {MIN_JWT_SECRET_LEN}자 이상이어야 합니다. 만들기: {GENERATE_HINT}")

    kakao_mode = env.get("KAKAO_MODE", "real").strip().lower() or "real"
    if kakao_mode not in KAKAO_MODES:
        raise ConfigError(f"KAKAO_MODE는 {' 또는 '.join(KAKAO_MODES)}여야 합니다(지금: {kakao_mode!r}).")

    raw_app_id = env.get("KAKAO_APP_ID", "").strip()
    kakao_app_id: Optional[int] = None
    if raw_app_id:
        if not re.fullmatch(r"[0-9]+", raw_app_id):  # isdigit()는 '²' 같은 글자도 통과시켜 int()가 터짐
            raise ConfigError(f"KAKAO_APP_ID는 숫자여야 합니다(카카오 개발자 > 내 애플리케이션 > 앱 ID). 지금: {raw_app_id!r}")
        kakao_app_id = int(raw_app_id)
    if kakao_mode == "real" and kakao_app_id is None:
        raise ConfigError("KAKAO_MODE=real에는 KAKAO_APP_ID가 필요합니다. 로컬 개발이면 KAKAO_MODE=fake를 적으세요.")

    return Settings(
        database_url=env.get("DATABASE_URL", DEFAULT_DATABASE_URL),
        jwt_secret=jwt_secret,
        kakao_mode=kakao_mode,
        kakao_app_id=kakao_app_id,
    )


load_env_file()
settings = load_settings(os.environ)
if settings.kakao_mode == "fake":
    log.warning("KAKAO_MODE=fake: 아무 `k_숫자` 토큰으로 로그인됩니다. 로컬·테스트 전용, 운영 서버에서 쓰면 안 됩니다.")
