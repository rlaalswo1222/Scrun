"""성장(계획 0048, BL-32·34·30): 인증 후 경험치·레벨·러닝복·주간 목표 반영. 실행 전 DB가 떠 있어야 함."""
import os
import subprocess
import sys
from datetime import date, datetime, timedelta, timezone
from pathlib import Path

import pytest
from sqlalchemy import select, update

from app.core import growth
from app.core.config import ConfigError, load_growth
from app.db.models import Run, RunReward, User, UserProgress, WeeklyGoal, WeeklyGoalCycle
from tests.helpers import data, fail, run_together, signup
from tests.test_run_records import add_run, my_id
from tests.test_runs import ago, db, me  # noqa: F401  (픽스처)
from tests.test_verification import confirm, finished, photo, read_then, run_row, skip


def progress(client, headers) -> dict:
    return data(client.get("/v1/users/me/progress", headers=headers))


def rewards(client, headers, run_id):
    return client.get(f"/v1/runs/{run_id}/rewards", headers=headers)


# ---------- 규칙 ----------
def test_levels_follow_the_backlog_thresholds():
    """BL-34 ③: Lv.2 3km, Lv.3 10km, Lv.4 25km, Lv.5 50km, Lv.6 100km. 러닝복 = min(레벨, 6), 100 km 뒤는 다음 레벨 없음."""
    assert [growth.level_of(m) for m in (0, 2999, 3000, 9999, 10000, 25000, 50000, 99999, 100000, 10 ** 7)] == \
        [1, 1, 2, 2, 3, 4, 5, 5, 6, 6]
    assert growth.remaining_m(0) == 3000 and growth.remaining_m(4120) == 5880
    assert growth.next_level_at(100000) is None and growth.remaining_m(250000) is None
    assert growth.outfit_of(6) == 6 and growth.outfit_of(9) == 6


def test_level_thresholds_from_env():
    assert load_growth({}).level_m == (3000, 10000, 25000, 50000, 100000)
    assert load_growth({"GROWTH_LEVEL_M": "3000, 10000,25000,50000,100000,200000"}).level_m[-1] == 200000
    for bad in ("3000,3000", "10000,3000", "0,3000", "3km", "3000,"):
        with pytest.raises(ConfigError):
            load_growth({"GROWTH_LEVEL_M": bad})


# ---------- 반영 ----------
def test_verified_run_adds_distance_count_and_level(db_client, me, db):
    run_id = finished(db_client, me)
    out = data(read_then(db_client, me, run_id, "d3050_t1010.jpg"))
    assert out["status"] == "VERIFIED"
    r = out["rewards"]
    assert (r["xpBeforeM"], r["xpAfterM"], r["levelBefore"], r["levelAfter"], r["leveledUp"]) == (0, 3050, 1, 2, True)
    assert (r["outfitStageAfter"], r["nextLevelRemainingM"], r["impactsUnlocked"]) == (2, 10000 - 3050, [])
    assert r["weeklyGoal"] == {"counted": True, "doneCount": 0, "targetCount": None}, "목표 없음 → 인정은 되지만 주기 없음"
    assert data(rewards(db_client, me, run_id)) == r, "결과 화면을 다시 열어도 같음"
    p = progress(db_client, me)
    assert (p["level"], p["xpM"], p["nextLevelAtM"], p["outfitStage"], p["verifiedCount"], p["impacts"]) == \
        (2, 3050, 10000, 2, 1, [])


def test_unverified_runs_add_nothing(db_client, me, db):
    """BL-32 ④: 미인증(건너뛰기·오차 초과·마감)은 경험치·횟수에 안 씀."""
    skipped = finished(db_client, me)
    r = data(skip(db_client, me, skipped, "SKIPPED"))["rewards"]
    assert (r["xpBeforeM"], r["xpAfterM"], r["leveledUp"], r["weeklyGoal"]["counted"]) == (0, 0, False, False)
    mismatch = finished(db_client, me, cid="c2")
    assert data(read_then(db_client, me, mismatch, "d3800_t1000.jpg"))["rewards"]["xpAfterM"] == 0
    late = finished(db_client, me, cid="c3")
    db.execute(update(Run).where(Run.id == late).values(verification_deadline=ago(minutes=1)))
    db.commit()
    db_client.get("/v1/runs", headers=me)   # 목록이 마감으로 닫음
    assert (run_row(db, late).status, db.get(RunReward, late).xp_after_m) == ("UNVERIFIED", 0)
    p = progress(db_client, me)
    assert (p["level"], p["xpM"], p["verifiedCount"]) == (1, 0, 0)


