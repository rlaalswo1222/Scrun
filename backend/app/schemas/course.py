from enum import Enum
from typing import Optional

from pydantic import Field

from ..core.model import CamelModel


class CourseType(str, Enum):
    LOOP_3D = "LOOP_3D"
    VIDEO = "VIDEO"  # 실사 영상 코스(우선순위 최하위). 자리만


class Course(CamelModel):
    course_id: str
    name: str
    type: CourseType
    length_m: int = Field(description="한 바퀴(루프) 또는 전체(영상) 길이")
    runners_now: int = Field(description="지금 달리는 러너 수(봇 제외)")
    thumbnail_url: Optional[str] = Field(description="없으면 null(앱 기본 그림)")


class CourseDetail(Course):
    description: str
    mood: str = Field(description="분위기 한 줄")
    max_runners_per_map: int = Field(description="맵 인스턴스당 최대 인원(20)")


class RealtimeAccess(CamelModel):
    """실시간 접속 정보. 모양은 BL-25(FastAPI WebSocket vs Photon) 뒤에 확정(미정)."""

    url: str
    token: str


class MapEntry(CamelModel):
    map_instance_id: str
    course_id: str
    entry_position_m: float = Field(description="입장 위치(코스 시작점부터 m). 실시간 서버(BL-25) 전에는 코스 위 랜덤, 뒤에 러너 많은 구간 근처로")
    realtime: Optional[RealtimeAccess] = None
