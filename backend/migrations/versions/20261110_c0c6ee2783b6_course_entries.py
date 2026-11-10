"""course entries, course thumbnail nullable

맵 입장 = 자리 잡기(course_entries, 사람당 한 줄). 입장 API가 코스 줄을 잠그고 인원을 센 뒤 여기에 써서,
동시에 들어와도 최대 인원을 넘지 않게 한다. 코스 썸네일은 없으면 null(앱 기본 그림). 계획 0021.

Revision ID: c0c6ee2783b6
Revises: 3f1c9a7e52d4
Create Date: 2026-11-10 12:00:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = 'c0c6ee2783b6'
down_revision: Union[str, Sequence[str], None] = '3f1c9a7e52d4'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        'course_entries',
        sa.Column('user_id', sa.Text(), nullable=False),
        sa.Column('course_id', sa.Text(), nullable=False),
        sa.Column('entered_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
        sa.ForeignKeyConstraint(['course_id'], ['courses.id']),
        sa.ForeignKeyConstraint(['user_id'], ['users.id'], ondelete='CASCADE'),
        sa.PrimaryKeyConstraint('user_id'),
    )
    op.create_index(op.f('ix_course_entries_course_id'), 'course_entries', ['course_id'], unique=False)
    op.alter_column('courses', 'thumbnail_url', nullable=True, server_default=None)
    op.execute("UPDATE courses SET thumbnail_url = NULL WHERE thumbnail_url = ''")


def downgrade() -> None:
    op.execute("UPDATE courses SET thumbnail_url = '' WHERE thumbnail_url IS NULL")
    op.alter_column('courses', 'thumbnail_url', nullable=False, server_default='')
    op.drop_index(op.f('ix_course_entries_course_id'), table_name='course_entries')
    op.drop_table('course_entries')
