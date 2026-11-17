from datetime import datetime
from typing import Optional

from pydantic import Field

from ..core.model import CamelModel
from .common import Verification


class FriendAddRequest(CamelModel):
    friend_code: str = Field(min_length=1, max_length=32,
                             description="받은 코드 8글자. 대소문자·공백·하이픈은 무시. 공유 문구에서 코드를 골라내는 건 앱이 함")


class Friend(CamelModel):
    user_id: str
    nickname: str
    level: Optional[int] = Field(default=None, description="레벨(BL-32). 레벨 계산이 생기기 전까지 null")
    verified_count: int = Field(description="인증 마크 수(공유한 인증 완료 러닝 수)")
    running_now: bool
    running_distance_m: Optional[int] = Field(
        default=None, description="달리는 중이면 이번 러닝 거리(m, G-10 '3.2km 달리는 중'). 안 달리거나 옛 앱이면 null")
    last_run_at: Optional[datetime] = Field(
        default=None, description="마지막으로 끝낸 공유 러닝의 종료 시각(G-10 '20분 전 러닝'). 없으면 null")


class FriendRun(CamelModel):
    """친구의 끝난 러닝 한 줄(피드·프로필). 친구가 공유한 기록만."""
    run_id: str
    started_at: datetime
    ended_at: datetime
    course_id: str
    course_name: str
    distance_m: int
    duration_s: int
    avg_pace_s_per_km: Optional[int] = None
    verification: Verification = Field(description="UNVERIFIED면 앱이 '미인증' 마크, PENDING(인증 대기)은 마크 없음")


class FriendProfile(Friend):
    outfit_stage: int
    recent_runs: list[FriendRun] = Field(description="최근 공유 러닝 10개(최신순, 인증 상태 무관)")


class FeedItem(FriendRun):
    feed_id: str = Field(description="피드 줄 ID(지금은 runId와 같음)")
    user_id: str
    nickname: str


class RunningFriend(CamelModel):
    user_id: str
    nickname: str
    course_id: str
    course_name: str
    distance_m: int = Field(description="이번 러닝에서 지금까지 달린 거리(m). 옛 앱이면 0")
    pace_s_per_km: Optional[int] = Field(default=None, description="지금 속도의 1 km 페이스(초). 1 km/h 아래면 null")
