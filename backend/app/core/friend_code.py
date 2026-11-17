"""친구 코드(계획 0044 정할 것 1, BL-27). 8글자, 헷갈리는 0·O·1·I를 뺀 32자 → 32^8 ≈ 1조 개.

코드 = 내 피드를 볼 수 있는 열쇠라 맞혀 보기 어렵게 길게 두고, 틀린 코드 입력은 core/attempts가 막는다.
입력은 대소문자·공백·하이픈을 무시한다. 공유 문구를 통째로 붙여 넣으면 앱(FriendText.NormalizeCode)이 코드만 골라 보낸다.
"""
import re
import secrets
from typing import Optional

from sqlalchemy import select
from sqlalchemy.orm import Session

from ..db.models import User
from .errors import ApiError, ErrorCode

ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"
LENGTH = 8
_IGNORED = re.compile(r"[\s\-]")
_VALID = re.compile(f"[{ALPHABET}]{{{LENGTH}}}")


def random_code() -> str:
    return "".join(secrets.choice(ALPHABET) for _ in range(LENGTH))


def new_code(db: Session) -> str:
    """아직 아무도 안 쓰는 코드. 겹칠 확률은 사용자 수 / 1조라 사실상 한 번에 끝난다."""
    for _ in range(10):
        code = random_code()
        if db.scalar(select(User.id).where(User.friend_code == code)) is None:
            return code
    raise ApiError(ErrorCode.INTERNAL_ERROR, "친구 코드를 만들지 못했습니다.")


def normalize(raw: str) -> Optional[str]:
    """입력 → 코드. 대문자로, 공백·하이픈 뺌. 코드 모양이 아니면 None(= 없는 코드)."""
    code = _IGNORED.sub("", raw).upper()
    return code if _VALID.fullmatch(code) else None
