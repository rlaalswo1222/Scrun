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
    runners_now: int = Field(description="그 맵에 지금 있는 사람 수(봇 제외). 실시간 서버 전까지 임시: 최근 10분 안에 입장했거나 달리는 중인 사람")
    thumbnail_url: Optional[str] = Field(description="없으면 null(앱 기본 그림)")


class CourseDetail(Course):
    description: str
    mood: str = Field(description="분위기 한 줄")
    max_runners_per_map: int = Field(description="맵 인스턴스당 최대 인원(20)")


class RealtimeAccess(CamelModel):
    """그 맵의 실시간 방 접속 정보(ADR-0010 FastAPI WebSocket, 메시지 모양은 docs/api/realtime.md)."""

    url: str = Field(description="WebSocket 주소(wss://…/v1/realtime). 토큰은 Authorization: Bearer 헤더로")
    token: str = Field(description="이 맵에만 붙는 토큰(6시간). 만료되면 다시 입장")


class MapEntry(CamelModel):
    map_instance_id: str
    course_id: str
    entry_position_m: float = Field(description="입장 위치(코스 시작점부터 m). 그 맵에 달리는 사람이 있으면 한 명의 20–40m 뒤, 없으면 랜덤")
    realtime: Optional[RealtimeAccess] = Field(None, description="실시간 방 접속 정보. 진짜 서버는 늘 줌(스텁 예시에도 있음)")
