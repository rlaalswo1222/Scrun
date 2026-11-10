"""seed courses

기존 섬 맵(Island01) 코스 4개(운영 데이터). 원본은 unity/TestRun/Assets/Scrun/Course/IslandLayout.json의 routes.
길이는 Unity RoadNetwork.BuildRoute(교차로 모서리 12m, 높낮이 포함)와 같은 계산으로 잰 한 바퀴 m.
임시 맵이라 새 맵(BL-43 3km 루프)이 나오면 새 마이그레이션으로 바꾼다. 계획 0021.

Revision ID: 090d14ed6534
Revises: c0c6ee2783b6
Create Date: 2026-11-10 12:10:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '090d14ed6534'
down_revision: Union[str, Sequence[str], None] = 'c0c6ee2783b6'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


# 썸네일은 아직 없음(null → 앱 기본 그림)
COURSES = [
    {"id": "crs_lake_loop", "name": "노을 호수 한 바퀴", "type": "LOOP_3D", "length_m": 6671,
     "description": "등대 항구에서 출발해 호숫가 나무 데크 길을 지나 고원 아래 골짜기로 돌아오는 코스.",
     "mood": "노을 지는 호숫가"},
    {"id": "crs_coast_run", "name": "해안 절벽 순환", "type": "LOOP_3D", "length_m": 8776,
     "description": "동쪽 해안 절벽 길과 바위 터널을 지나 풍차가 늘어선 고원 능선을 따라 도는 코스.",
     "mood": "바닷바람 부는 절벽 길"},
    {"id": "crs_forest_run", "name": "거목 협곡 순환", "type": "LOOP_3D", "length_m": 9695,
     "description": "호숫가를 지나 높은 바위벽 사이 협곡 길과 협곡 다리를 건너 고원을 넘어 돌아오는 코스.",
     "mood": "거목 숲과 바위 협곡"},
    {"id": "crs_island_tour", "name": "섬 일주", "type": "LOOP_3D", "length_m": 10884,
     "description": "호수·협곡·풍차 고원·해안 절벽을 모두 지나 섬을 한 바퀴 도는 코스.",
     "mood": "섬의 모든 풍경"},
]


def upgrade() -> None:
    table = sa.table(
        "courses",
        sa.column("id", sa.Text), sa.column("name", sa.Text), sa.column("type", sa.Text),
        sa.column("length_m", sa.Integer), sa.column("description", sa.Text), sa.column("mood", sa.Text),
    )
    op.bulk_insert(table, COURSES)


def downgrade() -> None:
    # 입장 기록은 지우고 내린다. 그 코스로 달린 기록(runs)이 있으면 외래 키로 실패한다(기록을 지우지 않으려고 일부러)
    ids = ", ".join(f"'{c['id']}'" for c in COURSES)
    op.execute(f"DELETE FROM course_entries WHERE course_id IN ({ids})")
    op.execute(f"DELETE FROM courses WHERE id IN ({ids})")
