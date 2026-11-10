"""코스별 지금 맵에 있는 사람 수(계획 0021). 세는 곳은 count_present 하나뿐이다.

실시간 서버(BL-25, ADR-0010 WebSocket)가 생기면 그 연결 수로 바꾼다(docs/api/README.md "코스와 맵 인원").
그 전까지(임시): 최근 FRESH 안에 맵에 입장한 사람(course_entries) + 최근 FRESH 안에 갱신된 달리는 중 기록(runs ACTIVE)을
사람 단위로 센다. 입장만 하고 아직 달리기 전인 사람도 자리를 차지하고, 앱이 꺼져 남은 기록·입장은 FRESH가 지나면 빠진다.
"""
from collections.abc import Iterable
from datetime import timedelta
from typing import Optional

from sqlalchemy import func, select, union
from sqlalchemy.orm import Session

from ..db.models import CourseEntry, Run

FRESH = timedelta(minutes=10)  # 임시 값(실시간 서버 전까지)


def count_present(db: Session, course_ids: Iterable[str], exclude_user_id: Optional[str] = None) -> dict[str, int]:
    """코스 ID → 그 맵에 지금 있는 사람 수(한 사람은 한 번만). exclude_user_id는 빼고 셈(입장 검사에서 나 빼기)."""
    ids = list(course_ids)
    since = func.now() - FRESH
    present = union(
        select(CourseEntry.course_id, CourseEntry.user_id)
        .where(CourseEntry.course_id.in_(ids), CourseEntry.entered_at >= since),
        select(Run.course_id, Run.user_id)
        .where(Run.course_id.in_(ids), Run.status == "ACTIVE", Run.updated_at >= since),
    ).subquery()
    q = select(present.c.course_id, func.count()).group_by(present.c.course_id)
    if exclude_user_id is not None:
        q = q.where(present.c.user_id != exclude_user_id)
    counts = dict(db.execute(q).all())
    return {cid: counts.get(cid, 0) for cid in ids}
