"""친구 API(테스트 DB, 계획 0044): 코드로 추가·목록·달리는 친구·프로필·삭제·피드. 실행 전 DB가 떠 있어야 함."""
from datetime import datetime, timedelta, timezone

import pytest
from sqlalchemy import func, select, update

from app.core.attempts import friend_code_failures
from app.core.config import settings
from app.db.ids import new_id
from app.db.models import Friendship, Run, User
from app.realtime.hub import hub
from tests.helpers import data, fail, run_together, signup
from tests.test_run_records import add_run, my_id
from tests.test_runs import ago, db  # noqa: F401 (픽스처)

LAKE_M = 6670.0


def code_of(client, headers) -> str:
    return data(client.get("/v1/users/me", headers=headers))["friendCode"]


def add(client, headers, code):
    return client.post("/v1/friends", headers=headers, json={"friendCode": code})


def befriend(client, a, b) -> dict:
    r = add(client, a, code_of(client, b))
    assert r.status_code == 201, r.text
    return data(r)


def friends(client, headers) -> list:
    return data(client.get("/v1/friends", headers=headers))


def feed(client, headers, **params) -> dict:
    r = client.get("/v1/feed", headers=headers, params=params)
    assert r.status_code == 200, r.text
    return data(r)


def fake_users(db, n) -> list[str]:
    ids = []
    for _ in range(n):
        uid = new_id()
        db.add(User(id=uid, nickname=f"u{uid[-10:]}", height_cm=170, weight_kg=60, friend_code=uid[-8:]))
        ids.append(uid)
    db.commit()
    return ids


def link(db, me, others) -> None:
    for o in others:
        db.add(Friendship(user_a_id=min(me, o), user_b_id=max(me, o)))
    db.commit()


@pytest.fixture
def two(db_client):
    """가입한 두 사람(헤더)."""
    return signup(db_client, "k_1"), signup(db_client, "k_2")


# ---------- 추가 ----------
def test_code_is_8_chars_and_adding_makes_both_friends(db_client, two):
    a, b = two
    code = code_of(db_client, b)
    assert len(code) == 8
    got = data(add(db_client, a, f" {code[:4].lower()}-{code[4:]} "))  # 소문자·하이픈·공백 무시
    assert got["userId"] == my_id(db_client, b) and got["nickname"] == "러너2"
    assert (got["level"], got["verifiedCount"], got["runningNow"], got["runningDistanceM"], got["lastRunAt"]) \
        == (None, 0, False, None, None)
    assert [f["nickname"] for f in friends(db_client, a)] == ["러너2"]
    assert [f["nickname"] for f in friends(db_client, b)] == ["러너1"], "요청·수락 없이 서로 바로(정할 것 2)"


def test_add_errors(db_client, two):
    a, b = two
    fail(add(db_client, a, code_of(db_client, a)), 400, "SELF_FRIEND_CODE")
    fail(add(db_client, a, "ZZZZZZZZ"), 404, "FRIEND_CODE_NOT_FOUND")
    fail(add(db_client, a, "SCR-7K2Q"), 404, "FRIEND_CODE_NOT_FOUND")  # 옛 모양
    fail(add(db_client, a, "O0I1O0I1"), 404, "FRIEND_CODE_NOT_FOUND")  # 안 쓰는 글자
    fail(db_client.post("/v1/friends", headers=a, json={"friendCode": ""}), 400, "VALIDATION_ERROR")
    befriend(db_client, a, b)
    fail(add(db_client, a, code_of(db_client, b)), 409, "ALREADY_FRIEND")
    fail(add(db_client, b, code_of(db_client, a)), 409, "ALREADY_FRIEND")


@pytest.mark.parametrize("full,who", [("me", "ME"), ("them", "FRIEND")])
def test_limit_50_on_either_side(db_client, db, two, full, who):
    a, b = two
    uid = my_id(db_client, a if full == "me" else b)
    link(db, uid, fake_users(db, settings.friends.max_friends))
    r = add(db_client, a, code_of(db_client, b))
    fail(r, 409, "FRIEND_LIMIT_EXCEEDED")
    assert r.json()["error"]["details"] == {"who": who}


def test_concurrent_adds_never_pass_50(db_client, db):
    me = signup(db_client, "k_1")
    uid = my_id(db_client, me)
    link(db, uid, fake_users(db, settings.friends.max_friends - 1))
    codes = [code_of(db_client, signup(db_client, f"k_{i}")) for i in (2, 3)]
    rs = run_together([lambda c, code=code: add(c, me, code) for code in codes])
    assert sorted(r.status_code for r in rs) == [201, 409], [r.text for r in rs]
    n = db.scalar(select(func.count()).select_from(Friendship)
                  .where((Friendship.user_a_id == uid) | (Friendship.user_b_id == uid)))
    assert n == settings.friends.max_friends


