"""달리는 쪽 runs API 진짜 흐름(테스트 DB, 계획 0037): 시작·시계열·끝나지 않은 러닝·종료·버리기. 실행 전 DB가 떠 있어야 함."""
from datetime import datetime, timedelta, timezone

import pytest
from sqlalchemy import select, update

from app.db.models import CourseEntry, Run, RunGhost, RunSample
from tests.helpers import data, fail, run_together, signup

LAKE = "map_crs_lake_loop"


def iso(dt: datetime) -> str:
    return dt.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def ago(**kw) -> datetime:
    return datetime.now(timezone.utc) - timedelta(**kw)


@pytest.fixture
def db(test_db):
    s = test_db.Session()
    yield s
    s.close()


def enter(client, db, headers, course="crs_lake_loop"):
    """보정을 올리고 맵에 들어감(시작은 마지막으로 들어간 맵이어야 함). 입장 시각은 1시간 전으로 돌려
    테스트가 지난 시각으로 시작할 수 있게(시작은 입장 뒤여야 함)."""
    r = client.put("/v1/users/me/calibration", headers=headers,
                   json={"points": [{"speedKmh": 8, "gait": "RUN", "durationS": 15}], "model": {"v": 1}, "device": "test"})
    assert r.status_code == 200, r.text
    assert client.post(f"/v1/courses/{course}/entries", headers=headers).status_code == 201
    db.execute(update(CourseEntry).values(entered_at=ago(hours=1)))
    db.commit()


@pytest.fixture
def me(db_client, db):
    headers = signup(db_client, "k_1")
    enter(db_client, db, headers)
    return headers


def start(client, headers, cid="c1", started=None, goal=None, **extra):
    body = {"clientRunId": cid, "mapInstanceId": LAKE, "goal": goal or {"type": "DISTANCE", "value": 3000},
            "startedAt": iso(started or ago(minutes=20)), **extra}
    return client.post("/v1/runs", json=body, headers=headers)


def started_id(client, headers, **kw) -> str:
    r = start(client, headers, **kw)
    assert r.status_code == 201, r.text
    return data(r)["runId"]


def finish(client, headers, run_id, ended=None, distance=3000, duration=1000):
    return client.post(f"/v1/runs/{run_id}/finish", headers=headers,
                       json={"endedAt": iso(ended or ago(minutes=1)), "sensor": {"distanceM": distance, "durationS": duration}})


def samples(client, headers, run_id, offset=0.0, speeds=(9.0,) * 60, interval=1.0):
    return client.put(f"/v1/runs/{run_id}/samples", headers=headers,
                      json={"offsetS": offset, "intervalS": interval, "speedsKmh": list(speeds)})


# ---------- 시작 ----------
def test_start_saves_active_run_with_my_share_default(db_client, me, db):
    db_client.patch("/v1/users/me/settings", headers=me, json={"feedShareDefault": False})
    started = ago(minutes=3)
    out = data(start(db_client, me, started=started))
    assert out["clientRunId"] == "c1"
    assert datetime.fromisoformat(out["startedAt"].replace("Z", "+00:00")) == started
    row = db.scalar(select(Run).where(Run.id == out["runId"]))
    assert (row.status, row.course_id, row.map_instance_id, row.goal_type, row.goal_value, row.shared) == \
           ("ACTIVE", "crs_lake_loop", LAKE, "DISTANCE", 3000, False)


def test_same_client_run_id_returns_first_run(db_client, me, db):
    first = data(start(db_client, me))
    again = data(start(db_client, me, started=ago(minutes=5)))  # 다시 보낸 요청은 값이 달라도 처음 것
    assert again == first
    assert len(db.scalars(select(Run)).all()) == 1


def test_same_client_run_id_at_once_makes_one_run(db_client, me, db):
    outs = run_together([lambda c: start(c, me)] * 4)
    assert all(r.status_code == 201 for r in outs), [r.text for r in outs]
    assert len({data(r)["runId"] for r in outs}) == 1
    assert len(db.scalars(select(Run)).all()) == 1


def test_client_run_id_is_per_user(db_client, me, db):
    other = signup(db_client, "k_2")
    enter(db_client, db, other)
    assert started_id(db_client, me) != started_id(db_client, other)


