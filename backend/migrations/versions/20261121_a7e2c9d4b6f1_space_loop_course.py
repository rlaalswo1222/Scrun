"""space loop course

우주 루프 맵(Unity SpaceLoop 장면, ADR-0013·계획 0063) 코스 하나. 앱은 crs_space_loop이면 SpaceLoop 장면을 연다(계획 0066).
길이는 장면 SplineTrack 제어점 300개를 Unity Polyline(centripetal Catmull-Rom, 0.5m 간격)과 같은 계산으로 잰 한 바퀴
2999.9997m → 3000(중심선 파일 lapLength 3000과 같음). 섬 코스 4개는 ADR-0013 승인 전까지 그대로 둔다(승인되면 active=false).
이름·설명·분위기는 임시(우주 시안 구간 이름 기준) → 상준 확인.

Revision ID: a7e2c9d4b6f1
Revises: d5b8e3f2a6c1
Create Date: 2026-11-21 18:00:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = 'a7e2c9d4b6f1'
down_revision: Union[str, Sequence[str], None] = 'd5b8e3f2a6c1'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


COURSE = {
    "id": "crs_space_loop", "name": "우주 루프", "type": "LOOP_3D", "length_m": 3000,
    "description": "구름 위 기지에서 출발해 화성 협곡, 밤 숲 호수, 얼음 위성, 달 기지를 지나 돌아오는 3km 코스.",
    "mood": "다섯 행성을 잇는 길",
}


def upgrade() -> None:
    table = sa.table(
        "courses",
        sa.column("id", sa.Text), sa.column("name", sa.Text), sa.column("type", sa.Text),
        sa.column("length_m", sa.Integer), sa.column("description", sa.Text), sa.column("mood", sa.Text),
    )
    op.bulk_insert(table, [COURSE])


def downgrade() -> None:
    # 입장 기록은 지우고 내린다. 이 코스로 달린 기록(runs)이 있으면 외래 키로 실패한다(기록을 지우지 않으려고 일부러, seed_courses와 같음)
    op.execute("DELETE FROM course_entries WHERE course_id = 'crs_space_loop'")
    op.execute("DELETE FROM courses WHERE id = 'crs_space_loop'")
