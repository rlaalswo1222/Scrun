"""환경 변수 설정. 값은 backend/.env(git 제외) 또는 실제 환경 변수에서. 예시는 .env.example.

잘못된 설정이면 import 시점에 ConfigError로 멈춘다(서버가 위험한 설정으로 뜨지 않게):
- JWT_SECRET: 꼭 있어야 하고 32자 이상, 저장소에 공개된 예전 기본값 금지
- KAKAO_MODE: real(기본) 또는 fake. fake는 적어야만 켜짐(아무 `k_숫자`로 로그인되므로 로컬·테스트 전용)
- KAKAO_APP_ID: real일 때 꼭 있어야 함(카카오 개발자 앱 ID, 숫자). 다른 앱에서 받은 토큰을 거르는 데 씀
- OCR_ENGINE: none(기본, 사진 인증은 503), fake(파일 이름으로 값을 정하는 가짜, 로컬·테스트 전용),
  onnx(진짜 엔진, 계획 0046: app/console_ocr 자식 프로세스 2개, 서버 compose가 켬. requirements의 onnxruntime·rapidocr 필요)
- VERIFY_*: 계기판 인증 판정 값(계획 0038). 없으면 아래 VerificationPolicy 기본값
- BOT_FILL_TO·BOT_PACES_KMH: 페이스 봇(계획 0040). 없으면 아래 BotPolicy 기본값, BOT_FILL_TO=0이면 봇 없음
- FRIEND_ADD_FAIL_LIMIT·FRIEND_ADD_FAIL_WINDOW_S: 틀린 친구 코드 제한(계획 0044). 없으면 아래 FriendPolicy 기본값
- REQUEST_LOG: 요청마다 처리 시간·DB 쿼리 수를 한 줄씩(계획 0070). 기본 켜짐, 0이면 끔
- ADMISSION_LIMIT·ADMISSION_WAIT_S: 동시 처리 번호표 수(0이면 끔)·기다리는 상한 초(계획 0071). DB_POOL_TIMEOUT_S: 연결 기다림 상한
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
OCR_ENGINES = ("none", "fake", "onnx")   # onnx: 진짜 엔진(계획 0046, app/console_ocr)
GENERATE_HINT = '../.venv/Scripts/python -c "import secrets; print(secrets.token_urlsafe(48))"'


class ConfigError(RuntimeError):
    pass


@dataclass(frozen=True)
class VerificationPolicy:
    """계기판 인증 판정 값(계획 0038 정할 것 2·3, 11/16 지환 승인). 첫 값은 가설: 헬스장 사진이 모이면 다시 정한다.

    거리 40%(11/25 민재, 계획 0069. 처음 15%): 인증은 "계기판과 거의 같게 뛰었나"가 아니라 "계기판과 비교해 말이 안 되는
    값인가"(다른 기록 사진·잘못 읽음)를 거른다. 인증되면 공식 기록은 계기판 값이라 센서 오차로 떨어뜨릴 이유가 없음.
    센서 오차는 나쁜 날 13–18%(10/29 다른 날 1.28 km/h), 11/21 실사용 −17.5%가 15%에 떨어짐 → 그 약 두 배(가설).
    시간: 계기판은 카운트다운 전 걷기부터 재므로 앱보다 길다(−30초에서 +5분). 일시정지가 있었으면 위쪽을 쉰 만큼 넓힌다
    (앱 시간은 일시정지를 뺀 값, 계기판은 기종마다 일시정지 중 시간이 흐르거나 멈춤. 정할 것 5, 11/16 지환 결정).
    """

    distance_tolerance: float = 0.40   # |계기판 − 센서| / 계기판. 센서 3000 m면 계기판 2143–5000 m
    duration_min_s: int = -30           # 계기판 − 센서(초), 이보다 짧으면 어긋남
    duration_max_s: int = 300           # + 쉰 시간
    max_pause_s: int = 600              # 쉰 시간 상한. 쉰 시간 = (종료 − 시작) − 달린 시간인데 앱이 꺼졌다 몇 시간 뒤 복구하면
                                        # 종료가 늦게 찍혀 시간 검사가 무의미해짐 → 넘는 만큼은 넓히지 않음(가설, 11/17 민재 리뷰)
    deadline_s: int = 600               # 서버가 종료를 받은 뒤 인증할 수 있는 시간
    max_retakes: int = 3                # 못 읽어 다시 찍기 횟수(첫 장 뒤)
    max_photos: int = 3                 # 한 번에 올리는 사진(브래킷 3장, ADR-0011)
    max_photo_bytes: int = 4 * 1024 * 1024
    ocr_timeout_s: float = 15.0


@dataclass(frozen=True)
class AdmissionPolicy:
    """동시 처리 제한(계획 0071, app/core/admission.py). 번호표는 DB 연결 합계(db_pool_total) 중 실시간 WebSocket 몫 3을 뺀 12
    (가설: 실시간은 입장·퇴장 때만 짧게 씀). 0071 측정으로 10·12·15를 비교해 정함. 0이면 끔. 연결 합계보다 크면 ConfigError
    (번호표가 연결보다 많으면 연결을 기다리는 요청이 다시 생김)."""

    limit: int = 12
    wait_s: float = 2.0            # 번호표를 이만큼 못 받으면 503. 받아 둔 요청이 쌓이는 상한
    db_pool_timeout_s: float = 5.0  # 연결 풀 기다림 상한(SQLAlchemy 기본 30초). 넘으면 503 DB_BUSY
    db_pool_size: int = 5           # 연결 풀(app/db/session.py). 합계 = size + overflow = 15
    db_pool_overflow: int = 10

    @property
    def db_pool_total(self) -> int:
        return self.db_pool_size + self.db_pool_overflow


ADMISSION_ENV = (
    ("ADMISSION_LIMIT", "limit", int, 0, 200),
    ("ADMISSION_WAIT_S", "wait_s", float, 0.1, 30.0),
    ("DB_POOL_TIMEOUT_S", "db_pool_timeout_s", float, 1.0, 60.0),
)


def _load_ranged(env: Mapping[str, str], table, policy_cls):
    values = {}
    for name, field, cast, lo, hi in table:
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
    return policy_cls(**values)


def _load_admission(env: Mapping[str, str]) -> AdmissionPolicy:
    policy = _load_ranged(env, ADMISSION_ENV, AdmissionPolicy)
    if policy.limit > policy.db_pool_total:
        raise ConfigError(f"ADMISSION_LIMIT({policy.limit})는 DB 연결 합계({policy.db_pool_total}) 이하여야 합니다.")
    return policy


# (환경 변수, 필드, 형, 최소, 최대). 시간 최소는 0 이하·최대는 0 이상이라 둘의 순서가 뒤집힐 수 없음
VERIFY_ENV = (
    ("VERIFY_DISTANCE_TOLERANCE", "distance_tolerance", float, 0.01, 1.0),
    ("VERIFY_DURATION_MIN_S", "duration_min_s", int, -3600, 0),
    ("VERIFY_DURATION_MAX_S", "duration_max_s", int, 0, 3600),
    ("VERIFY_MAX_PAUSE_S", "max_pause_s", int, 0, 7200),
    ("VERIFY_DEADLINE_S", "deadline_s", int, 60, 24 * 3600),
    ("VERIFY_MAX_RETAKES", "max_retakes", int, 0, 20),
    ("VERIFY_OCR_TIMEOUT_S", "ocr_timeout_s", float, 1.0, 120.0),
)


def load_verification(env: Mapping[str, str]) -> VerificationPolicy:
    return _load_ranged(env, VERIFY_ENV, VerificationPolicy)


@dataclass(frozen=True)
class BotPolicy:
    """페이스 봇(계획 0040, BL-23 ②: 수·페이스는 서버 설정값). 맵마다 사람 + 봇 = fill_to가 되게 채운다(사람만 셈, 고스트는 안 셈).
    사람이 늘면 사람에게서 가장 먼 봇이 빠지고, 줄면 빈 페이스 중 목록 앞쪽부터 들어온다. 첫 값은 가설: 걷기에 가까운 6부터 빠른 12까지."""

    fill_to: int = 6
    paces_kmh: tuple[float, ...] = (9.0, 7.5, 10.5, 6.0, 12.0)


@dataclass(frozen=True)
class FriendPolicy:
    """친구(계획 0044). 최대 인원은 11/06 확정(BL-27 ⑤). 틀린 코드 제한은 가설 값: 코드를 잘못 옮겨 적는 사람은 몇 번이면
    맞추고, 맞혀 보려는 사람은 10분에 10번이면 1조 개 중 하나를 못 찾는다."""

    max_friends: int = 50
    add_fail_limit: int = 10       # 이 횟수만큼 틀리면 그다음부터 RATE_LIMITED
    add_fail_window_s: int = 600   # 최근 이 시간 안의 틀린 코드만 셈


FRIEND_ENV = (
    ("FRIEND_ADD_FAIL_LIMIT", "add_fail_limit", 1, 1000),
    ("FRIEND_ADD_FAIL_WINDOW_S", "add_fail_window_s", 10, 24 * 3600),
)


def load_friends(env: Mapping[str, str]) -> FriendPolicy:
    values = {}
    for name, field, lo, hi in FRIEND_ENV:
        raw = env.get(name, "").strip()
        if not raw:
            continue
        if not re.fullmatch(r"[0-9]+", raw) or not lo <= int(raw) <= hi:
            raise ConfigError(f"{name}는 {lo}에서 {hi} 사이 정수여야 합니다(지금: {raw!r}).")
        values[field] = int(raw)
    return FriendPolicy(**values)


@dataclass(frozen=True)
class GrowthPolicy:
    """성장(계획 0048, BL-32·34·30). 값은 상준 백로그 확정값: 레벨 임계값 Lv.2 3km·Lv.3 10km·Lv.4 25km·Lv.5 50km·Lv.6 100km
    (11/06), 러닝복 6세트(기본 → 엘리트), 주간 목표 1회 = 인증 완료 + 5분 이상, 한 주 = 설정한 날부터 7일.
    Lv.6 뒤 임계값은 Q-25 열린 질문이라 없음(정할 것 1 가): 정해지면 GROWTH_LEVEL_M에 늘리기만 하면 됨."""

    level_m: tuple[int, ...] = (3000, 10000, 25000, 50000, 100000)  # 이 경험치(누적 거리 m)를 넘을 때마다 레벨 +1
    outfit_stages: int = 6
    weekly_min_s: int = 300
    cycle_days: int = 7


def load_growth(env: Mapping[str, str]) -> GrowthPolicy:
    raw = env.get("GROWTH_LEVEL_M", "").strip()
    if not raw:
        return GrowthPolicy()
    parts = [p.strip() for p in raw.split(",")]
    if not all(re.fullmatch(r"[0-9]+", p) for p in parts):
        raise ConfigError(f"GROWTH_LEVEL_M은 쉼표로 나눈 정수(m)여야 합니다(지금: {raw!r}).")
    values = tuple(int(p) for p in parts)
    if values[0] <= 0 or any(b <= a for a, b in zip(values, values[1:])):
        raise ConfigError(f"GROWTH_LEVEL_M은 0보다 크고 점점 커져야 합니다(지금: {raw!r}).")
    return GrowthPolicy(level_m=values)


BOT_MIN_KMH, BOT_MAX_KMH = 3.0, 20.0
BOT_MAX_FILL = 20  # 맵 최대 인원(ADR-0010)


def load_bots(env: Mapping[str, str]) -> BotPolicy:
    values = {}
    raw = env.get("BOT_FILL_TO", "").strip()
    if raw:
        if not re.fullmatch(r"[0-9]+", raw) or int(raw) > BOT_MAX_FILL:
            raise ConfigError(f"BOT_FILL_TO는 0에서 {BOT_MAX_FILL} 사이 정수여야 합니다(지금: {raw!r}).")
        values["fill_to"] = int(raw)
    raw = env.get("BOT_PACES_KMH", "").strip()
    if raw:
        try:
            paces = tuple(float(p) for p in raw.split(","))
        except ValueError:
            raise ConfigError(f"BOT_PACES_KMH는 쉼표로 나눈 숫자(km/h)여야 합니다(지금: {raw!r}).") from None
        if not all(BOT_MIN_KMH <= p <= BOT_MAX_KMH for p in paces):  # NaN도 여기서 걸림
            raise ConfigError(f"BOT_PACES_KMH의 값은 {BOT_MIN_KMH:g}에서 {BOT_MAX_KMH:g} km/h 사이여야 합니다(지금: {raw!r}).")
        values["paces_kmh"] = paces
    return BotPolicy(**values)


@dataclass(frozen=True)
class Settings:
    database_url: str
    jwt_secret: str
    kakao_mode: str
    kakao_app_id: Optional[int]
    kakao_admin_key: Optional[str] = None  # 연결 끊기·연결 끊기 웹훅(계획 0058). 없으면 계정 삭제가 503
    access_token_ttl_s: int = 3600
    signup_token_ttl_s: int = 1800
    realtime_token_ttl_s: int = 6 * 3600  # 맵 하나에 붙는 실시간 토큰(계획 0029)
    refresh_token_ttl_s: int = 30 * 24 * 3600
    refresh_reuse_grace_s: int = 30  # 교체 직후 같은 refresh 토큰 재시도 허용(응답 유실)
    max_sessions_per_user: int = 5  # 살아 있는 로그인(기기) 수 상한
    ocr_engine: str = "none"
    request_log: bool = True  # 요청마다 처리 시간 한 줄(계획 0070, app/core/request_log.py). REQUEST_LOG=0이면 끔
    admission: AdmissionPolicy = AdmissionPolicy()
    verification: VerificationPolicy = VerificationPolicy()
    bots: BotPolicy = BotPolicy()
    friends: FriendPolicy = FriendPolicy()
    growth: GrowthPolicy = GrowthPolicy()


SWITCH_VALUES = {"1": True, "0": False}


def _load_switch(env: Mapping[str, str], name: str, default: bool) -> bool:
    raw = env.get(name, "").strip()
    if not raw:
        return default
    if raw not in SWITCH_VALUES:
        raise ConfigError(f"{name}는 1 또는 0이어야 합니다(지금: {raw!r}).")
    return SWITCH_VALUES[raw]


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
        kakao_admin_key=env.get("KAKAO_ADMIN_KEY", "").strip() or None,
        ocr_engine=ocr_engine,
        request_log=_load_switch(env, "REQUEST_LOG", default=True),
        admission=_load_admission(env),
        verification=load_verification(env),
        bots=load_bots(env),
        friends=load_friends(env),
        growth=load_growth(env),
    )


load_env_file()
settings = load_settings(os.environ)
if settings.kakao_mode == "fake":
    log.warning("KAKAO_MODE=fake: 아무 `k_숫자` 토큰으로 로그인됩니다. 로컬·테스트 전용, 운영 서버에서 쓰면 안 됩니다.")
if settings.kakao_mode == "real" and not settings.kakao_admin_key:
    log.warning("KAKAO_ADMIN_KEY가 없습니다: 계정 삭제(카카오 연결 끊기)와 연결 끊기 웹훅이 503입니다. "
                "카카오 개발자 > 앱 키 > 어드민 키를 .env에 넣으세요.")
if settings.ocr_engine == "fake":
    log.warning("OCR_ENGINE=fake: 사진 파일 이름으로 계기판 값을 정합니다. 로컬·테스트 전용, 운영 서버에서 쓰면 안 됩니다.")
