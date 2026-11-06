from enum import Enum
from typing import Generic, Optional, TypeVar

from pydantic import Field

from ..core.model import CamelModel

T = TypeVar("T")


class CursorPage(CamelModel, Generic[T]):
    items: list[T]
    next_cursor: Optional[str] = Field(default=None, description="다음 쪽 커서. 없으면 null")


class Verification(str, Enum):
    """저장되는 인증 상태는 2개(PRD F06). PENDING은 러닝을 끝내고 인증을 아직 안 한 동안만."""

    VERIFIED = "VERIFIED"
    UNVERIFIED = "UNVERIFIED"
    PENDING = "PENDING"


class RunStats(CamelModel):
    distance_m: int = Field(ge=0, description="거리(m)")
    duration_s: int = Field(ge=0, description="시간(초)")