def test_start_needs_the_map_i_entered_last(db_client, me, db):
    enter(db_client, db, me, course="crs_coast_run")  # 다른 코스로 옮김
    r = start(db_client, me)
    fail(r, 400, "VALIDATION_ERROR")
    assert r.json()["error"]["details"]["fields"][0]["field"] == "mapInstanceId"


def test_start_without_entering_any_map(db_client):
    fail(start(db_client, signup(db_client, "k_3")), 400, "VALIDATION_ERROR")


@pytest.mark.parametrize("goal", [{"type": "DISTANCE"}, {"type": "TIME", "value": 0}, {"type": "NONE", "value": 600}])
def test_goal_value_must_match_type(db_client, me, goal):
    fail(start(db_client, me, goal=goal), 400, "VALIDATION_ERROR")


def test_goal_none_and_time(db_client, me, db):
    a = started_id(db_client, me, cid="a", goal={"type": "NONE"})
    b = started_id(db_client, me, cid="b", goal={"type": "TIME", "value": 1800})
    assert [(r.goal_type, r.goal_value) for r in db.scalars(select(Run).where(Run.id.in_([a, b])).order_by(Run.client_run_id))] == \
           [("NONE", None), ("TIME", 1800)]


def test_start_rejects_future_and_naive_times(db_client, me):
    fail(start(db_client, me, cid="f", started=datetime.now(timezone.utc) + timedelta(hours=1)), 400, "VALIDATION_ERROR")
    r = db_client.post("/v1/runs", headers=me, json={"clientRunId": "n", "mapInstanceId": LAKE, "goal": {"type": "NONE"},
                                                     "startedAt": "2026-11-06T10:00:00"})
    fail(r, 400, "VALIDATION_ERROR")


def test_ghost_must_be_my_finished_run(db_client, me, db):
    active = started_id(db_client, me, cid="g0")
    fail(start(db_client, me, cid="g1", ghostRunId=active), 400, "VALIDATION_ERROR")  # 아직 달리는 중
    finish(db_client, me, active)
    run_id = started_id(db_client, me, cid="g2", ghostRunId=active)
    assert db.scalar(select(RunGhost.ghost_run_id).where(RunGhost.run_id == run_id)) == active
    other = signup(db_client, "k_2")
    enter(db_client, db, other)
    fail(start(db_client, other, cid="g3", ghostRunId=active), 400, "VALIDATION_ERROR")  # 남의 기록


def test_runs_need_login(db_client):
    fail(db_client.post("/v1/runs", json={}), 401, "UNAUTHORIZED")
    fail(db_client.get("/v1/runs/active"), 401, "UNAUTHORIZED")


# ---------- 시계열 ----------
def test_samples_are_saved_and_resent_chunk_overwrites(db_client, me, db):
    run_id = started_id(db_client, me)
    assert samples(db_client, me, run_id, 0, [8.0] * 60).status_code == 204
    assert samples(db_client, me, run_id, 60, [9.0] * 60).status_code == 204
    assert samples(db_client, me, run_id, 60, [9.5] * 60).status_code == 204  # 같은 묶음 다시
    rows = db.execute(select(RunSample.offset_s, RunSample.speeds_kmh).where(RunSample.run_id == run_id)
                      .order_by(RunSample.offset_s)).all()
    assert [(o, s[0], len(s)) for o, s in rows] == [(0.0, 8.0, 60), (60.0, 9.5, 60)]


@pytest.mark.parametrize("bad", [
    {"intervalS": 1.0, "speedsKmh": []},
    {"intervalS": 1.0, "speedsKmh": [8.0, 31.0]},
    {"intervalS": 1.0, "speedsKmh": [-1.0]},
    {"intervalS": 0, "speedsKmh": [8.0]},
    {"intervalS": 1.0, "speedsKmh": [8.0], "offsetS": -1},
])
def test_samples_validation(db_client, me, bad):
    run_id = started_id(db_client, me)
    fail(db_client.put(f"/v1/runs/{run_id}/samples", headers=me, json=bad), 400, "VALIDATION_ERROR")


def test_samples_on_finished_or_discarded_run(db_client, me):
    done = started_id(db_client, me, cid="a")
    finish(db_client, me, done)
    fail(samples(db_client, me, done), 409, "RUN_ALREADY_FINISHED")
    gone = started_id(db_client, me, cid="b")
    db_client.post(f"/v1/runs/{gone}/discard", headers=me)
    fail(samples(db_client, me, gone), 409, "RUN_ALREADY_FINISHED")


