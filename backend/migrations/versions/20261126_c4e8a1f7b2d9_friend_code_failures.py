"""friend code failures

틀린 친구 코드 시도를 DB에 센다(계획 0073). 일반 API를 프로세스 여러 개로 띄우면 메모리로 센 횟수(계획 0044)가 프로세스마다
따로라 한도가 N배 느슨해짐. 옛 메모리 기록은 옮기지 않음(서버를 다시 켜면 비던 것과 같음).

Revision ID: c4e8a1f7b2d9
Revises: a7e2c9d4b6f1
Create Date: 2026-11-26 03:00:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = 'c4e8a1f7b2d9'
down_revision: Union[str, Sequence[str], None] = 'a7e2c9d4b6f1'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        "friend_code_failures",
        sa.Column("id", sa.Text(), nullable=False),
        sa.Column("user_id", sa.Text(), nullable=False),
        sa.Column("at", sa.DateTime(timezone=True), nullable=False),
        sa.ForeignKeyConstraint(["user_id"], ["users.id"], ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index("ix_friend_code_failures_user_at", "friend_code_failures", ["user_id", "at"])


def downgrade() -> None:
    op.drop_index("ix_friend_code_failures_user_at", table_name="friend_code_failures")
    op.drop_table("friend_code_failures")
