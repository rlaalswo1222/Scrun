"""사용자 시간대 검사. 주간 목표 주기(설정일부터 7일)를 이 시간대로 계산하므로 IANA 이름(Asia/Seoul 등)만 받는다(계획 0028).
가입(POST /auth/signup)과 내 정보 수정(PATCH /users/me)이 같은 검사를 쓴다.

ZoneInfo(이름)로 열어 보지 않고 IANA 목록(available_timezones)과 정확히 대조한다:
- 맥(대소문자 무시 파일 시스템)에서는 'asia/seoul'도 열려서 서버(리눅스)와 판정이 달라짐
- 'Asia' 같은 폴더 이름은 열 때 OSError(→ 500)
서버 이미지에 시간대 자료가 없을 수 있어 requirements에 tzdata를 넣었다(목록이 어느 서버에서나 같게).
"""
from functools import lru_cache
from zoneinfo import available_timezones

DEFAULT = "Asia/Seoul"
RULE = "IANA 시간대 이름(예: Asia/Seoul)"
MAX_LEN = 64  # 요청 크기 제한용(가장 긴 IANA 이름도 30자 남짓)
_PLACEHOLDERS = {"Factory"}  # IANA 목록에 있지만 "시간대 모름" 자리표시(-00)


@lru_cache(maxsize=1)
def _known() -> frozenset[str]:
    return frozenset(available_timezones() - _PLACEHOLDERS)


def validate_field(raw: str) -> str:
    """요청 모델(pydantic) 검사용: 앞뒤 공백을 지운 이름. IANA 이름이 아니면 ValueError → 400 VALIDATION_ERROR."""
    name = (raw or "").strip()
    if name not in _known():
        raise ValueError(f"시간대는 {RULE}")
    return name
