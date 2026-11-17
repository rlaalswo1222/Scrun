"""기록을 읽는 runs API(테스트 DB, 계획 0043): 목록·상세·공유·고스트, 함께 달린 사람. 실행 전 DB가 떠 있어야 함."""
from datetime import datetime, timedelta, timezone

import pytest
from sqlalchemy import select, update

from app.core.config import BotPolicy
from app.db.ids import new_id
from app.db.models import Friendship, Run, RunCompanion, RunGhost, User
from app.realtime.hub import COMPANIONS_MAX, TRACK_KEEP_S, Hub, hub
from tests.helpers import data, fail, signup
from tests.test_runs import LAKE, ago, db, enter, finish, iso, me, samples, start, started_id  # noqa: F401 (픽스처)

LAKE_M = 6670.0


def my_id(client, headers) -> str:
    return data(client.get("/v1/users/me", headers=headers))["userId"]


def add_run(db, user_id, started, status="PENDING", distance=3000, duration=1000, **kw) -> str:
    """끝난 기록을 DB에 바로 넣음(목록 쪽수·달 경계처럼 많이·옛 날짜가 필요할 때)."""
    run_id = new_id()
    db.add(Run(id=run_id, user_id=user_id, client_run_id=run_id, course_id="crs_lake_loop", map_instance_id=LAKE,
               goal_type="NONE", status=status, started_at=started, ended_at=started + timedelta(seconds=duration),
               sensor_distance_m=distance, sensor_duration_s=duration, official_source="SENSOR",
               official_distance_m=distance, official_duration_s=duration,
               verification_deadline=datetime.now(timezone.utc) + timedelta(hours=1), **kw))
    db.commit()
    return run_id


def finished_id(client, headers, cid="c1", started=None, **kw) -> str:
    run_id = started_id(client, headers, cid=cid, started=started)
    r = finish(client, headers, run_id, **kw)
    assert r.status_code == 200, r.text
    return run_id


def runs(client, headers, **params):
    r = client.get("/v1/runs", headers=headers, params=params)
    assert r.status_code == 200, r.text
    return data(r)


# ---------- 목록 ----------
def test_list_is_empty_without_runs(db_client, me):
    assert runs(db_client, me) == {"items": [], "nextCursor": None}


def test_list_shows_only_finished_runs_newest_first(db_client, me, db):
    old = finished_id(db_client, me, cid="old", distance=2000, duration=800)
    started_id(db_client, me, cid="active", started=ago(minutes=5))
    gone = started_id(db_client, me, cid="gone", started=ago(minutes=4))
    assert db_client.post(f"/v1/runs/{gone}/discard", headers=me).status_code == 204
    uid = my_id(db_client, me)
    new = add_run(db, uid, ago(minutes=2), status="VERIFIED", distance=3120, duration=1080)
    out = runs(db_client, me)
    assert [i["runId"] for i in out["items"]] == [new, old]
    first = out["items"][0]
    assert (first["verification"], first["distanceM"], first["durationS"], first["avgPaceSPerKm"], first["courseName"],
            first["shared"]) == ("VERIFIED", 3120, 1080, 346, "노을 호수 한 바퀴", True)
    assert out["items"][1]["verification"] == "PENDING"


