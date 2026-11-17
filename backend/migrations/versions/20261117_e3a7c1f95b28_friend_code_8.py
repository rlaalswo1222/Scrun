"""friend code 8 chars, feed index

계획 0044 정할 것 1(11/17 민재 승인): 친구 코드를 `SCR-` + 4글자(약 105만 개)에서 8글자(32^8 ≈ 1조 개)로.
코드 = 친구 피드를 보는 열쇠라 맞혀 보기 어렵게. Figma G-06 "친구에게 받은 8자리 코드"와도 맞음.
지금 있는 사용자는 모두 새 코드를 받는다(아직 친구 기능이 출시 전이라 옛 코드를 나눠 준 사람이 없음).
글자는 app/core/friend_code.py와 같게 둔다(마이그레이션은 앱 코드를 부르지 않음: 나중에 바뀌어도 이 기록은 그대로).
친구 피드·프로필이 친구들의 끝난 기록을 끝난 시각 최신순으로 읽는 인덱스 runs(user_id, ended_at, id)도 같이(리뷰).

Revision ID: e3a7c1f95b28
Revises: b8e1f0c3d5a7
Create Date: 2026-11-17 23:00:00

"""
import secrets
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = 'e3a7c1f95b28'
down_revision: Union[str, Sequence[str], None] = 'b8e1f0c3d5a7'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"


def _recode(make) -> None:
    conn = op.get_bind()
    ids = [row[0] for row in conn.execute(sa.text("SELECT id FROM users ORDER BY id"))]
    used: set[str] = set()
    for uid in ids:
        code = make()
        while code in used:
            code = make()
        used.add(code)
        conn.execute(sa.text("UPDATE users SET friend_code = :c WHERE id = :i").bindparams(c=code, i=uid))


def upgrade() -> None:
    # 옛 코드와 새 코드는 길이가 달라 겹칠 수 없음 → 한 줄씩 바꿔도 유니크 제약에 안 걸림
    _recode(lambda: "".join(secrets.choice(ALPHABET) for _ in range(8)))
    op.create_index("ix_runs_user_id_ended_at_id", "runs", ["user_id", "ended_at", "id"])


def downgrade() -> None:
    op.drop_index("ix_runs_user_id_ended_at_id", table_name="runs")
    _recode(lambda: "SCR-" + "".join(secrets.choice(ALPHABET) for _ in range(4)))
