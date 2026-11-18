"""growth: run_rewards status·weekly snapshot, backfill

계획 0048(11/18 민재 승인).
1. run_rewards에 칸 셋: status(반영할 때의 판정, 마감으로 닫힌 미인증 줄을 유예 안 인증 완료로 다시 반영할 때 구분),
   weekly_done_count·weekly_target_count(그때 주간 목표 칸, 결과 화면을 다시 열어도 같게)
2. 정할 것 3: 성장 반영(core/growth.settle) 전에 정해진 기록(인증 완료·미인증)을 시간순으로 다시 계산해 run_rewards·
   user_progress를 채운다. user_progress는 기록에서 다시 계산하는 캐시(data_model.md). 이미 반영 줄이 있는 기록은 건너뜀.
   규칙은 core/growth.py와 같게 여기 적어 둔다(마이그레이션은 앱 코드를 부르지 않음): 레벨 임계값 3·10·25·50·100 km
   (BL-34 ③ 11/06 확정), 러닝복 = min(레벨, 6), 주간 목표 1회 = 인증 완료 + 5분 이상. 레벨·러닝복은 API가 경험치로 매번
   다시 계산하므로 GROWTH_LEVEL_M을 바꿔도 맞음(여기 적는 level은 그때 기록). 임팩트는 목록이 없어(11/24 이후) 해금 없음.
   주간 목표는 이 배포 전엔 목표를 저장하는 API가 없어(지환 BL-30) 횟수 0·목표 없음.
내리기: 칸만 지운다. 채운 반영 줄·레벨은 지우지 않음(임팩트 선택 등 다른 값과 섞여 있어 되살릴 수 없음, 옛 코드는 안 읽음).

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
    op.add_column("run_rewards", sa.Column("status", sa.Text(), nullable=True))
    op.add_column("run_rewards", sa.Column("weekly_done_count", sa.SmallInteger(), server_default="0", nullable=False))
    op.add_column("run_rewards", sa.Column("weekly_target_count", sa.SmallInteger(), nullable=True))
    conn = op.get_bind()
    # 이 칸 전에 쓴 줄(이 기능 전엔 쓰는 코드가 없어 없을 것): 경험치가 늘었으면 인증 완료
    conn.execute(sa.text("UPDATE run_rewards SET status = CASE WHEN xp_after_m > xp_before_m THEN 'VERIFIED' "
                         "ELSE 'UNVERIFIED' END WHERE status IS NULL"))
    op.alter_column("run_rewards", "status", nullable=False)

    progress = {uid: [xp, count] for uid, xp, count in
                conn.execute(sa.text("SELECT user_id, xp_m, verified_count FROM user_progress"))}
    rows = conn.execute(sa.text(
        "SELECT r.id, r.user_id, r.status, COALESCE(r.official_distance_m, 0), COALESCE(r.official_duration_s, 0) "
        "FROM runs r LEFT JOIN run_rewards w ON w.run_id = r.id "
        "WHERE r.status IN ('VERIFIED', 'UNVERIFIED') AND w.run_id IS NULL "
        "ORDER BY r.user_id, r.ended_at, r.id")).all()   # core/growth.settle_ids와 같은 순서
    touched = set()
    for run_id, user_id, status, dist, dur in rows:
        xp, count = progress.setdefault(user_id, [0, 0])
        before = xp
        if status == "VERIFIED":
            xp, count = xp + dist, count + 1
            progress[user_id] = [xp, count]
        touched.add(user_id)
        level = _level(xp)
        conn.execute(sa.text(
            "INSERT INTO run_rewards (run_id, status, xp_before_m, xp_after_m, level_before, level_after, outfit_stage_after, "
            "impacts_unlocked, weekly_counted, weekly_done_count) VALUES (:r, :s, :b, :a, :lb, :la, :o, '{}', :w, 0)").bindparams(
            r=run_id, s=status, b=before, a=xp, lb=_level(before), la=level, o=min(level, OUTFIT_STAGES),
            w=status == "VERIFIED" and dur >= WEEKLY_MIN_S))
    for user_id in touched:
        xp, count = progress[user_id]
        level = _level(xp)
        conn.execute(sa.text(
            "INSERT INTO user_progress (user_id, xp_m, level, outfit_stage, verified_count) VALUES (:u, :x, :l, :o, :c) "
            "ON CONFLICT (user_id) DO UPDATE SET xp_m = EXCLUDED.xp_m, level = EXCLUDED.level, "
            "outfit_stage = EXCLUDED.outfit_stage, verified_count = EXCLUDED.verified_count").bindparams(
            u=user_id, x=xp, l=level, o=min(level, OUTFIT_STAGES), c=count))


def downgrade() -> None:
    op.drop_column("run_rewards", "weekly_target_count")
    op.drop_column("run_rewards", "weekly_done_count")
    op.drop_column("run_rewards", "status")
