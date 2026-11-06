from datetime import datetime
from enum import Enum
from typing import Any

from pydantic import Field

from ..core.model import CamelModel


class EventName(str, Enum):
    """PRD 2.4 검증 지표용. 목록은 상준과 확정(초안)."""

    APP_OPEN = "APP_OPEN"  # 재방문율
    RUN_GOAL_REACHED = "RUN_GOAL_REACHED"  # 목표 완주율
    RUN_ENDED_EARLY = "RUN_ENDED_EARLY"
    VERIFICATION_ATTEMPT = "VERIFICATION_ATTEMPT"  # 인증 성공률
    BOREDOM_SURVEY = "BOREDOM_SURVEY"  # 운동 직후 지루함(5점 척도)


class Event(CamelModel):
    name: EventName
    occurred_at: datetime
    props: dict[str, Any] = Field(default_factory=dict)


class EventBatch(CamelModel):
    events: list[Event] = Field(min_length=1, max_length=100)
