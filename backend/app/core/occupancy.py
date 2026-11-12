"""코스별 지금 맵에 있는 사람 수(계획 0021 → 0029). 세는 곳은 count_present 하나뿐이다.

ADR-0010: 실시간 방에 붙어 있는 사람(끊긴 지 30초 안 포함) + 입장했지만 아직 안 붙은 사람(입장 ENTRY_FRESH 안)을
사람 단위로 센다. 방에 있어도 자리(course_entries)가 다른 코스로 옮겨졌으면 그 코스로 센다(앱이 꺼져 옛 방에 남은 경우).
입장한 사람도 자리를 차지해야 동시에 들어와도 최대 인원이 지켜진다.
"""
from collections.abc import Iterable
from datetime import timedelta
from typing import Optional

from sqlalchemy import func, or_, select
from sqlalchemy.orm import Session

from ..db.models import CourseEntry
from ..realtime.hub import hub

ENTRY_FRESH = timedelta(minutes=2)  # 입장 → 실시간 연결까지 기다려 주는 시간(섬 불러오기·느린 망)


def count_present(db: Session, course_ids: Iterable[str], exclude_user_id: Optional[str] = None) -> dict[str, int]:
    """코스 ID → 그 맵에 지금 있는 사람 수(한 사람은 한 번만). exclude_user_id는 빼고 셈(입장 검사에서 나 빼기)."""
    ids = list(course_ids)
    connected = hub.present_user_ids()  # 어느 방이든: 옛 방에 남은 사람도 자리가 옮겨진 코스로 셈(물어본 코스와 상관없이 같은 답)
    conds = [CourseEntry.entered_at >= func.now() - ENTRY_FRESH]
    if connected:
        conds.append(CourseEntry.user_id.in_(connected))
    q = (select(CourseEntry.course_id, func.count())
         .where(CourseEntry.course_id.in_(ids), or_(*conds))
         .group_by(CourseEntry.course_id))
    if exclude_user_id is not None:
        q = q.where(CourseEntry.user_id != exclude_user_id)
    counts = dict(db.execute(q).all())
    return {cid: counts.get(cid, 0) for cid in ids}
