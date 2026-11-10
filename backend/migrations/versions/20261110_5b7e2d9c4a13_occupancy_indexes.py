"""occupancy indexes

맵 인원 세기(core/occupancy.count_present)가 코스 목록 요청마다 도는데 그 조건에 맞는 인덱스가 없었다(민재 리뷰, PR #30).
course_entries(course_id, entered_at)는 기존 course_id 하나짜리 인덱스를 대신하고, runs(course_id, status, updated_at)를 더한다.

Revision ID: 5b7e2d9c4a13
Revises: 090d14ed6534
Create Date: 2026-11-10 23:00:00

"""
from typing import Sequence, Union

from alembic import op


# revision identifiers, used by Alembic.
revision: str = '5b7e2d9c4a13'
down_revision: Union[str, Sequence[str], None] = '090d14ed6534'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.drop_index('ix_course_entries_course_id', table_name='course_entries')
    op.create_index('ix_course_entries_course_id_entered_at', 'course_entries', ['course_id', 'entered_at'], unique=False)
    op.create_index('ix_runs_course_id_status_updated_at', 'runs', ['course_id', 'status', 'updated_at'], unique=False)


def downgrade() -> None:
    op.drop_index('ix_runs_course_id_status_updated_at', table_name='runs')
    op.drop_index('ix_course_entries_course_id_entered_at', table_name='course_entries')
    op.create_index('ix_course_entries_course_id', 'course_entries', ['course_id'], unique=False)
