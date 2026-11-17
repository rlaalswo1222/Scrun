"""verification confirmed

계기판 읽은 값 확인(계획 0045): 사용자가 읽은 값을 "맞아요"(true)·"틀렸어요"(false)로 답한 것.
null은 아직 안 답했거나 확인 없는 시도(못 읽음·건너뛰기·이 칸 전 기록). 틀렸어요는 실제 오독률을 보는 데 쓴다.

Revision ID: c4a9d2e7f1b3
Revises: b8e1f0c3d5a7
Create Date: 2026-11-18 10:00:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = 'c4a9d2e7f1b3'
down_revision: Union[str, Sequence[str], None] = 'b8e1f0c3d5a7'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column("verifications", sa.Column("confirmed", sa.Boolean(), nullable=True))


def downgrade() -> None:
    op.drop_column("verifications", "confirmed")