def test_same_run_is_counted_once(db_client, me, db):
    run_id = finished(db_client, me)
    shown = data(photo(db_client, me, run_id, "d3000_t1000.jpg"))
    rs = run_together([lambda c: confirm(c, me, run_id, shown["attemptId"])] * 4)   # 두 번 누름·재시도
    assert all(r.status_code == 200 for r in rs), [r.text for r in rs]
    data(skip(db_client, me, run_id, "SKIPPED"))   # 정해진 뒤 건너뛰기 → 처음 결과
    rewards(db_client, me, run_id)
    p = progress(db_client, me)
    assert (p["xpM"], p["verifiedCount"]) == (3000, 1)


def test_closed_by_deadline_then_confirmed_in_grace_counts(db_client, me, db):
    """마감 직전 사진 → 목록 조회가 마감으로 닫음(미인증 반영 줄) → 유예 안 맞아요 → 인증 완료로 다시 반영."""
    from app.core.run_access import close_expired
    run_id = finished(db_client, me)
    shown = data(photo(db_client, me, run_id, "d3000_t1000.jpg"))
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(seconds=30)))
    db.commit()
    close_expired(db, run_id=run_id)
    db.commit()
    assert db.get(RunReward, run_id).xp_after_m == 0
    assert data(confirm(db_client, me, run_id, shown["attemptId"]))["rewards"]["xpAfterM"] == 3000
    assert progress(db_client, me)["verifiedCount"] == 1


def test_rewards_need_a_decided_run_of_mine(db_client, me, db):
    run_id = finished(db_client, me)
    fail(rewards(db_client, me, run_id), 409, "RUN_NOT_FINISHED")   # 인증 대기
    other = signup(db_client, "k_2")
    fail(rewards(db_client, other, run_id), 403, "FORBIDDEN")
    fail(rewards(db_client, me, "01NOSUCHRUN000000000000000"), 404, "RUN_NOT_FOUND")


def test_old_decided_run_without_reward_row_is_settled_on_read(db_client, me, db):
    """이 기능 전에 정해진 기록(마이그레이션이 채우지만 빠진 것이 있어도): 읽을 때 반영."""
    uid = my_id(db_client, me)
    run_id = add_run(db, uid, ago(days=1), status="VERIFIED", distance=12000, duration=3600)
    r = data(rewards(db_client, me, run_id))
    assert (r["xpAfterM"], r["levelAfter"]) == (12000, 3)
    assert data(rewards(db_client, me, run_id)) == r and progress(db_client, me)["xpM"] == 12000


def test_impact_select_only_unlocked(db_client, me):
    assert data(db_client.put("/v1/users/me/impact", headers=me, json={"impactId": None}))["selectedImpactId"] is None
    fail(db_client.put("/v1/users/me/impact", headers=me, json={"impactId": "imp_spark"}), 400, "VALIDATION_ERROR")


def test_new_user_starts_at_level_one(db_client):
    p = progress(db_client, signup(db_client, "k_5"))
    assert p == {"level": 1, "xpM": 0, "nextLevelAtM": 3000, "nextLevelRemainingM": 3000, "outfitStage": 1,
                 "verifiedCount": 0, "impacts": [], "selectedImpactId": None}