def test_wrong_codes_are_rate_limited(db_client, two, monkeypatch):
    a, b = two
    now = [1000.0]
    monkeypatch.setattr(friend_code_failures, "_clock", lambda: now[0])
    good = code_of(db_client, b)
    fail(add(db_client, a, code_of(db_client, a)), 400, "SELF_FRIEND_CODE")  # 내 코드는 틀린 코드로 안 셈
    for _ in range(settings.friends.add_fail_limit):
        fail(add(db_client, a, "ZZZZZZZZ"), 404, "FRIEND_CODE_NOT_FOUND")
    fail(add(db_client, a, good), 429, "RATE_LIMITED")  # 맞는 코드도 막음
    assert add(db_client, b, code_of(db_client, a)).status_code == 201, "다른 사람은 안 막힘"
    now[0] += settings.friends.add_fail_window_s
    fail(add(db_client, a, good), 409, "ALREADY_FRIEND")  # 10분 지나면 다시 시도할 수 있음


def test_concurrent_wrong_codes_cannot_pass_the_limit(db_client):
    me = signup(db_client, "k_1")
    n = settings.friends.add_fail_limit * 2
    rs = run_together([lambda c: add(c, me, "ZZZZZZZZ")] * n)
    codes = sorted(r.status_code for r in rs)
    assert codes.count(404) == settings.friends.add_fail_limit and codes.count(429) == n - settings.friends.add_fail_limit, codes


# ---------- 목록·달리는 친구 ----------
def test_list_order_counts_and_last_run(db_client, db):
    me = signup(db_client, "k_1")
    hs = {k: signup(db_client, f"k_{k}") for k in (2, 3, 4)}
    for h in hs.values():
        befriend(db_client, me, h)
    u2, u3, u4 = (my_id(db_client, hs[k]) for k in (2, 3, 4))
    add_run(db, u2, ago(days=2), status="VERIFIED")
    add_run(db, u2, ago(days=1), status="VERIFIED")
    add_run(db, u2, ago(hours=1), status="UNVERIFIED", shared=False)  # 공유 안 한 건 마지막 러닝에 안 셈
    add_run(db, u3, ago(hours=3), status="PENDING")
    add_run(db, u3, ago(minutes=30), status="ACTIVE")  # 달리는 중은 기록 아님
    conn, _ = hub.join("map_crs_lake_loop", "crs_lake_loop", LAKE_M, u4, "러너4")
    hub.update(conn, 100.0, 9.0, True, run=1234.4)

    rows = friends(db_client, me)
    assert [r["nickname"] for r in rows] == ["러너4", "러너3", "러너2"], "달리는 친구 먼저, 그다음 최근에 달린 순"
    r4, r3, r2 = rows
    assert (r4["runningNow"], r4["runningDistanceM"], r4["lastRunAt"]) == (True, 1234, None)
    assert r3["verifiedCount"] == 0 and r3["runningNow"] is False and r3["runningDistanceM"] is None
    assert r2["verifiedCount"] == 2
    assert datetime.fromisoformat(r2["lastRunAt"].replace("Z", "+00:00")) < datetime.fromisoformat(
        r3["lastRunAt"].replace("Z", "+00:00"))


def test_list_is_empty_without_friends(db_client):
    assert friends(db_client, signup(db_client, "k_1")) == []


def test_running_friends_from_realtime_room(db_client, two):
    a, b = two
    assert data(db_client.get("/v1/friends/running", headers=a)) == []
    befriend(db_client, a, b)
    stranger = signup(db_client, "k_3")
    conn_b, _ = hub.join("map_crs_lake_loop", "crs_lake_loop", LAKE_M, my_id(db_client, b), "러너2")
    conn_s, _ = hub.join("map_crs_lake_loop", "crs_lake_loop", LAKE_M, my_id(db_client, stranger), "러너3")
    hub.update(conn_s, 10.0, 8.0, True, run=500.0)  # 친구 아님
    hub.update(conn_b, 10.0, 0.0, False)
    assert data(db_client.get("/v1/friends/running", headers=a)) == [], "서 있는 친구는 안 나옴"
    hub.update(conn_b, 20.0, 10.0, True, run=3200.0)
    assert data(db_client.get("/v1/friends/running", headers=a)) == [{
        "userId": my_id(db_client, b), "nickname": "러너2", "courseId": "crs_lake_loop", "courseName": "노을 호수 한 바퀴",
        "distanceM": 3200, "paceSPerKm": 360}]
    hub.leave(conn_b, intentional=True)
    assert data(db_client.get("/v1/friends/running", headers=a)) == []


