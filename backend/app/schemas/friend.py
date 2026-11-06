from datetime import datetime
from typing import Optional

from pydantic import Field

from ..core.model import CamelModel
from .common import Verification


class FriendAddRequest(CamelModel):
    friend_code: str = Field(min_length=1)


class Friend(CamelModel):
    user_id: str
    nickname: str
    level: int
    verified_count: int
    running_now: bool


class FriendRun(CamelModel):
    run_id: str
    started_at: datetime
    distance_m: int
    duration_s: int
    avg_pace_s_per_km: Optional[int] = None


class FriendProfile(Friend):
    outfit_stage: int
    recent_verified_runs: list[FriendRun]


class FeedItem(CamelModel):
    feed_id: str
    user_id: str
    nickname: str
    run_id: str
    started_at: datetime
    distance_m: int
    duration_s: int
    avg_pace_s_per_km: Optional[int] = None
    verification: Verification = Field(description="미인증이면 앱이 '미인증' 마크")


class RunningFriend(CamelModel):
    user_id: str
    nickname: str
    course_id: str
    course_name: str
    distance_m: int = Field(description="지금까지 달린 거리")
    pace_s_per_km: Optional[int] = None
