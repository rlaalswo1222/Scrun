from datetime import date, datetime
from enum import Enum
from typing import Any, Optional

from pydantic import Field

from ..core.model import CamelModel


class Me(CamelModel):
    user_id: str
    nickname: str
    height_cm: int
    weight_kg: int
    friend_code: str = Field(description="친구 추가용 코드")
    timezone: str
    created_at: datetime


class MeUpdate(CamelModel):
    """보낸 칸만 바뀜."""

    nickname: Optional[str] = Field(default=None, min_length=2, max_length=12)
    height_cm: Optional[int] = Field(default=None, ge=100, le=230)
    weight_kg: Optional[int] = Field(default=None, ge=20, le=250)
    timezone: Optional[str] = None


class NicknameReason(str, Enum):
    DUPLICATED = "DUPLICATED"
    NOT_ALLOWED = "NOT_ALLOWED"


class NicknameCheck(CamelModel):
    nickname: str
    available: bool
    reason: Optional[NicknameReason] = Field(default=None, description="쓸 수 없을 때 이유")


class Settings(CamelModel):
    feed_share_default: bool = Field(description="새 기록을 피드에 공유할지 기본값(PRD: 켜짐)")


class SettingsUpdate(CamelModel):
    feed_share_default: Optional[bool] = None


class Gait(str, Enum):
    WALK = "WALK"
    RUN = "RUN"


class CalibrationPoint(CamelModel):
    speed_kmh: float = Field(gt=0, le=25, description="안내한 러닝머신 속도")
    gait: Gait
    duration_s: int = Field(gt=0)


class CalibrationUpload(CamelModel):
    points: list[CalibrationPoint] = Field(min_length=1, description="보정한 구간들")
    model: dict[str, Any] = Field(
        description="폰 SpeedEstimator가 만든 보정값(계수 등). 서버는 해석하지 않고 보관만. OCR 값은 넣지 않음(독립성 원칙)"
    )
    device: str = Field(description="보정한 폰 기종. 기종이 바뀌면 재보정 안내에 씀")


class Calibration(CalibrationUpload):
    created_at: datetime


class WeeklyGoalCycle(CamelModel):
    cycle_start: date
    target_count: int
    done_count: int


class WeeklyGoal(CamelModel):
    target_count: Optional[int] = Field(default=None, ge=1, le=7, description="목표를 안 정했으면 null")
    cycle_start: Optional[date] = Field(default=None, description="이번 주기 시작일(설정일부터 7일 단위)")
    cycle_end: Optional[date] = None
    done_count: int = Field(description="이번 주기 인정 횟수(인증 완료 + 5분 이상)")
    days_left: Optional[int] = None
    next_target_count: Optional[int] = Field(default=None, description="변경 예약값. 다음 주기부터 적용")
    history: list[WeeklyGoalCycle] = Field(description="최근 8주기")


class WeeklyGoalUpdate(CamelModel):
    target_count: int = Field(ge=1, le=7)
