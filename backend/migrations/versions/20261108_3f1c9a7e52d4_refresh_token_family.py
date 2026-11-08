"""refresh token family

로그인 한 번 = family 하나(재사용 탐지 때 그 로그인만 폐기), 기기 ID(같은 기기 재로그인 때 예전 것 폐기),
폐기 이유(교체 직후 재시도 유예 판단), 교체로 대신 나간 줄. 계획 0016 "리뷰 후 수정" 5·8번.

Revision ID: 3f1c9a7e52d4
Revises: 98f24f758845
Create Date: 2026-11-08 15:00:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '3f1c9a7e52d4'
down_revision: Union[str, Sequence[str], None] = '98f24f758845'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column('refresh_tokens', sa.Column('family_id', sa.Text(), nullable=True))
    op.add_column('refresh_tokens', sa.Column('device_id', sa.Text(), nullable=True))
    op.add_column('refresh_tokens', sa.Column('revoked_reason', sa.Text(), nullable=True))
    op.add_column('refresh_tokens', sa.Column('replaced_by_id', sa.Text(), nullable=True))
    # 이미 있는 줄: 각자 자기 family. 이미 폐기된 줄은 이유를 모르므로 LOGOUT(유예·family 폐기 없이 그냥 401)
    op.execute("UPDATE refresh_tokens SET family_id = id")
    op.execute("UPDATE refresh_tokens SET revoked_reason = 'LOGOUT' WHERE revoked_at IS NOT NULL")
    op.alter_column('refresh_tokens', 'family_id', nullable=False)
    op.create_index(op.f('ix_refresh_tokens_family_id'), 'refresh_tokens', ['family_id'], unique=False)
    op.create_check_constraint(
        'ck_refresh_tokens_revoked_reason', 'refresh_tokens',
        "revoked_reason IN ('ROTATED', 'LOGOUT', 'REUSED', 'REPLACED', 'LIMIT')")
    op.create_check_constraint(
        'ck_refresh_tokens_revoked_pair', 'refresh_tokens', "(revoked_at IS NULL) = (revoked_reason IS NULL)")


def downgrade() -> None:
    op.drop_constraint('ck_refresh_tokens_revoked_pair', 'refresh_tokens', type_='check')
    op.drop_constraint('ck_refresh_tokens_revoked_reason', 'refresh_tokens', type_='check')
    op.drop_index(op.f('ix_refresh_tokens_family_id'), table_name='refresh_tokens')
    op.drop_column('refresh_tokens', 'replaced_by_id')
    op.drop_column('refresh_tokens', 'revoked_reason')
    op.drop_column('refresh_tokens', 'device_id')
    op.drop_column('refresh_tokens', 'family_id')
