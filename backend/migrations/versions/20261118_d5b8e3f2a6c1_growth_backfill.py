"""growth backfill

계획 0048 정할 것 3(11/18 민재 승인): 성장 반영(core/growth.settle) 전에 정해진 기록(인증 완료·미인증)을 시간순으로
다시 계산해 run_rewards·user_progress를 채운다. user_progress는 기록에서 언제든 다시 계산하는 캐시(data_model.md).
규칙은 core/growth.py와 같게 여기 적어 둔다(마이그레이션은 앱 코드를 부르지 않음: 나중에 바뀌어도 이 기록은 그대로).
레벨 임계값 3·10·25·50·100 km(BL-34 ③ 11/06 확정), 러닝복 = min(레벨, 6), 주간 목표 1회 = 인증 완료 + 5분 이상.
임팩트는 목록이 없어(11/24 이후) 해금 없음. 이미 반영 줄이 있는 기록은 건너뜀.
내리기: 이 배포 뒤 쌓인 반영까지 지우고(둘 다 캐시) 표를 비운다.

Revision ID: d5b8e3f2a6c1
Revises: c4a9d2e7f1b3
Create Date: 2026-11-18 21:00:00

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = 'd5b8e3f2a6c1'
down_revision: Union[str, Sequence[str], None] = 'c4a9d2e7f1b3'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

LEVEL_M = (3000, 10000, 25000, 50000, 100000)
OUTFIT_STAGES = 6
WEEKLY_MIN_S = 300


def _level(xp: int) -> int:
    return 1 + sum(1 for t in LEVEL_M if xp >= t)


def upgrade() -> None:
    conn = op.get_bind()
    rows = conn.execute(sa.text(
        "SELECT r.id, r.user_id, r.status, COALESCE(r.official_distance_m, 0), COALESCE(r.official_duration_s, 0) "
        "FROM runs r LEFT JOIN run_rewards w ON w.run_id = r.id "
        "WHERE r.status IN ('VERIFIED', 'UNVERIFIED') AND w.run_id IS NULL "
        "ORDER BY r.user_id, r.ended_at NULLS FIRST, r.id")).all()
    progress: dict[str, list[int]] = {}   # user → [xp, verified]
    for run_id, user_id, status, dist, dur in rows:
        xp, count = progress.setdefault(user_id, [0, 0])
        before, level_before = xp, _level(xp)
        if status == "VERIFIED":
            xp, count = xp + dist, count + 1
            progress[user_id] = [xp, count]
        level = _level(xp)
        conn.execute(sa.text(
            "INSERT INTO run_rewards (run_id, xp_before_m, xp_after_m, level_before, level_after, outfit_stage_after, "
            "impacts_unlocked, weekly_counted) VALUES (:r, :b, :a, :lb, :la, :o, '{}', :w)").bindparams(
            r=run_id, b=before, a=xp, lb=level_before, la=level, o=min(level, OUTFIT_STAGES),
            w=status == "VERIFIED" and dur >= WEEKLY_MIN_S))
    for user_id, (xp, count) in progress.items():
        level = _level(xp)
        conn.execute(sa.text(
            "INSERT INTO user_progress (user_id, xp_m, level, outfit_stage, verified_count) VALUES (:u, :x, :l, :o, :c) "
            "ON CONFLICT (user_id) DO UPDATE SET xp_m = user_progress.xp_m + EXCLUDED.xp_m, "
            "verified_count = user_progress.verified_count + EXCLUDED.verified_count").bindparams(
            u=user_id, x=xp, l=level, o=min(level, OUTFIT_STAGES), c=count))
    # 위 ON CONFLICT로 더한 사람(이미 줄이 있던 사람, 이 배포 전엔 표가 비어 있어 없을 것)도 합친 경험치로 레벨을 다시
    conn.execute(sa.text(
        "UPDATE user_progress SET level = 1 + " + " + ".join(f"(CASE WHEN xp_m >= {t} THEN 1 ELSE 0 END)" for t in LEVEL_M)))
    conn.execute(sa.text(f"UPDATE user_progress SET outfit_stage = LEAST(level, {OUTFIT_STAGES})"))


def downgrade() -> None:
    op.execute("DELETE FROM run_rewards")
    op.execute("DELETE FROM user_progress")
