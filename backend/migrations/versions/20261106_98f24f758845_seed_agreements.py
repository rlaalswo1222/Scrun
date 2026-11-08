"""seed agreements

Revision ID: 98f24f758845
Revises: 8d34e8b64e8a
Create Date: 2026-11-06 17:58:45.228876

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '98f24f758845'
down_revision: Union[str, Sequence[str], None] = '8d34e8b64e8a'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


# 약관 목록(운영 데이터). 본문 주소는 개인정보처리방침·이용약관을 만들면(BL-49) 새 마이그레이션으로 바꾼다
AGREEMENTS = [
    {"id": "terms", "title": "서비스 이용약관", "required": True, "version": "2026-11-06", "url": "https://example.com/scrun/terms", "sort": 1},
    {"id": "privacy", "title": "개인정보 수집·이용", "required": True, "version": "2026-11-06", "url": "https://example.com/scrun/privacy", "sort": 2},
    {"id": "notice", "title": "운동 알림 받기", "required": False, "version": "2026-11-06", "url": "https://example.com/scrun/notice", "sort": 3},
]


def upgrade() -> None:
    table = sa.table(
        "agreements",
        sa.column("id", sa.Text), sa.column("title", sa.Text), sa.column("required", sa.Boolean),
        sa.column("version", sa.Text), sa.column("url", sa.Text), sa.column("sort", sa.SmallInteger),
    )
    op.bulk_insert(table, AGREEMENTS)


def downgrade() -> None:
    op.execute("DELETE FROM agreements WHERE id IN ('terms', 'privacy', 'notice')")
