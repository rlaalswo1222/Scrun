"""age 14 agreement

Figma A-18 약관 동의(계획 0031): 필수 항목 "만 14세 이상입니다"를 서버 약관 목록에 넣어 동의 기록이 남게 한다.
항목 이름은 화면 문구 그대로("… 동의"), 앱은 앞에 [필수]/[선택]만 붙인다. 만 14세는 본문이 없어 주소를 비운다(앱이 "보기"를 숨김).
앱은 약관 목록을 서버에서 받아 그리므로 옛 빌드도 새 필수 항목을 그대로 보여 준다.

Revision ID: 7a1d4e2b9c30
Revises: 5b7e2d9c4a13
Create Date: 2026-11-12 17:00:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '7a1d4e2b9c30'
down_revision: Union[str, Sequence[str], None] = '5b7e2d9c4a13'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

AGE14 = {"id": "age14", "title": "만 14세 이상입니다", "required": True, "version": "2026-11-12", "url": "", "sort": 3}
TITLES = {"terms": ("서비스 이용약관", "서비스 이용약관 동의"), "privacy": ("개인정보 수집·이용", "개인정보 수집·이용 동의")}


def upgrade() -> None:
    table = sa.table(
        "agreements",
        sa.column("id", sa.Text), sa.column("title", sa.Text), sa.column("required", sa.Boolean),
        sa.column("version", sa.Text), sa.column("url", sa.Text), sa.column("sort", sa.SmallInteger),
    )
    op.bulk_insert(table, [AGE14])
    op.execute("UPDATE agreements SET sort = 4 WHERE id = 'notice'")
    for aid, (_, new) in TITLES.items():
        op.execute(sa.text("UPDATE agreements SET title = :t WHERE id = :i").bindparams(t=new, i=aid))


def downgrade() -> None:
    for aid, (old, _) in TITLES.items():
        op.execute(sa.text("UPDATE agreements SET title = :t WHERE id = :i").bindparams(t=old, i=aid))
    op.execute("UPDATE agreements SET sort = 3 WHERE id = 'notice'")
    op.execute("DELETE FROM user_agreements WHERE agreement_id = 'age14'")
    op.execute("DELETE FROM agreements WHERE id = 'age14'")
