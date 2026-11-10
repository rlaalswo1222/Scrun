"""코스 목록·상세·맵 입장 진짜 흐름(테스트 DB, 계획 0021). 실행 전 DB가 떠 있어야 함."""
import threading
import time
from datetime import datetime, timedelta, timezone

import pytest
from sqlalchemy import delete, func, select, update

from app.core import occupancy, tokens
from app.core.occupancy import FRESH
from app.db.ids import new_id
from app.db.models import Calibration, Course, CourseEntry, Run, User
from app.routers import courses as courses_router
from tests.helpers import bearer, data, fail, run_together

COURSES = [  # 짧은 코스부터
    ("crs_lake_loop", "노을 호수 한 바퀴", 6671),
    ("crs_coast_run", "해안 절벽 순환", 8776),
    ("crs_forest_run", "거목 협곡 순환", 9695),
    ("crs_island_tour", "섬 일주", 10884),
]
STALE = FRESH + timedelta(minutes=1)


def signup(client, kakao) -> dict:
    """진짜 가입 → Authorization 헤더."""
    first = data(client.post("/v1/auth/kakao", json={"kakaoAccessToken": kakao}))
    r = client.post("/v1/auth/signup", headers=bearer(first["signupToken"]),
                    json={"agreedIds": ["terms", "privacy"], "nickname": f"러너{kakao[2:]}", "heightCm": 175, "weightKg": 70})
    assert r.status_code == 201, r.text
    return bearer(data(r)["accessToken"])


@pytest.fixture
def db(test_db):
    s = test_db.Session()
    yield s
    s.close()


def user_id_of(headers) -> str:
    return tokens.decode(headers["Authorization"].split()[1])["sub"]


@pytest.fixture
def me(db_client):
    """(내 Authorization 헤더, 내 사용자 ID)."""
    headers = signup(db_client, "k_1")
    return headers, user_id_of(headers)


def add_user(db) -> str:
    uid = new_id()
    db.add(User(id=uid, nickname=f"u{uid[-8:]}", height_cm=170, weight_kg=60, friend_code=f"SCR-{uid[-6:]}"))
    db.commit()
    return uid


def add_run(db, user_id, course_id="crs_lake_loop", status="ACTIVE", age=timedelta(0)):
    """age만큼 전에 마지막으로 갱신된 기록."""
    rid = new_id()
    now = datetime.now(timezone.utc)
    db.add(Run(id=rid, user_id=user_id, client_run_id=rid, course_id=course_id, goal_type="NONE",
               status=status, started_at=now - age - timedelta(minutes=1)))
    db.commit()
    if age:
        db.execute(update(Run).where(Run.id == rid).values(updated_at=now - age))
        db.commit()


def add_entry(db, user_id, course_id="crs_lake_loop", age=timedelta(0)):
    db.add(CourseEntry(user_id=user_id, course_id=course_id, entered_at=datetime.now(timezone.utc) - age))
    db.commit()


def calibrate(db, user_id):
    db.add(Calibration(id=new_id(), user_id=user_id, points=[], model={}, device="iPhone15,4"))
    db.commit()


def enter(client, headers, course="crs_lake_loop"):
    return client.post(f"/v1/courses/{course}/entries", headers=headers)


# ---------- 목록·상세 ----------
def test_list_courses_from_db(db_client, me):
    items = data(db_client.get("/v1/courses", headers=me[0]))
    assert [(c["courseId"], c["name"], c["lengthM"]) for c in items] == COURSES
    assert all(c["type"] == "LOOP_3D" and c["runnersNow"] == 0 and c["thumbnailUrl"] is None for c in items)


def test_runners_now_counts_people_in_the_map(db_client, me, db):
    a, b, c, d, e, f = (add_user(db) for _ in range(6))
    add_run(db, a)
    add_run(db, a)                                   # 같은 사람 기록 2개 → 1명
    add_entry(db, a)                                 # 입장 + 달리는 중이어도 1명
    add_entry(db, b)                                 # 입장만 하고 아직 달리기 전 → 셈
    add_run(db, c, age=STALE)                        # 오래전 갱신(앱 꺼짐) → 안 셈
    add_entry(db, d, age=STALE)                      # 오래전 입장 → 안 셈
    add_run(db, e, status="PENDING")                 # 끝난 기록 → 안 셈
    add_entry(db, f, course_id="crs_island_tour")
    counts = {x["courseId"]: x["runnersNow"] for x in data(db_client.get("/v1/courses", headers=me[0]))}
    assert counts == {"crs_lake_loop": 2, "crs_coast_run": 0, "crs_forest_run": 0, "crs_island_tour": 1}
    assert data(db_client.get("/v1/courses/crs_lake_loop", headers=me[0]))["runnersNow"] == 2


def test_course_detail(db_client, me):
    d = data(db_client.get("/v1/courses/crs_coast_run", headers=me[0]))
    assert d["courseId"] == "crs_coast_run" and d["lengthM"] == 8776 and d["maxRunnersPerMap"] == 20
    assert d["description"] and d["mood"] and d["thumbnailUrl"] is None


def test_unknown_course_is_404(db_client, me):
    fail(db_client.get("/v1/courses/crs_x", headers=me[0]), 404, "COURSE_NOT_FOUND")
    fail(enter(db_client, me[0], "crs_x"), 404, "COURSE_NOT_FOUND")


