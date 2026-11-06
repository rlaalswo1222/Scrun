from typing import Optional

from pydantic import Field

from ..core.model import CamelModel


class Impact(CamelModel):
    impact_id: str
    name: str
    unlock_at_count: int = Field(description="이 인증 횟수에 자동 해금")
    unlocked: bool


class Progress(CamelModel):
    level: int
    xp_m: int = Field(description="경험치 = 인증 완료 기록의 누적 거리(m). 줄지 않음")
    next_level_at_m: Optional[int] = Field(default=None, description="다음 레벨 경험치. 임계값은 서버 설정")
    next_level_remaining_m: Optional[int] = None
    outfit_stage: int = Field(ge=1, le=6, description="러닝복 세트 단계(Lv.6에서 완성, 이후는 배지 프레임)")
    verified_count: int = Field(description="인증 완료 횟수(인증 마크)")
    impacts: list[Impact]
    selected_impact_id: Optional[str] = None


class ImpactSelect(CamelModel):
    impact_id: Optional[str] = Field(description="해금한 임팩트만. null이면 끄기")


class WeeklyGoalReflect(CamelModel):
    counted: bool = Field(description="이번 운동이 주간 목표 1회로 인정됐는지(인증 완료 + 5분 이상)")
    done_count: int
    target_count: Optional[int] = None


class Rewards(CamelModel):
    """결과 화면(E-11) 연출용. 미인증이면 경험치·횟수 변화 없음."""

    xp_before_m: int
    xp_after_m: int
    level_before: int
    level_after: int
    leveled_up: bool
    outfit_stage_after: int
    next_level_remaining_m: Optional[int] = None
    impacts_unlocked: list[Impact]
    weekly_goal: WeeklyGoalReflect
