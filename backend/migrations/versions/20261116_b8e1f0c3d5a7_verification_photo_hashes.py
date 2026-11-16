"""verification photo hashes

계기판 인증(계획 0038): 올린 사진의 SHA-256만 남겨 같은 사진을 다른 기록에 다시 쓰는 것을 막는다.
사진 자체는 저장하지 않는다(BL-17 ③). 어느 기록에 쓰였는지 찾으려고 GIN 색인.

Revision ID: b8e1f0c3d5a7
Revises: 7a1d4e2b9c30
Create Date: 2026-11-16 22:00:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = 'b8e1f0c3d5a7'
down_revision: Union[str, Sequence[str], None] = '7a1d4e2b9c30'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column("verifications", sa.Column("photo_hashes", postgresql.ARRAY(sa.Text()), server_default=sa.text("'{}'"),
                                             nullable=False))
    op.create_index("ix_verifications_photo_hashes", "verifications", ["photo_hashes"], postgresql_using="gin")


def downgrade() -> None:
    op.drop_index("ix_verifications_photo_hashes", table_name="verifications")
    op.drop_column("verifications", "photo_hashes")
