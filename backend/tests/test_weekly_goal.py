"""주간 목표(계획 0054, BL-30): GET·PUT /users/me/weekly-goal을 DB와 연결. 실행 전 DB가 떠 있어야 함.

주기 = 설정한 날부터 7일(사용자 시간대), 1회 = 인증 완료 + 5분 이상(계획 0048 growth.cycle_at·counted_in을 그대로 씀).
변경은 다음 주기부터: 지난 주기·이번 주기 목표는 weekly_goal_cycles에 굳힘.
"""
from datetime import date, datetime, timedelta, timezone

from app.core import growth
from app.db.models import User, WeeklyGoal, WeeklyGoalCycle
from tests.helpers import data, fail
from tests.test_run_records import add_run, my_id
from tests.test_runs import db, me  # noqa: F401  (픽스처)
from tests.test_verification import finished, read_then

KST = timezone(timedelta(hours=9))


def get(client, headers) -> dict:
    return data(client.get("/v1/users/me/weekly-goal", headers=headers))


def put(client, headers, n):
    return client.put("/v1/users/me/weekly-goal", headers=headers, json={"targetCount": n})


def today_of(db, uid) -> date:
    return growth.local_date(datetime.now(timezone.utc), db.get(User, uid))


def set_anchor(db, uid, anchor: date, target=3):
    db.add(WeeklyGoal(user_id=uid, target_count=target, anchor_date=anchor))
    db.commit()


# ---------- 처음 ----------
def test_no_goal_is_empty(db_client, me):
    assert get(db_client, me) == {"targetCount": None, "cycleStart": None, "cycleEnd": None, "doneCount": 0,
                                  "daysLeft": None, "nextTargetCount": None, "history": []}


def test_first_goal_starts_today(db_client, me, db):
    today = today_of(db, my_id(db_client, me))
    out = data(put(db_client, me, 3))
    assert out == {"targetCount": 3, "cycleStart": today.isoformat(), "cycleEnd": (today + timedelta(days=6)).isoformat(),
                   "doneCount": 0, "daysLeft": 7, "nextTargetCount": None, "history": []}
    assert get(db_client, me) == out, "응답은 GET과 같음"


def test_target_must_be_1_to_7(db_client, me):
    for bad in (0, 8, -1):
        r = put(db_client, me, bad)
        fail(r, 400, "VALIDATION_ERROR")
        assert any(f["field"] == "targetCount" for f in r.json()["error"]["details"]["fields"])
    assert get(db_client, me)["targetCount"] is None, "실패하면 저장 안 됨"


# ---------- 집계 ----------
def ended_at(db, uid, local: datetime, status="VERIFIED", duration=1000):
    """서울 시각 local에 끝난 기록(시각을 고정해 자정 무렵에 돌려도 같게)."""
    return add_run(db, uid, local.astimezone(timezone.utc) - timedelta(seconds=duration), status=status, duration=duration)


def test_only_verified_runs_of_five_minutes_count(db_client, me, db):
    """BL-30 ④: 인증 완료 중 5분 이상만 1회. 미인증·대기·4분 59초·설정 전 기록은 빼고."""
    uid = my_id(db_client, me)
    set_anchor(db, uid, date(2026, 11, 10))
    ended_at(db, uid, datetime(2026, 11, 12, 19, tzinfo=KST), duration=300)              # 딱 5분
    ended_at(db, uid, datetime(2026, 11, 12, 18, tzinfo=KST), duration=299)              # 4분 59초
    ended_at(db, uid, datetime(2026, 11, 12, 17, tzinfo=KST), status="UNVERIFIED")
    ended_at(db, uid, datetime(2026, 11, 12, 16, tzinfo=KST), status="PENDING")
    ended_at(db, uid, datetime(2026, 11, 9, 23, 59, tzinfo=KST))                          # 설정 전날
    out = growth.weekly_payload(db, db.get(User, uid), now=datetime(2026, 11, 12, 21, tzinfo=KST))
    assert (out["doneCount"], out["history"]) == (1, [])


def test_weekly_count_matches_the_result_screen(db_client, me, db):
    """결과 화면 E-11의 주간 칸(인증할 때 저장)과 GET이 같은 횟수. 설정일은 어제(자정 무렵에 돌려도 기록이 같은 주기에)."""
    uid = my_id(db_client, me)
    set_anchor(db, uid, today_of(db, uid) - timedelta(days=1), target=2)
    run_id = finished(db_client, me)
    seen = data(read_then(db_client, me, run_id, "d3000_t1000.jpg"))["rewards"]["weeklyGoal"]
    assert seen == {"counted": True, "doneCount": 1, "targetCount": 2}
    assert (get(db_client, me)["doneCount"], get(db_client, me)["targetCount"]) == (1, 2)


def test_cycle_boundary_in_the_users_timezone(db_client, me, db):
    """설정일부터 7일. 서울 7일째 23:59는 그 주기(남은 1일), 8일째 0시는 다음 주기(이력에 첫 주기)."""
    uid = my_id(db_client, me)
    user = db.get(User, uid)
    set_anchor(db, uid, date(2026, 11, 2))
    last = growth.weekly_payload(db, user, now=datetime(2026, 11, 8, 23, 59, tzinfo=KST))
    assert (last["cycleStart"], last["cycleEnd"], last["daysLeft"], last["history"]) == \
        (date(2026, 11, 2), date(2026, 11, 8), 1, [])
    nxt = growth.weekly_payload(db, user, now=datetime(2026, 11, 9, 0, 0, 1, tzinfo=KST))
    assert (nxt["cycleStart"], nxt["daysLeft"]) == (date(2026, 11, 9), 7)
    assert nxt["history"] == [{"cycleStart": date(2026, 11, 2), "targetCount": 3, "doneCount": 0}]
    utc_same_day = growth.weekly_payload(db, user, now=datetime(2026, 11, 8, 16, 0, tzinfo=timezone.utc))   # 11/9 01:00 서울
    assert utc_same_day["cycleStart"] == date(2026, 11, 9), "UTC 날짜(11/8)가 아니라 서울 날짜"


