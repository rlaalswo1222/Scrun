from datetime import datetime
from enum import Enum
from typing import Optional

from pydantic import Field

from ..core.model import CamelModel
from .common import RunStats, Verification


class GoalType(str, Enum):
    DISTANCE = "DISTANCE"
    TIME = "TIME"
    NONE = "NONE"


class Goal(CamelModel):
    type: GoalType
    value: Optional[int] = Field(default=None, description="DISTANCE면 m, TIME이면 초")


class RunStartRequest(CamelModel):
    client_run_id: str = Field(min_length=1, description="앱이 만든 ID. 같은 값이 다시 오면 처음 결과를 돌려줌")
    map_instance_id: str
    goal: Goal
    ghost_run_id: Optional[str] = Field(default=None, description="고스트로 쓸 내 기록")
    started_at: datetime


class RunStarted(CamelModel):
    run_id: str
    client_run_id: str
    started_at: datetime


class SpeedSeries(CamelModel):
    interval_s: float = Field(gt=0, description="칸 간격(초)")
    speeds_kmh: list[float]


class SamplesUpload(SpeedSeries):
    offset_s: float = Field(default=0, ge=0, description="이 묶음의 시작 시각(러닝 시작부터 초). 나눠 올릴 때")


class RunFinishRequest(CamelModel):
    ended_at: datetime
    sensor: RunStats = Field(description="센서로 잰 거리·시간")


class RunSummary(CamelModel):
    run_id: str
    started_at: datetime
    course_id: str
    course_name: str
    distance_m: int = Field(description="공식 값: 인증 완료면 계기판, 미인증이면 센서")
    duration_s: int
    avg_pace_s_per_km: Optional[int] = None
    verification: Verification
    shared: bool = Field(description="친구 피드 공유 여부")


class CompanionKind(str, Enum):
    RUNNER = "RUNNER"
    FRIEND = "FRIEND"
    BOT = "BOT"


class Companion(CamelModel):
    nickname: str
    kind: CompanionKind


class OfficialSource(str, Enum):
    CONSOLE = "CONSOLE"
    SENSOR = "SENSOR"


class RunDetail(RunSummary):
    official_source: OfficialSource
    sensor: RunStats
    console: Optional[RunStats] = Field(default=None, description="계기판 값(인증 완료일 때)")
    speed_series: SpeedSeries = Field(description="그래프용으로 줄인 시계열")
    companions: list[Companion]
    ghost_run_id: Optional[str] = None


class RunUpdate(CamelModel):
    shared: bool = Field(description="공유만 바꿀 수 있음. 기록 값 수정은 없음(PRD)")


class ActiveRun(CamelModel):
    run: Optional[RunSummary] = Field(default=None, description="끝나지 않은 러닝. 없으면 null")
    recoverable_until: Optional[datetime] = Field(default=None, description="앱 종료 후 24시간")


class GhostTrack(CamelModel):
    run_id: str
    distance_m: int
    duration_s: int
    speed_series: SpeedSeries
