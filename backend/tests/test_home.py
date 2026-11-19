"""홈 한 번에(계획 0057, BL-31): GET /home이 각 API와 같은 값을 모으는지. 실행 전 DB가 떠 있어야 함."""
from sqlalchemy import update

from app.db.models import CourseEntry, Run
from app.realtime.hub import hub
from tests.helpers import data, signup
from tests.test_friends import LAKE_M, befriend
from tests.test_run_records import add_run, my_id
from tests.test_runs import ago, db, enter  # noqa: F401 (픽스처)


def home(client, headers) -> dict:
    return data(client.get("/v1/home", headers=headers))


def test_new_user_is_first_use(db_client):
    """가입만 한 사람: 목표 없음(B-02 판단)·Lv.1·친구 없음·기록 없음(B-04). 코스는 목록과 같은 운영 코스."""
    a = signup(db_client, "k_1")
    out = home(db_client, a)
    assert out["nickname"] == data(db_client.get("/v1/users/me", headers=a))["nickname"]
    assert out["weeklyGoal"]["targetCount"] is None
    assert (out["progress"]["level"], out["progress"]["xpM"]) == (1, 0)
    assert (out["runningFriends"], out["recentRuns"]) == ([], [])
    listed = data(db_client.get("/v1/courses", headers=a))
    assert sorted(c["courseId"] for c in out["courses"]) == sorted(c["courseId"] for c in listed)


def test_same_values_as_each_api(db_client, db):
    """주간 목표·레벨·달리는 친구는 따로 있는 API와 똑같다(같은 함수)."""
    a, b = signup(db_client, "k_1"), signup(db_client, "k_2")
    befriend(db_client, a, b)
    ua = my_id(db_client, a)
    assert db_client.put("/v1/users/me/weekly-goal", headers=a, json={"targetCount": 3}).status_code == 200
    add_run(db, ua, ago(hours=2), status="VERIFIED", duration=1200)
    conn, _ = hub.join("map_crs_lake_loop", "crs_lake_loop", LAKE_M, my_id(db_client, b), "러너2")
    try:
        hub.update(conn, 20.0, 10.0, True, run=1500.0)
        out = home(db_client, a)
        assert out["weeklyGoal"] == data(db_client.get("/v1/users/me/weekly-goal", headers=a))
        assert out["progress"] == data(db_client.get("/v1/users/me/progress", headers=a))
        assert out["runningFriends"] == data(db_client.get("/v1/friends/running", headers=a))
        assert [f["distanceM"] for f in out["runningFriends"]] == [1500]
    finally:
        hub.leave(conn, intentional=True)


def test_recent_runs_are_my_finished_runs_newest_first(db_client, db):
    """끝낸 러닝만 최근 3개(달리는 중·버린 것·남의 것은 빼고). 마감 지난 인증 대기는 닫힌 상태(UNVERIFIED)로."""
    a, other = signup(db_client, "k_1"), signup(db_client, "k_2")
    ua = my_id(db_client, a)
    ids = [add_run(db, ua, ago(hours=h), status="VERIFIED") for h in (40, 30, 20)]
    expired = add_run(db, ua, ago(hours=10))                                     # PENDING, 아래에서 마감을 지난 것으로
    db.query(Run).filter(Run.id == expired).update({Run.verification_deadline: ago(minutes=1)})
    db.commit()
    add_run(db, ua, ago(hours=5), status="ACTIVE")
    add_run(db, ua, ago(hours=4), status="DISCARDED")
    add_run(db, my_id(db_client, other), ago(hours=1), status="VERIFIED")
    out = home(db_client, a)["recentRuns"]
    assert [r["runId"] for r in out] == [expired, ids[2], ids[1]]
    assert out[0]["verification"] == "UNVERIFIED"
    assert out[0]["courseName"] == "노을 호수 한 바퀴"


def test_courses_with_more_runners_first(db_client, db):
    """홈 카드는 지금 달리는 사람이 많은 코스부터(같으면 목록 순서 = 짧은 코스부터)."""
    a, b = signup(db_client, "k_1"), signup(db_client, "k_2")
    listed = [c["courseId"] for c in data(db_client.get("/v1/courses", headers=a))]
    assert len(listed) >= 2
    busy = listed[-1]                       # 가장 긴 코스에 한 사람이 들어감
    enter(db_client, db, b, course=busy)
    db.execute(update(CourseEntry).values(entered_at=ago(minutes=0)))   # enter는 1시간 전으로 돌림 → 방금 들어온 사람으로
    db.commit()
    out = [c["courseId"] for c in home(db_client, a)["courses"]]
    assert out[0] == busy
    assert out[1:] == [c for c in listed if c != busy]
    assert home(db_client, b)["courses"][0]["runnersNow"] == 0, "보는 사람은 빼고 셈(목록과 같음)"


def test_needs_login(db_client):
    r = db_client.get("/v1/home")
    assert r.status_code == 401