def test_someone_elses_run_is_forbidden_and_unknown_is_not_found(db_client, me):
    run_id = started_id(db_client, me)
    other = signup(db_client, "k_2")
    fail(samples(db_client, other, run_id), 403, "FORBIDDEN")
    fail(finish(db_client, other, run_id), 403, "FORBIDDEN")
    fail(db_client.post(f"/v1/runs/{run_id}/discard", headers=other), 403, "FORBIDDEN")
    fail(samples(db_client, me, "01NOPE"), 404, "RUN_NOT_FOUND")
    fail(finish(db_client, me, "01NOPE"), 404, "RUN_NOT_FOUND")


# ---------- 끝나지 않은 러닝 ----------
def test_active_is_null_without_runs(db_client, me):
    assert data(db_client.get("/v1/runs/active", headers=me)) == {"run": None, "recoverableUntil": None, "lastActiveAt": None}


def test_active_shows_latest_unfinished_run_with_samples_so_far(db_client, me):
    started_id(db_client, me, cid="old", started=ago(minutes=50))
    run_id = started_id(db_client, me, cid="new", started=ago(minutes=10))
    samples(db_client, me, run_id, 0, [9.0] * 60)
    samples(db_client, me, run_id, 60, [10.8] * 60)
    out = data(db_client.get("/v1/runs/active", headers=me))
    run = out["run"]
    assert run["runId"] == run_id and run["verification"] == "PENDING" and run["courseName"] == "노을 호수 한 바퀴"
    assert (run["distanceM"], run["durationS"]) == (330, 120)  # 9 km/h 60초 150m + 10.8 km/h 60초 180m
    until = datetime.fromisoformat(out["recoverableUntil"].replace("Z", "+00:00"))
    assert timedelta(hours=23, minutes=59) < until - datetime.now(timezone.utc) <= timedelta(hours=24)
    last = datetime.fromisoformat(out["lastActiveAt"].replace("Z", "+00:00"))
    assert until - last == timedelta(hours=24), "마지막 갱신 시각(앱이 폰 기록 없이 끝낼 때 종료 시각, 계획 0052)"


def test_active_forgets_runs_untouched_for_24_hours(db_client, me, db):
    run_id = started_id(db_client, me)
    db.execute(update(Run).where(Run.id == run_id).values(updated_at=ago(hours=24, minutes=1)))
    db.commit()
    assert data(db_client.get("/v1/runs/active", headers=me))["run"] is None


def test_samples_extend_recovery_window(db_client, me, db):
    run_id = started_id(db_client, me)
    db.execute(update(Run).where(Run.id == run_id).values(updated_at=ago(hours=23)))
    db.commit()
    samples(db_client, me, run_id)
    db.expire_all()
    assert db.scalar(select(Run.updated_at).where(Run.id == run_id)) > ago(minutes=1)


def test_active_is_only_mine_and_skips_finished(db_client, me, db):
    finish(db_client, me, started_id(db_client, me, cid="done"))
    other = signup(db_client, "k_2")
    enter(db_client, db, other)
    started_id(db_client, other)
    assert data(db_client.get("/v1/runs/active", headers=me))["run"] is None


# ---------- 종료 ----------
def test_finish_moves_to_pending_with_sensor_values(db_client, me, db):
    run_id = started_id(db_client, me, started=ago(minutes=20))
    out = data(finish(db_client, me, run_id, distance=3120, duration=1080))
    assert (out["runId"], out["verification"], out["distanceM"], out["durationS"], out["avgPaceSPerKm"]) == \
           (run_id, "PENDING", 3120, 1080, 346)
    assert out["courseId"] == "crs_lake_loop" and out["shared"] is True
    row = db.scalar(select(Run).where(Run.id == run_id))
    assert (row.status, row.sensor_distance_m, row.official_source, row.official_distance_m) == ("PENDING", 3120, "SENSOR", 3120)


def test_finish_resent_with_same_values_returns_same_result(db_client, me):
    run_id = started_id(db_client, me)
    ended = ago(minutes=1)
    first = data(finish(db_client, me, run_id, ended=ended))
    assert data(finish(db_client, me, run_id, ended=ended)) == first
    fail(finish(db_client, me, run_id, ended=ended, distance=9999), 409, "RUN_ALREADY_FINISHED")


