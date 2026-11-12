"""닉네임 규칙. 다른 러너의 이름표에 보이므로 형식·금칙어·중복을 서버가 한 곳에서 판정한다(가입·내 정보 수정·확인 API 공통).

1. 정규화: NFKC(전각 'ＡＤＭＩＮ' → 'ADMIN', 호환 문자 통일) → 앞뒤 공백 제거
2. 형식: 2에서 12자, 한글 완성형(가-힣)·영문·숫자·`_`만. 공백·이모지·특수문자·보이지 않는 글자(zero-width 등)·
   자모 단독(ㅋㅋ)은 안 됨(NFKC가 호환 자모를 조합용 자모로 바꿔 깨져 보이므로)
3. 금칙어: 대소문자와 `_`를 무시하고 포함 여부 검사(심사 1.2). 목록은 운영하며 늘린다(초안)
4. 중복: 대소문자 무시(DB 인덱스 lower(nickname)과 같게)
"""
import re
import unicodedata
from enum import Enum
from typing import Optional

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from ..db.models import User
from .errors import ApiError, ErrorCode

MIN_LEN, MAX_LEN = 2, 12
RAW_MAX_LEN = 40  # 정규화 전 입력 상한(요청 크기 제한용)
FORMAT_RULE = f"{MIN_LEN}에서 {MAX_LEN}자, 한글·영문·숫자·_만"
_ALLOWED = re.compile(r"[0-9A-Za-z_가-힣]+")
BANNED_WORDS = ("admin", "관리자", "운영자", "스크런", "scrun")


class Problem(str, Enum):
    INVALID = "INVALID"  # 형식(길이·글자)
    NOT_ALLOWED = "NOT_ALLOWED"  # 금칙어
    DUPLICATED = "DUPLICATED"


def normalize(raw: str) -> str:
    return unicodedata.normalize("NFKC", raw).strip()


def format_ok(name: str) -> bool:
    """정규화된 이름 기준."""
    return MIN_LEN <= len(name) <= MAX_LEN and _ALLOWED.fullmatch(name) is not None


def is_allowed(name: str) -> bool:
    skeleton = name.lower().replace("_", "")
    return not any(word in skeleton for word in BANNED_WORDS)


def is_taken(db: Session, name: str, exclude_user_id: Optional[str] = None) -> bool:
    q = select(User.id).where(func.lower(User.nickname) == name.lower())
    if exclude_user_id is not None:
        q = q.where(User.id != exclude_user_id)
    return db.scalar(q.limit(1)) is not None


def check(db: Session, raw: str, exclude_user_id: Optional[str] = None) -> tuple[str, Optional[Problem]]:
    """(정규화된 이름, 문제). 문제가 없으면 None. 형식 → 금칙어 → 중복 순서."""
    name = normalize(raw)
    if not format_ok(name):
        return name, Problem.INVALID
    if not is_allowed(name):
        return name, Problem.NOT_ALLOWED
    if is_taken(db, name, exclude_user_id):
        return name, Problem.DUPLICATED
    return name, None


def validate_field(raw: str) -> str:
    """요청 모델(pydantic) 검사용: 정규화해서 돌려주고, 형식이 틀리면 ValueError → 400 VALIDATION_ERROR."""
    name = normalize(raw)
    if not format_ok(name):
        raise ValueError(f"닉네임은 {FORMAT_RULE}")
    return name


def require_usable(db: Session, raw: str, exclude_user_id: Optional[str] = None) -> str:
    """가입·내 정보 수정의 최종 판정: 쓸 수 있으면 정규화된 이름, 아니면 ApiError
    (형식 400 VALIDATION_ERROR fields, 금칙어 400 NICKNAME_NOT_ALLOWED, 중복 409 NICKNAME_DUPLICATED)."""
    name, problem = check(db, raw, exclude_user_id)
    if problem is Problem.INVALID:
        raise ApiError(ErrorCode.VALIDATION_ERROR, "닉네임 형식을 확인하세요.",
                       {"fields": [{"field": "nickname", "reason": f"닉네임은 {FORMAT_RULE}"}]})
    if problem is Problem.NOT_ALLOWED:
        raise ApiError(ErrorCode.NICKNAME_NOT_ALLOWED)
    if problem is Problem.DUPLICATED:
        raise ApiError(ErrorCode.NICKNAME_DUPLICATED)
    return name