# ---------- 주간 목표 ----------
def test_weekly_cycle_from_the_day_the_goal_was_set(db_client, me, db):
    """BL-30 ③④: 설정한 날부터 7일 주기(사용자 시간대), 인증 완료 + 5분 이상만 1회."""
    uid = my_id(db_client, me)
    user = db.get(User, uid)
    today = growth.local_date(datetime.now(timezone.utc), user)
    db.add(WeeklyGoal(user_id=uid, target_count=3, anchor_date=today - timedelta(days=8)))
    db.add(WeeklyGoalCycle(user_id=uid, cycle_start=today - timedelta(days=8), target_count=2))   # 지난 주기는 2회 목표
    db.commit()
    c = growth.cycle_at(db, user, today)
    assert (c.start, c.target) == (today - timedelta(days=1), 3)
    assert growth.cycle_at(db, user, today - timedelta(days=2)).target == 2
    assert growth.cycle_at(db, user, today - timedelta(days=9)) is None, "설정 전"

    short = finished(db_client, me, distance=1000, duration=250)
    assert data(read_then(db_client, me, short, "d1000_t250.jpg"))["rewards"]["weeklyGoal"] == \
        {"counted": False, "doneCount": 0, "targetCount": 3}, "5분 안 됨"
    long_ = finished(db_client, me, cid="c2")
    assert data(read_then(db_client, me, long_, "d3000_t1000.jpg"))["rewards"]["weeklyGoal"] == \
        {"counted": True, "doneCount": 1, "targetCount": 3}


def test_week_is_counted_in_the_users_timezone(db_client, me, db):
    """서울 자정 직전에 끝난 기록은 그 주기, 직후는 다음 주기(UTC 날짜가 아님)."""
    uid = my_id(db_client, me)
    user = db.get(User, uid)
    anchor = date(2026, 11, 2)
    db.add(WeeklyGoal(user_id=uid, target_count=3, anchor_date=anchor))
    db.commit()
    seoul_midnight = datetime(2026, 11, 9, 0, 0, tzinfo=timezone.utc) - timedelta(hours=9)   # 11/9 00:00 서울 = 다음 주기 시작
    before = add_run(db, uid, seoul_midnight - timedelta(seconds=1001), status="VERIFIED")   # 11/8 23:59:59 서울에 끝남
    after = add_run(db, uid, seoul_midnight - timedelta(seconds=999), status="VERIFIED")     # 11/9 00:00:01
    first = growth.cycle_at(db, user, anchor)
    assert growth.counted_in(db, user, first) == 1
    assert growth.cycle_at(db, user, growth.local_date(run_row(db, after).ended_at, user)).start == date(2026, 11, 9)
    assert run_row(db, before).ended_at < seoul_midnight <= run_row(db, after).ended_at


# ---------- 마이그레이션 ----------
def test_backfill_migration_recomputes_in_time_order(db_client, me, db):
    """정할 것 3 가: 이미 정해진 기록을 시간순으로 다시 계산(내리기 → 올리기)."""
    from app.db.testing import TEST_DATABASE_URL
    uid = my_id(db_client, me)
    a = add_run(db, uid, ago(days=3), status="VERIFIED", distance=2500, duration=900)
    b = add_run(db, uid, ago(days=2), status="UNVERIFIED", distance=9000, duration=3000, unverified_reason="SKIPPED")
    c = add_run(db, uid, ago(days=1), status="VERIFIED", distance=8000, duration=200)
    env = {**os.environ, "ALEMBIC_DATABASE_URL": TEST_DATABASE_URL}
    backend = Path(__file__).resolve().parents[1]
    for step in (["downgrade", "c4a9d2e7f1b3"], ["upgrade", "head"]):
        r = subprocess.run([sys.executable, "-m", "alembic", *step], cwd=backend, env=env, capture_output=True,
                           text=True, encoding="utf-8", errors="replace", timeout=120)
        assert r.returncode == 0, r.stderr
    db.expire_all()
    got = {rid: db.get(RunReward, rid) for rid in (a, b, c)}
    assert (got[a].xp_before_m, got[a].xp_after_m, got[a].level_after, got[a].weekly_counted) == (0, 2500, 1, True)
    assert (got[b].xp_before_m, got[b].xp_after_m, got[b].weekly_counted) == (2500, 2500, False)
    assert (got[c].xp_after_m, got[c].level_before, got[c].level_after, got[c].weekly_counted) == (10500, 1, 3, False)
    p = db.get(UserProgress, uid)
    assert (p.xp_m, p.level, p.outfit_stage, p.verified_count) == (10500, 3, 3, 2)