# ---------- 프로필·삭제 ----------
def test_profile_shows_recent_shared_runs(db_client, db, two):
    a, b = two
    befriend(db_client, a, b)
    ub = my_id(db_client, b)
    ids = [add_run(db, ub, ago(days=20 - i), status="VERIFIED") for i in range(11)]
    hidden = add_run(db, ub, ago(hours=2), status="VERIFIED", shared=False)
    late = add_run(db, ub, ago(hours=1), status="PENDING")
    db.execute(update(Run).where(Run.id == late).values(verification_deadline=ago(minutes=1)))  # 마감 지남
    db.commit()
    got = data(db_client.get(f"/v1/friends/{ub}", headers=a))
    assert got["nickname"] == "러너2" and got["outfitStage"] == 1 and got["level"] is None
    assert got["verifiedCount"] == 11, "공유 안 한 인증 기록은 마크 수에도 안 셈"
    runs = got["recentRuns"]
    assert [r["runId"] for r in runs] == [late] + ids[::-1][:9], "최신순 10개, 공유 안 한 건 빠짐"
    assert hidden not in [r["runId"] for r in runs]
    assert runs[0]["verification"] == "UNVERIFIED", "마감 지난 대기는 미인증으로"
    assert (runs[1]["courseName"], runs[1]["distanceM"], runs[1]["avgPaceSPerKm"]) == ("노을 호수 한 바퀴", 3000, 333)


def test_profile_and_delete_need_friendship(db_client, two):
    a, b = two
    ub = my_id(db_client, b)
    fail(db_client.get(f"/v1/friends/{ub}", headers=a), 404, "USER_NOT_FOUND")
    fail(db_client.get(f"/v1/friends/{my_id(db_client, a)}", headers=a), 404, "USER_NOT_FOUND")
    fail(db_client.get("/v1/friends/nope", headers=a), 404, "USER_NOT_FOUND")
    fail(db_client.delete(f"/v1/friends/{ub}", headers=a), 404, "USER_NOT_FOUND")


def test_delete_removes_both_sides(db_client, two):
    a, b = two
    befriend(db_client, a, b)
    assert db_client.delete(f"/v1/friends/{my_id(db_client, a)}", headers=b).status_code == 204
    assert friends(db_client, a) == [] and friends(db_client, b) == []
    fail(db_client.delete(f"/v1/friends/{my_id(db_client, b)}", headers=a), 404, "USER_NOT_FOUND")
    befriend(db_client, a, b)  # 코드로 다시 맺을 수 있음


# ---------- 피드 ----------
def test_feed_shows_only_friends_shared_finished_runs(db_client, db, two):
    a, b = two
    stranger = signup(db_client, "k_3")
    ua, ub, us = my_id(db_client, a), my_id(db_client, b), my_id(db_client, stranger)
    assert feed(db_client, a) == {"items": [], "nextCursor": None}
    befriend(db_client, a, b)
    v = add_run(db, ub, ago(hours=5), status="VERIFIED", distance=5000, duration=1620)
    u = add_run(db, ub, ago(hours=4), status="UNVERIFIED")
    p = add_run(db, ub, ago(hours=3), status="PENDING")
    add_run(db, ub, ago(hours=2), status="VERIFIED", shared=False)
    add_run(db, ub, ago(hours=1), status="ACTIVE")
    add_run(db, ub, ago(hours=1), status="DISCARDED")
    add_run(db, ua, ago(hours=1), status="VERIFIED")  # 내 기록은 안 나옴
    add_run(db, us, ago(hours=1), status="VERIFIED")  # 친구 아님
    items = feed(db_client, a)["items"]
    assert [(i["runId"], i["verification"]) for i in items] == [(p, "PENDING"), (u, "UNVERIFIED"), (v, "VERIFIED")]
    first = items[-1]
    assert (first["feedId"], first["userId"], first["nickname"], first["distanceM"], first["durationS"],
            first["avgPaceSPerKm"], first["courseName"]) == (v, ub, "러너2", 5000, 1620, 324, "노을 호수 한 바퀴")
    assert first["endedAt"] and first["startedAt"]
    db_client.delete(f"/v1/friends/{ub}", headers=a)
    assert feed(db_client, a)["items"] == [], "친구를 끊으면 피드에서도 빠짐"


def test_feed_pages_by_end_time_with_ties(db_client, db, two):
    a, b = two
    befriend(db_client, a, b)
    ub = my_id(db_client, b)
    base = datetime.now(timezone.utc) - timedelta(days=1)
    ids = [add_run(db, ub, base - timedelta(minutes=i // 2), duration=600) for i in range(23)]  # 두 개씩 같은 종료 시각
    first = feed(db_client, a)
    assert len(first["items"]) == 20 and first["nextCursor"]
    second = feed(db_client, a, cursor=first["nextCursor"])
    assert len(second["items"]) == 3 and second["nextCursor"] is None
    seen = [i["runId"] for i in first["items"] + second["items"]]
    assert sorted(seen) == sorted(ids) and len(set(seen)) == 23
    fail(db_client.get("/v1/feed", headers=a, params={"cursor": "!!"}), 400, "VALIDATION_ERROR")