def test_list_pages_with_cursor_including_same_start_time(db_client, me, db):
    uid = my_id(db_client, me)
    base = ago(days=1)
    ids = [add_run(db, uid, base - timedelta(minutes=i // 2)) for i in range(23)]  # 두 개씩 같은 시작 시각
    first = runs(db_client, me)
    assert len(first["items"]) == 20 and first["nextCursor"]
    second = runs(db_client, me, cursor=first["nextCursor"])
    assert len(second["items"]) == 3 and second["nextCursor"] is None
    seen = [i["runId"] for i in first["items"] + second["items"]]
    assert sorted(seen) == sorted(ids) and len(set(seen)) == 23, "겹치거나 빠진 기록 없음"


def test_list_month_is_in_my_timezone(db_client, me, db):
    uid = my_id(db_client, me)  # 가입 기본 시간대 Asia/Seoul
    nov_kst = add_run(db, uid, datetime(2026, 10, 31, 15, 30, tzinfo=timezone.utc))  # 11/01 00:30 KST
    oct_kst = add_run(db, uid, datetime(2026, 10, 31, 14, 30, tzinfo=timezone.utc))  # 10/31 23:30 KST
    assert [i["runId"] for i in runs(db_client, me, month="2026-11")["items"]] == [nov_kst]
    assert [i["runId"] for i in runs(db_client, me, month="2026-10")["items"]] == [oct_kst]
    db.execute(update(User).where(User.id == uid).values(timezone="UTC"))
    db.commit()
    assert {i["runId"] for i in runs(db_client, me, month="2026-10")["items"]} == {nov_kst, oct_kst}
    dec = add_run(db, uid, datetime(2026, 12, 31, 23, 0, tzinfo=timezone.utc))
    assert [i["runId"] for i in runs(db_client, me, month="2026-12")["items"]] == [dec], "12월 → 다음 해 1월 경계"


@pytest.mark.parametrize("params", [{"month": "2026-13"}, {"month": "2026-1"}, {"month": "0000-05"}, {"month": "9999-12"},
                                    {"cursor": "!!"}, {"cursor": "bm9wZQ"}])
def test_list_rejects_bad_month_and_cursor(db_client, me, params):
    fail(db_client.get("/v1/runs", headers=me, params=params), 400, "VALIDATION_ERROR")


def test_list_closes_expired_pending_runs(db_client, me, db):
    uid = my_id(db_client, me)
    run_id = add_run(db, uid, ago(hours=2))
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(minutes=1)))
    db.commit()
    assert runs(db_client, me)["items"][0]["verification"] == "UNVERIFIED"
    db.expire_all()
    assert db.scalar(select(Run.unverified_reason).where(Run.id == run_id)) == "TIMEOUT"


def test_list_is_only_mine(db_client, me, db):
    other = signup(db_client, "k_2")
    add_run(db, my_id(db_client, other), ago(hours=1))
    assert runs(db_client, me)["items"] == []


def test_read_apis_need_login(db_client):
    for path in ("/v1/runs", "/v1/runs/x", "/v1/runs/x/ghost"):
        assert db_client.get(path).status_code == 401
    assert db_client.patch("/v1/runs/x", json={"shared": False}).status_code == 401


# ---------- 상세 ----------
def test_detail_has_sensor_values_and_graph_series(db_client, me, db):
    run_id = started_id(db_client, me, started=ago(minutes=30))
    for k in range(17):  # 1초 × 60칸 묶음 17개 = 1020초
        assert samples(db_client, me, run_id, offset=k * 60.0, speeds=[9.0] * 60).status_code == 204
    assert samples(db_client, me, run_id, offset=30.0, speeds=[1.0] * 30).status_code == 204  # 앞 묶음 안에 든 묶음(덮인 칸은 안 씀)
    assert finish(db_client, me, run_id, distance=2550, duration=1020).status_code == 200
    out = data(db_client.get(f"/v1/runs/{run_id}", headers=me))
    assert (out["officialSource"], out["sensor"], out["console"], out["verification"]) == \
           ("SENSOR", {"distanceM": 2550, "durationS": 1020}, None, "PENDING")
    series = out["speedSeries"]
    assert series["intervalS"] == 9.0 and len(series["speedsKmh"]) == 114, "1020초를 120칸 안으로(9초 칸)"
    assert set(series["speedsKmh"]) == {9.0}
    assert out["companions"] == [] and out["ghostRunId"] is None


def test_detail_shows_console_values_and_ghost(db_client, me, db):
    ghost = finished_id(db_client, me, cid="g", started=ago(minutes=50), ended=ago(minutes=30))
    run_id = started_id(db_client, me, cid="r", ghostRunId=ghost, started=ago(minutes=20))
    assert finish(db_client, me, run_id).status_code == 200
    db.execute(update(Run).where(Run.id == run_id).values(
        status="VERIFIED", console_distance_m=3120, console_duration_s=1000, official_source="CONSOLE",
        official_distance_m=3120))
    db.commit()
    out = data(db_client.get(f"/v1/runs/{run_id}", headers=me))
    assert (out["verification"], out["officialSource"], out["distanceM"], out["console"], out["ghostRunId"]) == \
           ("VERIFIED", "CONSOLE", 3120, {"distanceM": 3120, "durationS": 1000}, ghost)
    assert out["speedSeries"] == {"intervalS": 1.0, "speedsKmh": []}, "시계열이 안 올라온 기록"


def test_detail_fills_missing_chunks_with_previous_speed(db_client, me):
    run_id = started_id(db_client, me, started=ago(minutes=30))
    samples(db_client, me, run_id, offset=0.0, speeds=[8.0] * 60)
    samples(db_client, me, run_id, offset=120.0, speeds=[10.0] * 60)  # 60–120초 묶음이 빠짐
    finish(db_client, me, run_id, distance=500, duration=180)
    series = data(db_client.get(f"/v1/runs/{run_id}/ghost", headers=me))["speedSeries"]
    assert series["intervalS"] == 1.0 and len(series["speedsKmh"]) == 180
    assert series["speedsKmh"][59:61] == [8.0, 8.0] and series["speedsKmh"][119:121] == [8.0, 10.0]


def test_read_apis_hide_unfinished_and_others(db_client, me, db):
    active = started_id(db_client, me, cid="a")
    other = signup(db_client, "k_2")
    theirs = add_run(db, my_id(db_client, other), ago(hours=1))
    for path in ("", "/ghost"):
        fail(db_client.get(f"/v1/runs/{active}{path}", headers=me), 404, "RUN_NOT_FOUND")
        fail(db_client.get(f"/v1/runs/nope{path}", headers=me), 404, "RUN_NOT_FOUND")
        fail(db_client.get(f"/v1/runs/{theirs}{path}", headers=me), 403, "FORBIDDEN")
    fail(db_client.patch(f"/v1/runs/{active}", headers=me, json={"shared": False}), 404, "RUN_NOT_FOUND")
    fail(db_client.patch(f"/v1/runs/{theirs}", headers=me, json={"shared": False}), 403, "FORBIDDEN")


def test_detail_closes_expired_pending(db_client, me, db):
    run_id = finished_id(db_client, me)
    db.execute(update(Run).where(Run.id == run_id).values(verification_deadline=ago(seconds=1)))
    db.commit()
    assert data(db_client.get(f"/v1/runs/{run_id}", headers=me))["verification"] == "UNVERIFIED"


# ---------- 공유 ----------
def test_patch_changes_only_share(db_client, me, db):
    run_id = finished_id(db_client, me)
    out = data(db_client.patch(f"/v1/runs/{run_id}", headers=me, json={"shared": False}))
    assert (out["runId"], out["shared"], out["distanceM"]) == (run_id, False, 3000)
    assert runs(db_client, me)["items"][0]["shared"] is False
    assert data(db_client.patch(f"/v1/runs/{run_id}", headers=me, json={"shared": True}))["shared"] is True
    fail(db_client.patch(f"/v1/runs/{run_id}", headers=me, json={}), 400, "VALIDATION_ERROR")


# ---------- 고스트 ----------
def test_ghost_returns_full_series_with_official_values(db_client, me):
    run_id = started_id(db_client, me, started=ago(minutes=30))
    samples(db_client, me, run_id, offset=0.0, speeds=[9.0] * 60 + [10.0] * 60)
    finish(db_client, me, run_id, distance=320, duration=120)
    out = data(db_client.get(f"/v1/runs/{run_id}/ghost", headers=me))
    assert (out["runId"], out["distanceM"], out["durationS"]) == (run_id, 320, 120)
    assert out["speedSeries"]["speedsKmh"] == [9.0] * 60 + [10.0] * 60


def test_series_cells_are_whole_seconds(db_client, me):
    run_id = started_id(db_client, me, started=ago(minutes=30))
    samples(db_client, me, run_id, offset=0.0, interval=0.5, speeds=[8.0, 10.0] * 60)  # 0.5초 칸 120개 = 60초
    finish(db_client, me, run_id, distance=150, duration=60)
    series = data(db_client.get(f"/v1/runs/{run_id}/ghost", headers=me))["speedSeries"]
    assert series == {"intervalS": 1.0, "speedsKmh": [9.0] * 60}, "1초보다 짧은 칸은 1초로 평균"


# ---------- 함께 달린 사람(API + 실시간 방) ----------
def in_room(uid, nick, running=True, d=0.0):
    conn, _ = hub.join(LAKE, "crs_lake_loop", LAKE_M, uid, nick)
    assert hub.update(conn, d, 9.0 if running else 0.0, running)
    return conn


def test_companions_are_saved_at_finish_and_marked_friend_when_read(db_client, me, db):
    other = signup(db_client, "k_2")
    stander = signup(db_client, "k_3")
    me_id, other_id, stander_id = my_id(db_client, me), my_id(db_client, other), my_id(db_client, stander)
    run_id = started_id(db_client, me)
    in_room(other_id, "러너2")
    in_room(stander_id, "러너3", running=False)   # 서 있기만 한 사람은 같이 달린 게 아님
    mine = in_room(me_id, "러너1")
    hub.update(mine, 10.0, 9.0, True)
    assert finish(db_client, me, run_id).status_code == 200
    rows = db.execute(select(RunCompanion.kind, RunCompanion.companion_user_id)
                      .where(RunCompanion.run_id == run_id)).all()
    assert rows == [("RUNNER", other_id)]
    assert data(db_client.get(f"/v1/runs/{run_id}", headers=me))["companions"] == [{"nickname": "러너2", "kind": "RUNNER"}]
    a, b = sorted([me_id, other_id])
    db.add(Friendship(user_a_id=a, user_b_id=b))
    db.commit()
    assert data(db_client.get(f"/v1/runs/{run_id}", headers=me))["companions"] == [{"nickname": "러너2", "kind": "FRIEND"}]


def test_companions_start_fresh_each_run(db_client, me, db):
    other = signup(db_client, "k_2")
    me_id, other_id = my_id(db_client, me), my_id(db_client, other)
    first = started_id(db_client, me, cid="1", started=ago(minutes=40))
    in_room(other_id, "러너2")
    in_room(me_id, "러너1")
    hub.leave(hub.join(LAKE, "crs_lake_loop", LAKE_M, other_id, "러너2")[0], intentional=True)
    second = started_id(db_client, me, cid="2", started=ago(minutes=20))  # 첫 러닝을 안 끝내고 새로 시작
    assert finish(db_client, me, second).status_code == 200
    assert data(db_client.get(f"/v1/runs/{second}", headers=me))["companions"] == []
    assert finish(db_client, me, first, ended=ago(minutes=30), duration=500).status_code == 200
    assert data(db_client.get(f"/v1/runs/{first}", headers=me))["companions"] == [], "앞 러닝 것은 새 시작 때 버림"


def test_discard_stops_collecting(db_client, me, db):
    other = signup(db_client, "k_2")
    me_id, other_id = my_id(db_client, me), my_id(db_client, other)
    run_id = started_id(db_client, me)
    in_room(other_id, "러너2")
    in_room(me_id, "러너1")
    assert hub.companions(me_id, run_id)
    assert db_client.post(f"/v1/runs/{run_id}/discard", headers=me).status_code == 204
    assert hub.companions(me_id, run_id) == []


def test_companion_who_left_the_service_is_dropped(db_client, me, db):
    other = signup(db_client, "k_2")
    me_id, other_id = my_id(db_client, me), my_id(db_client, other)
    run_id = started_id(db_client, me)
    in_room(other_id, "러너2")
    in_room(me_id, "러너1")
    db.execute(User.__table__.delete().where(User.id == other_id))
    db.commit()
    assert finish(db_client, me, run_id).status_code == 200
    assert data(db_client.get(f"/v1/runs/{run_id}", headers=me))["companions"] == []


# ---------- 함께 달린 사람(실시간 방만) ----------
class FakeClock:
    def __init__(self):
        self.t = 1000.0

    def __call__(self):
        return self.t


def join(h, uid, nick=None):
    return h.join(LAKE, "crs_lake_loop", LAKE_M, uid, nick or uid)[0]


def test_hub_collects_running_people_and_bots_while_i_run():
    h = Hub(clock=FakeClock(), grace_s=30, bots=BotPolicy(fill_to=3, paces_kmh=(9.0, 6.0)))
    h.start_tracking("me", "r1")
    me_conn = join(h, "me", "나")
    h.update(me_conn, 0.0, 0.0, False)              # 나는 아직 서 있음 → 안 모음
    b = join(h, "b", "비")
    h.update(b, 5.0, 9.0, True)                      # b가 달리기 시작 → 봇 하나(사람 2 + 봇 1 = 3, 9 km/h)
    assert h.companions("me", "r1") == []
    h.update(me_conn, 1.0, 9.0, True)
    h.update(me_conn, 2.0, 9.0, True)                # 같은 사람은 한 번만
    got = h.companions("me", "r1")
    assert [(c.kind, c.user_id, c.label) for c in got] == [("RUNNER", "b", "비"), ("BOT", None, "페이스 6:40")]
    assert h.companions("me", "other-run") == [], "다른 러닝이면 빈 목록"
    h.stop_tracking("me", "other-run")
    assert h.companions("me", "r1"), "다른 러닝 ID로는 안 지워짐"
    h.stop_tracking("me", "r1")
    assert h.companions("me", "r1") == []


def test_hub_skips_disconnected_runners():
    h = Hub(clock=FakeClock(), grace_s=30)
    h.start_tracking("me", "r1")
    b = join(h, "b")
    h.update(b, 5.0, 9.0, True)
    h.leave(b)                                        # 끊김: 30초 동안 방에 남지만 같이 달린 게 아님
    me_conn = join(h, "me")
    h.update(me_conn, 0.0, 9.0, True)
    assert h.companions("me", "r1") == []


def test_hub_keeps_companions_across_reconnect_and_caps_people():
    h = Hub(clock=FakeClock(), grace_s=0)
    h.start_tracking("me", "r1")
    for i in range(COMPANIONS_MAX + 3):
        h.update(join(h, f"u{i}"), float(i), 9.0, True)
    me_conn = join(h, "me")
    h.update(me_conn, 0.0, 9.0, True)
    h.leave(me_conn)                                  # 끊겨 방에서 빠져도(grace 0) 모은 건 남음
    h.update(join(h, "me"), 0.0, 9.0, True)
    got = h.companions("me", "r1")
    assert len(got) == COMPANIONS_MAX and got[0].user_id == "u0"


def test_hub_new_start_replaces_and_old_abandoned_tracks_are_dropped():
    clock = FakeClock()
    h = Hub(clock=clock)
    h.start_tracking("a", "r1")
    h.start_tracking("b", "r9")
    h.start_tracking("a", "r2")                       # 앞 러닝(r1)은 버림
    assert h.companions("a", "r1") == []
    clock.t += TRACK_KEEP_S + 1
    h.start_tracking("c", "r5")                       # 오래 버려진 b 것은 이때 지움
    assert set(h._tracks) == {"c"}


def test_hub_without_tracking_collects_nothing():
    h = Hub(clock=FakeClock())
    h.update(join(h, "a"), 0.0, 9.0, True)
    h.update(join(h, "b"), 0.0, 9.0, True)
    assert h.companions("b", "r1") == []


# ---------- 시각은 UTC(Z) ----------
KST = timezone(timedelta(hours=9))


def test_times_go_out_in_utc_even_if_sent_with_offset(db_client, me):
    """앱이 +09:00으로 보내도 응답은 Z"""
    when = ago(minutes=20).astimezone(KST)
    r = db_client.post("/v1/runs", headers=me, json={
        "clientRunId": "c1", "mapInstanceId": LAKE, "goal": {"type": "DISTANCE", "value": 3000},
        "startedAt": when.isoformat()})
    assert r.status_code == 201, r.text
    assert data(r)["startedAt"] == iso(when)


def test_times_read_from_db_go_out_in_utc_whatever_the_connection_timezone(db_client, me, test_db):
    """서버 DB 연결 시간대가 Asia/Seoul이어도(읽은 값에 +09:00이 붙음) 응답은 Z"""
    from sqlalchemy import event

    run_id = finished_id(db_client, me)

    def seoul(dbapi_conn, _record):
        with dbapi_conn.cursor() as cur:
            cur.execute("SET TIME ZONE 'Asia/Seoul'")

    test_db.engine.dispose()
    event.listen(test_db.engine, "connect", seoul)
    try:
        listed = runs(db_client, me)["items"][0]["startedAt"]
        detail = data(db_client.get(f"/v1/runs/{run_id}", headers=me))["startedAt"]
    finally:
        event.remove(test_db.engine, "connect", seoul)
        test_db.engine.dispose()
    assert listed.endswith("Z") and detail == listed
