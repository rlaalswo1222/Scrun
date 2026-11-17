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
    RETAKE_REQUIRED = "RETAKE_REQUIRED"  # 진행 중 상태, 저장되지 않음. reason: MISMATCH·UNSUPPORTED_CONSOLE·null(못 읽음)


class UnverifiedReason(str, Enum):
    MISMATCH = "MISMATCH"  # 계기판과 센서 차이가 허용 오차 초과
    UNSUPPORTED_CONSOLE = "UNSUPPORTED_CONSOLE"  # OCR 불가 계기판(엔진이 판단했거나 다시 찍기를 다 써도 못 읽음)
    SKIPPED = "SKIPPED"  # 촬영 없이 마무리
    TIMEOUT = "TIMEOUT"  # 촬영 제한 시간 초과
    RETAKE_GIVEN_UP = "RETAKE_GIVEN_UP"


class ComparisonRow(CamelModel):
    item: str = Field(description="distance 또는 duration")
    console: Optional[float] = None
    sensor: float
    diff_ratio: Optional[float] = Field(default=None, description="(계기판 − 센서) / 계기판")
    ok: bool = Field(description="허용 범위 안인지. 시간은 일시정지가 있었으면 위쪽을 쉰 만큼 넓혀 봄")


class VerificationResult(CamelModel):
    status: VerificationStatus
    reason: Optional[UnverifiedReason] = Field(default=None, description="UNVERIFIED면 미인증 사유, RETAKE_REQUIRED면 다시 찍으라는 이유(MISMATCH·UNSUPPORTED_CONSOLE, null은 못 읽음)")
    console: Optional[RunStats] = Field(default=None, description="계기판에서 읽은 값")
    sensor: RunStats
    comparison: list[ComparisonRow]
    retake_deadline: Optional[datetime] = Field(default=None, description="재촬영 필요일 때 마감 시각")
    retakes_left: Optional[int] = Field(default=None, description="재촬영 필요일 때 남은 다시 찍기 횟수(1 이상, 이만큼 더 못 읽으면 UNVERIFIED)")
    rewards: Optional[Rewards] = Field(default=None, description="저장됐을 때(인증 완료·미인증) 반영 결과. 성장 API(BL-32, S8) 전까지 null")


class SkipReason(str, Enum):
    SKIPPED = "SKIPPED"
    TIMEOUT = "TIMEOUT"
    RETAKE_GIVEN_UP = "RETAKE_GIVEN_UP"


class SkipRequest(CamelModel):
    reason: SkipReason
