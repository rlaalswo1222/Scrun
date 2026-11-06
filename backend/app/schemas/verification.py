from datetime import datetime
from enum import Enum
from typing import Optional

from pydantic import Field

from ..core.model import CamelModel
from .common import RunStats
from .progress import Rewards


class VerificationStatus(str, Enum):
    VERIFIED = "VERIFIED"
    UNVERIFIED = "UNVERIFIED"
    RETAKE_REQUIRED = "RETAKE_REQUIRED"  # 진행 중 상태, 저장되지 않음


class UnverifiedReason(str, Enum):
    MISMATCH = "MISMATCH"  # 계기판과 센서 차이가 허용 오차 초과
    UNSUPPORTED_CONSOLE = "UNSUPPORTED_CONSOLE"  # OCR 불가 계기판
    SKIPPED = "SKIPPED"  # 촬영 없이 마무리
    TIMEOUT = "TIMEOUT"  # 촬영 제한 시간 초과
    RETAKE_GIVEN_UP = "RETAKE_GIVEN_UP"


class ComparisonRow(CamelModel):
    item: str = Field(description="distance 또는 duration")
    console: Optional[float] = None
    sensor: float
    diff_ratio: Optional[float] = Field(default=None, description="(계기판 − 센서) / 계기판")
    ok: bool


class VerificationResult(CamelModel):
    status: VerificationStatus
    reason: Optional[UnverifiedReason] = None
    console: Optional[RunStats] = Field(default=None, description="계기판에서 읽은 값")
    sensor: RunStats
    comparison: list[ComparisonRow]
    retake_deadline: Optional[datetime] = Field(default=None, description="재촬영 필요일 때 마감 시각")
    rewards: Optional[Rewards] = Field(default=None, description="저장됐을 때(인증 완료·미인증) 반영 결과")


class ConsoleReading(CamelModel):
    """OCR을 앱에서 할 때의 대안(미정)."""

    distance_m: int = Field(ge=0)
    duration_s: int = Field(ge=0)
    read_at: datetime


class SkipReason(str, Enum):
    SKIPPED = "SKIPPED"
    TIMEOUT = "TIMEOUT"
    RETAKE_GIVEN_UP = "RETAKE_GIVEN_UP"


class SkipRequest(CamelModel):
    reason: SkipReason
