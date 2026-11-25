from enum import Enum
from typing import Optional

from pydantic import Field

from ..core.model import CamelModel, UtcDatetime
from .common import RunStats
from .progress import Rewards


class VerificationStatus(str, Enum):
    VERIFIED = "VERIFIED"
    UNVERIFIED = "UNVERIFIED"
    RETAKE_REQUIRED = "RETAKE_REQUIRED"  # 진행 중 상태, 저장되지 않음. reason: UNSUPPORTED_CONSOLE·USER_REJECTED·null(못 읽음)
    CONFIRM_REQUIRED = "CONFIRM_REQUIRED"  # 진행 중: 읽은 값을 사용자가 확인(계획 0045, E-05b). 판정 전이라 센서 대조는 안 보냄


class UnverifiedReason(str, Enum):
    MISMATCH = "MISMATCH"  # 계기판과 센서 차이가 허용 오차 초과
    UNSUPPORTED_CONSOLE = "UNSUPPORTED_CONSOLE"  # OCR 불가 계기판(엔진이 판단했거나 다시 찍기를 다 써도 못 읽음)
    SKIPPED = "SKIPPED"  # 촬영 없이 마무리
    TIMEOUT = "TIMEOUT"  # 촬영 제한 시간 초과
    RETAKE_GIVEN_UP = "RETAKE_GIVEN_UP"
    USER_REJECTED = "USER_REJECTED"  # 다시 찍기 이유만: 읽은 값이 틀렸다고 함(계획 0045, 다시 찍기 횟수 안 씀)


class ComparisonRow(CamelModel):
    item: str = Field(description="distance 또는 duration")
    console: Optional[float] = None
    sensor: float
    diff_ratio: Optional[float] = Field(default=None, description="(계기판 − 센서) / 계기판")
    ok: bool = Field(description="허용 범위 안인지. 시간은 일시정지가 있었으면 위쪽을 쉰 만큼 넓혀 봄")


class VerificationResult(CamelModel):
    status: VerificationStatus
    reason: Optional[UnverifiedReason] = Field(default=None, description="UNVERIFIED면 미인증 사유, RETAKE_REQUIRED면 다시 찍으라는 이유(UNSUPPORTED_CONSOLE·USER_REJECTED, null은 못 읽음)")
    attempt_id: Optional[str] = Field(default=None, description="CONFIRM_REQUIRED일 때 확인할 시도. POST …/verification/confirm에 그대로")
    console: Optional[RunStats] = Field(default=None, description="계기판에서 읽은 값(CONFIRM_REQUIRED면 확인할 값)")
    sensor: RunStats
    comparison: list[ComparisonRow] = Field(description="센서 대조 표. 판정 전(CONFIRM_REQUIRED)·못 읽음이면 빈 목록")
    retake_deadline: Optional[UtcDatetime] = Field(default=None, description="재촬영 필요·값 확인일 때 마감 시각")
    retakes_left: Optional[int] = Field(default=None, description="재촬영 필요·값 확인일 때 남은 다시 찍기 횟수(이만큼 더 못 읽으면 UNVERIFIED. 틀렸어요는 안 씀)")
    rewards: Optional[Rewards] = Field(default=None, description="정해졌을 때(인증 완료·미인증) 성장 반영 결과(계획 0048, GET /runs/{runId}/rewards와 같음). 정하기 전(값 확인·다시 찍기)은 null")


class SkipReason(str, Enum):
    SKIPPED = "SKIPPED"
    TIMEOUT = "TIMEOUT"
    RETAKE_GIVEN_UP = "RETAKE_GIVEN_UP"


class SkipRequest(CamelModel):
    reason: SkipReason


class ConfirmRequest(CamelModel):
    attempt_id: str = Field(min_length=1, max_length=64, description="사진 응답(CONFIRM_REQUIRED)의 attemptId")
    correct: bool = Field(description="true: 맞아요(지금 판정) / false: 틀렸어요(다시 찍기, 횟수 안 씀)")