def test_history_is_the_last_seven_cycles_oldest_first(db_client, me, db):
    uid = my_id(db_client, me)
    user = db.get(User, uid)
    set_anchor(db, uid, date(2026, 9, 1))
    out = growth.weekly_payload(db, user, now=datetime(2026, 11, 18, 12, tzinfo=KST))
    assert out["cycleStart"] == date(2026, 11, 17)
    assert [h["cycleStart"] for h in out["history"]] == [date(2026, 11, 17) - timedelta(days=7 * k) for k in range(7, 0, -1)]
    early = growth.weekly_payload(db, user, now=datetime(2026, 9, 10, 12, tzinfo=KST))
    assert [h["cycleStart"] for h in early["history"]] == [date(2026, 9, 1)], "설정일 전 주기는 없음"


def test_history_counts_each_cycle(db_client, me, db):
    """주기마다 그 주기 목표와 횟수. 주기 경계(7일째 23:59 / 8일째 0시)도 사용자 시간대로 나눔."""
    uid = my_id(db_client, me)
    set_anchor(db, uid, date(2026, 11, 2), target=2)
    db.add(WeeklyGoalCycle(user_id=uid, cycle_start=date(2026, 11, 9), target_count=1))
    db.commit()
    ended_at(db, uid, datetime(2026, 11, 3, 8, tzinfo=KST))
    ended_at(db, uid, datetime(2026, 11, 8, 23, 59, tzinfo=KST))     # 첫 주기 마지막
    ended_at(db, uid, datetime(2026, 11, 9, 0, 1, tzinfo=KST))       # 둘째 주기 처음
    ended_at(db, uid, datetime(2026, 11, 17, 7, tzinfo=KST))
    out = growth.weekly_payload(db, db.get(User, uid), now=datetime(2026, 11, 18, 12, tzinfo=KST))
    assert (out["cycleStart"], out["targetCount"], out["doneCount"]) == (date(2026, 11, 16), 2, 1)
    assert out["history"] == [{"cycleStart": date(2026, 11, 2), "targetCount": 2, "doneCount": 2},
                              {"cycleStart": date(2026, 11, 9), "targetCount": 1, "doneCount": 1}]


# ---------- 변경 ----------
def test_change_applies_from_the_next_cycle(db_client, me, db):
    """BL-30 ⑥: 바꾼 목표는 이번 주기가 끝난 뒤부터. 지난 주기 목표는 그대로."""
    uid = my_id(db_client, me)
    user = db.get(User, uid)
    today = today_of(db, uid)
    set_anchor(db, uid, today - timedelta(days=8), target=3)   # 지난 주기 하나 + 이번 주기 2일째
    out = data(put(db_client, me, 5))
    assert (out["targetCount"], out["nextTargetCount"]) == (3, 5), "이번 주기는 그대로, 다음부터 5"
    assert out["history"][0]["targetCount"] == 3, "지난 주기 목표는 굳힘"
    db.expire_all()
    later = growth.weekly_payload(db, user, now=datetime.now(timezone.utc) + timedelta(days=7))
    assert (later["targetCount"], later["nextTargetCount"]) == (5, None)
    assert [h["targetCount"] for h in later["history"]] == [3, 3]
    assert {r.cycle_start: r.target_count for r in db.query(WeeklyGoalCycle).filter_by(user_id=uid)} == \
        {today - timedelta(days=8): 3, today - timedelta(days=1): 3}


def test_change_twice_and_back_in_one_cycle(db_client, me, db):
    data(put(db_client, me, 3))                              # 처음: 바로 3
    assert data(put(db_client, me, 5))["nextTargetCount"] == 5, "같은 날 바꿔도 다음 주기부터"
    out = data(put(db_client, me, 2))
    assert (out["targetCount"], out["nextTargetCount"]) == (3, 2), "마지막 값"
    out = data(put(db_client, me, 3))
    assert (out["targetCount"], out["nextTargetCount"]) == (3, None), "이번 주기 목표로 되돌리면 예약 없음"
    db.expire_all()
    goal = db.get(WeeklyGoal, my_id(db_client, me))
    assert (goal.target_count, goal.anchor_date) == (3, today_of(db, goal.user_id)), "설정일은 처음 그대로"


def test_change_keeps_the_result_screen_target(db_client, me, db):
    """이번 주기에 인증한 기록의 반영 결과(목표 3)는 목표를 바꿔도 그대로, 이번 주기 GET도 3."""
    uid = my_id(db_client, me)
    set_anchor(db, uid, today_of(db, uid) - timedelta(days=1), target=3)
    run_id = finished(db_client, me)
    data(read_then(db_client, me, run_id, "d3000_t1000.jpg"))
    data(put(db_client, me, 6))
    assert data(db_client.get(f"/v1/runs/{run_id}/rewards", headers=me))["weeklyGoal"]["targetCount"] == 3
    out = get(db_client, me)
    assert (out["targetCount"], out["doneCount"], out["nextTargetCount"]) == (3, 1, 6)


def test_other_users_goal_is_separate(db_client, me, db):
    from tests.helpers import signup
    other = signup(db_client, "k_2")
    data(put(db_client, me, 4))
    assert get(db_client, other)["targetCount"] is None