@pytest.fixture
def hidden_course(db):
    """목록에서 숨긴 테스트 전용 코스(운영 코스 4개는 건드리지 않음)."""
    db.add(Course(id="crs_test_hidden", name="숨긴 코스", type="LOOP_3D", length_m=3000, active=False))
    db.commit()
    yield "crs_test_hidden"
    db.execute(delete(CourseEntry).where(CourseEntry.course_id == "crs_test_hidden"))
    db.execute(delete(Course).where(Course.id == "crs_test_hidden"))
    db.commit()


def test_hidden_course_is_gone(db_client, me, db, hidden_course):
    assert hidden_course not in [c["courseId"] for c in data(db_client.get("/v1/courses", headers=me[0]))]
    fail(db_client.get(f"/v1/courses/{hidden_course}", headers=me[0]), 404, "COURSE_NOT_FOUND")
    calibrate(db, me[1])
    fail(enter(db_client, me[0], hidden_course), 404, "COURSE_NOT_FOUND")


# ---------- 맵 입장 ----------
def test_entry_needs_calibration(db_client, me, db):
    fail(enter(db_client, me[0]), 409, "CALIBRATION_REQUIRED")
    assert db.scalar(select(CourseEntry).where(CourseEntry.user_id == me[1])) is None


def test_entry_takes_a_seat(db_client, me, db):
    calibrate(db, me[1])
    e = data(enter(db_client, me[0]))
    assert e["mapInstanceId"] == "map_crs_lake_loop" and e["courseId"] == "crs_lake_loop"
    assert 0 <= e["entryPositionM"] <= 6671 and e["realtime"] is None
    assert db.scalar(select(CourseEntry.course_id).where(CourseEntry.user_id == me[1])) == "crs_lake_loop"
    assert data(db_client.get("/v1/courses/crs_lake_loop", headers=me[0]))["runnersNow"] == 1


def test_entering_another_course_moves_the_seat(db_client, me, db):
    calibrate(db, me[1])
    data(enter(db_client, me[0]))
    data(enter(db_client, me[0], "crs_island_tour"))
    counts = {x["courseId"]: x["runnersNow"] for x in data(db_client.get("/v1/courses", headers=me[0]))}
    assert counts["crs_lake_loop"] == 0 and counts["crs_island_tour"] == 1


def test_21st_person_is_refused(db_client, me, db):
    calibrate(db, me[1])
    others = [add_user(db) for _ in range(20)]
    for uid in others[:10]:
        add_run(db, uid)                             # 달리는 중 10명
    for uid in others[10:19]:
        add_entry(db, uid)                           # 입장만 한 9명
    add_run(db, others[19], age=STALE)               # 오래된 기록은 안 셈
    add_run(db, me[1])                               # 내 기록은 나를 셀 때 빼고 셈
    data(enter(db_client, me[0]))                    # 나 말고 19명 → 들어감
    data(enter(db_client, me[0]))                    # 이미 들어온 사람이 다시 들어와도 됨

    add_entry(db, others[19])                        # 나 말고 20명
    fail(enter(db_client, me[0]), 409, "COURSE_FULL")
    data(enter(db_client, me[0], "crs_island_tour"))  # 다른 코스는 들어감


def test_full_check_comes_after_calibration(db_client, me, db):
    for _ in range(20):
        add_entry(db, add_user(db))
    fail(enter(db_client, me[0]), 409, "CALIBRATION_REQUIRED")


def test_concurrent_entries_never_exceed_max(db_client, db, monkeypatch):
    """19명이 있을 때 세 명이 동시에 들어오면 한 명만 들어간다(코스 줄 잠금)."""
    for _ in range(19):
        add_entry(db, add_user(db))
    newcomers = [signup(db_client, f"k_{30 + i}") for i in range(3)]
    for h in newcomers:
        calibrate(db, user_id_of(h))

    barrier = threading.Barrier(3, timeout=10)
    original_course, original_count = courses_router._active_course, occupancy.count_present

    def course_at_once(*args, **kwargs):             # 세 요청이 동시에 잠금을 시도하게
        if "lock" in kwargs:
            barrier.wait()
        return original_course(*args, **kwargs)

    def slow_count(*args, **kwargs):                 # 세고 나서 쓰기 전까지 틈을 벌림(잠금이 없으면 셋 다 19명을 봄)
        out = original_count(*args, **kwargs)
        time.sleep(0.3)
        return out

    monkeypatch.setattr(courses_router, "_active_course", course_at_once)
    monkeypatch.setattr(courses_router, "count_present", slow_count)
    results = run_together([lambda c, h=h: enter(c, h) for h in newcomers])
    assert sorted(r.status_code for r in results) == [201, 409, 409], [r.text for r in results]
    assert db.scalar(select(func.count()).select_from(CourseEntry).where(CourseEntry.course_id == "crs_lake_loop")) == 20


@pytest.mark.parametrize("method,path", [
    ("GET", "/v1/courses"), ("GET", "/v1/courses/crs_lake_loop"), ("POST", "/v1/courses/crs_lake_loop/entries")])
def test_needs_real_login(db_client, method, path):
    fail(db_client.request(method, path), 401, "UNAUTHORIZED")
    fail(db_client.request(method, path, headers=bearer("demo")), 401, "UNAUTHORIZED")
    first = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_9"}))
    fail(db_client.request(method, path, headers=bearer(first["signupToken"])), 403, "SIGNUP_REQUIRED")
