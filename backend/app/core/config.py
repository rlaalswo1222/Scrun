"""환경 변수 설정. 값은 backend/.env(git 제외) 또는 실제 환경 변수에서. 예시는 .env.example.

잘못된 설정이면 import 시점에 ConfigError로 멈춘다(서버가 위험한 설정으로 뜨지 않게):
- JWT_SECRET: 꼭 있어야 하고 32자 이상, 저장소에 공개된 예전 기본값 금지
- KAKAO_MODE: real(기본) 또는 fake. fake는 적어야만 켜짐(아무 `k_숫자`로 로그인되므로 로컬·테스트 전용)
- KAKAO_APP_ID: real일 때 꼭 있어야 함(카카오 개발자 앱 ID, 숫자). 다른 앱에서 받은 토큰을 거르는 데 씀
- OCR_ENGINE: none(기본, 사진 인증은 503) 또는 fake(파일 이름으로 값을 정하는 가짜, 로컬·테스트 전용). 진짜 엔진은 ADR-0012 뒤
- VERIFY_*: 계기판 인증 판정 값(계획 0038). 없으면 아래 VerificationPolicy 기본값
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
OCR_ENGINES = ("none", "fake")
GENERATE_HINT = '../.venv/Scripts/python -c "import secrets; print(secrets.token_urlsafe(48))"'


class ConfigError(RuntimeError):
    pass


@dataclass(frozen=True)
class VerificationPolicy:
    """계기판 인증 판정 값(계획 0038 정할 것 2·3, 11/16 지환 승인). 첫 값은 가설: 헬스장 사진이 모이면 다시 정한다.

    거리 15%: 10/29 다른 날 센서 오차 평균 0.52 km/h(8–10 km/h에서 5–7%), 나쁠 때 1.28 km/h(13–18%).
    시간: 계기판은 카운트다운 전 걷기부터 재므로 앱보다 길다(−30초에서 +5분). 일시정지가 있었으면 위쪽을 쉰 만큼 넓힌다
    (앱 시간은 일시정지를 뺀 값, 계기판은 기종마다 일시정지 중 시간이 흐르거나 멈춤. 정할 것 5, 11/16 지환 결정).
    """

    distance_tolerance: float = 0.15   # |계기판 − 센서| / 계기판
    duration_min_s: int = -30           # 계기판 − 센서(초), 이보다 짧으면 어긋남
    duration_max_s: int = 300           # + 쉰 시간
    deadline_s: int = 600               # 서버가 종료를 받은 뒤 인증할 수 있는 시간
    max_retakes: int = 3                # 못 읽어 다시 찍기 횟수(첫 장 뒤)
    max_photos: int = 3                 # 한 번에 올리는 사진(브래킷 3장, ADR-0011)
    max_photo_bytes: int = 4 * 1024 * 1024
    ocr_timeout_s: float = 15.0


# (환경 변수, 필드, 형, 최소, 최대). 시간 최소는 0 이하·최대는 0 이상이라 둘의 순서가 뒤집힐 수 없음
VERIFY_ENV = (
    ("VERIFY_DISTANCE_TOLERANCE", "distance_tolerance", float, 0.01, 1.0),
    ("VERIFY_DURATION_MIN_S", "duration_min_s", int, -3600, 0),
    ("VERIFY_DURATION_MAX_S", "duration_max_s", int, 0, 3600),
    ("VERIFY_DEADLINE_S", "deadline_s", int, 60, 24 * 3600),
    ("VERIFY_MAX_RETAKES", "max_retakes", int, 0, 20),
    ("VERIFY_OCR_TIMEOUT_S", "ocr_timeout_s", float, 1.0, 120.0),
)


def load_verification(env: Mapping[str, str]) -> VerificationPolicy:
    values = {}
    for name, field, cast, lo, hi in VERIFY_ENV:
        raw = env.get(name, "").strip()
        if not raw:
            continue
        try:
            v = cast(raw)
        except ValueError:
            raise ConfigError(f"{name}는 {'정수' if cast is int else '숫자'}여야 합니다(지금: {raw!r}).") from None
        if not lo <= v <= hi:
            raise ConfigError(f"{name}는 {lo}에서 {hi} 사이여야 합니다(지금: {v}).")
        values[field] = v
    return VerificationPolicy(**values)


@dataclass(frozen=True)
class Settings:
    database_url: str
    jwt_secret: str
    kakao_mode: str
    kakao_app_id: Optional[int]
    access_token_ttl_s: int = 3600
    signup_token_ttl_s: int = 1800
    realtime_token_ttl_s: int = 6 * 3600  # 맵 하나에 붙는 실시간 토큰(계획 0029)
    refresh_token_ttl_s: int = 30 * 24 * 3600
    refresh_reuse_grace_s: int = 30  # 교체 직후 같은 refresh 토큰 재시도 허용(응답 유실)
    max_sessions_per_user: int = 5  # 살아 있는 로그인(기기) 수 상한
    ocr_engine: str = "none"
    verification: VerificationPolicy = VerificationPolicy()


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

    ocr_engine = env.get("OCR_ENGINE", "none").strip().lower() or "none"
    if ocr_engine not in OCR_ENGINES:
        raise ConfigError(f"OCR_ENGINE은 {' 또는 '.join(OCR_ENGINES)}여야 합니다(지금: {ocr_engine!r}).")

    return Settings(
        database_url=env.get("DATABASE_URL", DEFAULT_DATABASE_URL),
        jwt_secret=jwt_secret,
        kakao_mode=kakao_mode,
        kakao_app_id=kakao_app_id,
        ocr_engine=ocr_engine,
        verification=load_verification(env),
    )


load_env_file()
settings = load_settings(os.environ)
if settings.kakao_mode == "fake":
    log.warning("KAKAO_MODE=fake: 아무 `k_숫자` 토큰으로 로그인됩니다. 로컬·테스트 전용, 운영 서버에서 쓰면 안 됩니다.")
if settings.ocr_engine == "fake":
    log.warning("OCR_ENGINE=fake: 사진 파일 이름으로 계기판 값을 정합니다. 로컬·테스트 전용, 운영 서버에서 쓰면 안 됩니다.")