def test_finish_with_zero_distance_has_no_pace(db_client, me):
    assert data(finish(db_client, me, started_id(db_client, me), distance=0, duration=30))["avgPaceSPerKm"] is None


def test_finish_time_checks(db_client, me):
    run_id = started_id(db_client, me, started=ago(minutes=10))
    fail(finish(db_client, me, run_id, ended=ago(minutes=11)), 400, "VALIDATION_ERROR")       # 시작보다 앞
    fail(finish(db_client, me, run_id, ended=ago(seconds=-3600)), 400, "VALIDATION_ERROR")    # 미래
    fail(finish(db_client, me, run_id, ended=ago(minutes=5), duration=600), 400, "VALIDATION_ERROR")  # 5분 동안 10분 달림


def test_finish_after_discard(db_client, me):
    run_id = started_id(db_client, me)
    db_client.post(f"/v1/runs/{run_id}/discard", headers=me)
    fail(finish(db_client, me, run_id), 409, "RUN_ALREADY_FINISHED")


# ---------- 버리기 ----------
def test_discard_active_run_twice_is_fine(db_client, me, db):
    run_id = started_id(db_client, me)
    assert db_client.post(f"/v1/runs/{run_id}/discard", headers=me).status_code == 204
    assert db_client.post(f"/v1/runs/{run_id}/discard", headers=me).status_code == 204
    assert db.scalar(select(Run.status).where(Run.id == run_id)) == "DISCARDED"
    assert data(db_client.get("/v1/runs/active", headers=me))["run"] is None


def test_finished_run_cannot_be_discarded(db_client, me):
    run_id = started_id(db_client, me)
    finish(db_client, me, run_id)
    fail(db_client.post(f"/v1/runs/{run_id}/discard", headers=me), 409, "RUN_ALREADY_FINISHED")


# ---------- 리뷰 반영(11/16): 입장 전 시작, 평균 속도 상한, 복구 기한 지난 러닝, 가장 최근 러닝만, 겹친 묶음 ----------
def test_start_before_map_entry_is_rejected(db_client, me):
    r = start(db_client, me, started=ago(hours=2))  # 입장은 1시간 전
    fail(r, 400, "VALIDATION_ERROR")
    assert r.json()["error"]["details"]["fields"][0]["field"] == "startedAt"


def test_finish_rejects_impossible_average_speed(db_client, me):
    run_id = started_id(db_client, me)
    fail(finish(db_client, me, run_id, distance=100000, duration=600), 400, "VALIDATION_ERROR")
    assert finish(db_client, me, run_id, distance=5000, duration=600).status_code == 200  # 30 km/h 그대로는 됨


def test_expired_run_takes_no_samples_or_finish(db_client, me, db):
    run_id = started_id(db_client, me)
    db.execute(update(Run).where(Run.id == run_id).values(updated_at=ago(hours=24, minutes=1)))
    db.commit()
    fail(samples(db_client, me, run_id), 409, "RUN_ALREADY_FINISHED")
    fail(finish(db_client, me, run_id), 409, "RUN_ALREADY_FINISHED")
    db.expire_all()
    assert db.scalar(select(Run.updated_at).where(Run.id == run_id)) < ago(hours=24)  # 되살아나지 않음


def test_active_only_asks_about_the_latest_run(db_client, me):
    old = started_id(db_client, me, cid="a", started=ago(minutes=50))  # 꺼진 러닝 A
    new = started_id(db_client, me, cid="b", started=ago(minutes=10))  # 복구를 안 고르고 새로 B
    assert data(db_client.get("/v1/runs/active", headers=me))["run"]["runId"] == new
    db_client.post(f"/v1/runs/{new}/discard", headers=me)
    assert data(db_client.get("/v1/runs/active", headers=me))["run"] is None  # A를 다시 묻지 않음
    assert samples(db_client, me, old).status_code == 204  # A 자체는 남아 있음(서버가 몰래 버리지 않음)


def test_active_counts_overlapping_chunks_once(db_client, me):
    run_id = started_id(db_client, me)
    samples(db_client, me, run_id, 0, [9.0] * 60)
    samples(db_client, me, run_id, 30, [9.0] * 90)  # 30초부터 다시 나눠 보냄(30–60초가 겹침)
    run = data(db_client.get("/v1/runs/active", headers=me))["run"]
    assert (run["distanceM"], run["durationS"]) == (300, 120)  # 9 km/h 120초
