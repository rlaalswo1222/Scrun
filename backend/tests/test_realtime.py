"""실시간 같이 뛰기(계획 0029): 방 로직(Hub)과 WebSocket 진짜 흐름(테스트 DB)."""
import math
import random
from datetime import timedelta

import pytest
from starlette.websockets import WebSocketDisconnect

from app.db.models import CourseEntry, UserProgress
from app.realtime import router as rt
from app.core.config import BotPolicy
from app.realtime.hub import Hub, hub, lap_position, pace_name
from tests.helpers import data, signup
from tests.test_courses import calibrate, db, enter, user_id_of  # noqa: F401 (db 픽스처)

LAKE, LAKE_M = "map_crs_lake_loop", 6671


# ---------- 방 로직 ----------
class FakeClock:
    def __init__(self):
        self.t = 1000.0

    def __call__(self):
        return self.t


@pytest.fixture
def h():
    clock = FakeClock()
    hub_ = Hub(clock=clock, grace_s=30)
    hub_.clock = clock
    return hub_


def join(h, uid, map_id=LAKE, course_id="crs_lake_loop"):
    return h.join(map_id, course_id, LAKE_M, uid, f"닉{uid}")


def placed(h, uid, d=0.0, v=0.0, running=False, **kw):
    conn, w = join(h, uid, **kw)
    assert h.update(conn, d, v, running)
    return conn, w


def test_runner_is_hidden_until_first_state(h):
    a, wa = placed(h, "a", 10.0)
    assert wa["type"] == "welcome" and wa["you"] == "a" and wa["runners"] == [] and wa["courseLengthM"] == LAKE_M
    b, wb = join(h, "b")                     # 섬을 불러오는 중(아직 state 없음)
    assert [r["userId"] for r in wb["runners"]] == ["a"] and wb["runners"][0]["nickname"] == "닉a"
    assert h.drain(a) == [] and h.states(a)["runners"] == [], "첫 state 전엔 안 보임(시작점 0m에 나타나지 않게)"
    assert h.update(b, 4000.0, 9.0, False)
    assert h.drain(a) == [{"type": "joined", "runner": {"userId": "b", "nickname": "닉b", "d": 4000.0, "v": 9.0,
                                                         "running": False}}]
    assert h.drain(b) == []
    h.update(b, 4001.0, 9.0, False)
    assert h.drain(a) == [], "joined는 한 번만"


def test_state_is_wrapped_clamped_and_hidden_from_self(h):
    a, _ = placed(h, "a")
    b, _ = placed(h, "b")
    assert h.update(a, LAKE_M + 10.5, 45.0, True)
    assert h.states(b)["runners"] == [{"userId": "a", "d": 10.5, "v": 30.0, "running": True}]
    assert [r["userId"] for r in h.states(a)["runners"]] == ["b"]
    assert not h.update(a, math.nan, 1.0, True) and not h.update(a, 1.0, "9", True) and not h.update(a, True, 1.0, True)


def test_reconnect_within_grace_is_silent(h):
    a, _ = placed(h, "a")
    b, _ = placed(h, "b")
    h.drain(a)
    h.leave(b)
    h.clock.t += 29
    assert h.present_user_ids() == {"a", "b"}
    b2, wb = join(h, "b")
    assert h.drain(a) == [] and [r["userId"] for r in wb["runners"]] == ["a"]
    h.clock.t += 60
    assert h.states(a)["runners"][0]["userId"] == "b"  # 다시 붙었으니 안 빠짐


def test_grace_over_means_left(h):
    a, _ = placed(h, "a")
    b, _ = placed(h, "b")
    h.drain(a)
    h.leave(b)
    h.clock.t += 30
    assert h.states(a)["runners"] == []
    assert h.drain(a) == [{"type": "left", "userId": "b"}]
    assert h.present_user_ids() == {"a"}


def test_leaving_on_purpose_is_immediate(h):
    a, _ = placed(h, "a")
    b, _ = placed(h, "b", 100.0, 10.0, True)
    h.drain(a)
    h.leave(b, intentional=True)
    assert h.drain(a) == [{"type": "left", "userId": "b"}] and h.present_user_ids() == {"a"}


def test_leave_reports_whether_it_removed(h):
    a1, _ = placed(h, "a")
    a2, _ = join(h, "a")                         # a1은 새 연결로 바뀜
    assert h.leave(a1, intentional=True) is False  # 앞 연결의 leave는 새 연결을 안 뺌(자리도 안 지움)
    assert h.present_user_ids() == {"a"}
    assert h.leave(a2) is False                  # 끊김은 30초 남김
    a3, _ = join(h, "a")
    assert h.leave(a3, intentional=True) is True and h.present_user_ids() == set()


def test_unplaced_runner_leaves_silently(h):
    a, _ = placed(h, "a")
    b, _ = join(h, "b")
    h.leave(b, intentional=True)
    assert h.drain(a) == []


def test_second_connection_replaces_first(h):
    a1, _ = placed(h, "a")
    b, _ = placed(h, "b")
    h.drain(b)
    a2, _ = join(h, "a")
    assert a1.replaced and not a2.replaced
    assert h.drain(b) == []  # 같은 사람이니 joined·left 없음
    h.leave(a1)               # 앞 연결이 늦게 닫혀도 새 연결은 그대로
    assert h.update(a2, 5.0, 8.0, True) and not h.update(a1, 6.0, 8.0, True)


def test_moving_to_another_map_leaves_the_old_room(h):
    a, _ = placed(h, "a")
    b, _ = placed(h, "b")
    h.drain(b)
    join(h, "a", "map_crs_island_tour", "crs_island_tour")
    assert a.replaced and h.drain(b) == [{"type": "left", "userId": "a"}]
    assert h.states(a) is None
    assert h.present_user_ids() == {"a", "b"}


def test_wake_is_called_for_events(h):
    woken = []
    a, _ = h.join(LAKE, "crs_lake_loop", LAKE_M, "a", "a", wake=lambda: woken.append(1))
    h.update(a, 0.0, 0.0, False)
    placed(h, "b")
    assert woken == [1]


def test_running_positions_for_entry(h):
    placed(h, "a", 100.0, 9.0, True)
    placed(h, "b", 200.0, 0.0, False)
    c, _ = placed(h, "c", 300.0, 9.0, True)
    join(h, "d")
    h.leave(c)                                       # 끊김(곧 빠질 수 있음) → 뺌
    assert h.running_positions("crs_lake_loop") == [100.0]
    assert h.running_positions("crs_lake_loop", except_user="a") == []



def test_run_distance_is_passed_on(h):
    a, _ = placed(h, "a")
    b, _ = placed(h, "b")
    assert h.update(a, 10.0, 9.0, True, 2400.04)
    assert h.states(b)["runners"] == [{"userId": "a", "d": 10.0, "v": 9.0, "running": True, "run": 2400.0}]
    assert h.update(a, 11.0, 9.0, True, "많이") and h.update(a, 11.0, 9.0, True, math.nan)
    assert "run" not in h.states(b)["runners"][0], "숫자가 아니면 모름(연결은 안 끊음)"
    assert h.update(a, 11.0, 9.0, True, -5) and h.states(b)["runners"][0]["run"] == 0.0
    assert h.update(a, 11.0, 9.0, True, 10 ** 400) and "run" not in h.states(b)["runners"][0]
    assert not h.update(a, 10 ** 400, 9.0, True), "d가 아주 큰 정수여도 예외 없이 거부"

# ---------- 페이스 봇(계획 0040) ----------
PACES = (9.0, 7.5, 10.5, 6.0, 12.0)


@pytest.fixture
def hb():
    clock = FakeClock()
    hub_ = Hub(clock=clock, grace_s=30, bots=BotPolicy(fill_to=6, paces_kmh=PACES), rng=random.Random(1))
    hub_.clock = clock
    return hub_


def bots_of(msgs):
    return [m["runner"] for m in msgs if m["type"] == "joined" and m["runner"].get("bot")]


def test_bots_appear_when_someone_starts_running(hb):
    a, wa = join(hb, "a")
    assert wa["runners"] == [] and hb.states(a)["runners"] == [], "섬에 나타나기 전엔 봇도 없음(놓을 기준이 없음)"
    hb.update(a, 1000.0, 0.0, False)
    assert hb.drain(a) == [], "러닝 설정·카운트다운 중(서 있음)엔 아직 없음: 그 사이 봇이 멀리 가 버리지 않게"
    hb.clock.t += 90
    hb.update(a, 1000.0, 9.0, True)
    bots = bots_of(hb.drain(a))
    assert [b["userId"] for b in bots] == ["bot_1", "bot_2", "bot_3", "bot_4", "bot_5"]
    assert [b["nickname"] for b in bots] == ["페이스 6:40", "페이스 8:00", "페이스 5:43", "페이스 10:00", "페이스 5:00"]
    # 느린 봇은 앞, 빠른 봇은 뒤(가운데 9 km/h는 25m 앞)
    assert [b["d"] for b in bots] == [1025.0, 1085.0, 965.0, 1145.0, 905.0]
    assert all(b["running"] is True and b["bot"] is True for b in bots)
    assert len(hb.states(a)["runners"]) == 5


def test_bots_are_in_the_same_place_for_everyone(hb):
    a, _ = placed(hb, "a", 1000.0, 9.0, True)
    b, wb = placed(hb, "b", 3000.0, 9.0, True)
    assert [r["userId"] for r in wb["runners"] if r.get("bot")] == ["bot_1", "bot_2", "bot_3", "bot_5"]
    hb.clock.t += 10
    bots_a = [r for r in hb.states(a)["runners"] if r.get("bot")]
    bots_b = [r for r in hb.states(b)["runners"] if r.get("bot")]
    assert bots_a == bots_b
    assert bots_a[0]["d"] == round(1025.0 + 9.0 / 3.6 * 10, 2) and bots_a[0]["v"] == 9.0


def test_bot_position_wraps_around_the_lap(hb):
    a, _ = placed(hb, "a", LAKE_M - 10.0, 9.0, True)
    first = [r for r in hb.states(a)["runners"] if r["userId"] == "bot_1"][0]
    assert first["d"] == 15.0  # 6661 + 25 → 바퀴를 넘어 15m
    hb.clock.t += 3600 * 2  # 2시간 = 18 km
    first = [r for r in hb.states(a)["runners"] if r["userId"] == "bot_1"][0]
    assert 0.0 <= first["d"] < LAKE_M


def test_a_bot_makes_room_for_a_person_and_comes_back(hb):
    a, _ = placed(hb, "a", 1000.0, 9.0, True)
    hb.drain(a)
    b, wb = join(hb, "b")  # 섬을 불러오는 중이어도 자리는 차지
    assert hb.drain(a) == [{"type": "left", "userId": "bot_4"}], "사람에게서 가장 먼 봇(1145m)부터 빠짐"
    assert hb.drain(b) == [] and [r["userId"] for r in wb["runners"]] == ["a", "bot_1", "bot_2", "bot_3", "bot_5"]
    hb.leave(b, intentional=True)
    back = hb.drain(a)
    assert [m["type"] for m in back] == ["joined"] and back[0]["runner"]["userId"] == "bot_4"


def test_the_bot_beside_you_stays(hb):
    a, _ = placed(hb, "a", 1000.0, 9.0, True)
    hb.update(a, 905.0, 12.0, True)  # bot_5(12 km/h) 옆에서 같이 뜀
    hb.drain(a)
    join(hb, "b")
    assert hb.drain(a) == [{"type": "left", "userId": "bot_4"}], "옆의 bot_5가 아니라 가장 먼 bot_4가 빠짐"


def test_a_disconnected_runner_is_not_an_anchor(hb):
    a, _ = placed(hb, "a", 1000.0, 9.0, True)
    c, _ = placed(hb, "c", 4000.0, 9.0, True)
    hb.leave(c)                      # 끊김: 30초 동안 4000m에 멈춰 남음
    b, _ = join(hb, "b")
    hb.drain(a)
    hb.leave(b, intentional=True)    # 봇 하나가 다시 들어옴 → 붙어 있는 a 근처에
    back = bots_of(hb.drain(a))
    assert len(back) == 1 and abs(back[0]["d"] - 1000.0) < 200.0, back


def test_lap_position_stays_below_the_lap():
    assert lap_position(6670.996, 6671.0) == 0.0 and lap_position(6671.0 + 5.0, 6671.0) == 5.0
    assert lap_position(-1.0, 6671.0) == 6670.0 and lap_position(5.0, 0.0) == 0.0


def test_six_people_means_no_bots(hb):
    conns = [placed(hb, f"u{i}", i * 100.0, 9.0, True)[0] for i in range(6)]
    assert [r for r in hb.states(conns[0])["runners"] if r.get("bot")] == []
    hb.leave(conns[5], intentional=True)
    assert [r["userId"] for r in hb.states(conns[0])["runners"] if r.get("bot")] == ["bot_1"]


def test_bots_are_not_people(hb):
    a, _ = placed(hb, "a", 100.0, 9.0, True)
    assert hb.present_user_ids() == {"a"}, "인원(지금 N명)엔 사람만"
    assert hb.running_positions("crs_lake_loop", except_user="a") == [], "입장 위치는 사람만 기준"


def test_bots_go_with_the_room(hb):
    a, _ = placed(hb, "a", 100.0, 9.0, True)
    hb.leave(a, intentional=True)
    a2, w = join(hb, "a")
    assert w["runners"] == [] and hb.states(a2)["runners"] == []


def test_no_bots_when_fill_is_zero(h):
    a, w = placed(h, "a", 100.0, 9.0, True)
    assert h.drain(a) == [] and h.states(a)["runners"] == []


def test_pace_name():
    assert pace_name(9.0) == "페이스 6:40" and pace_name(12.0) == "페이스 5:00" and pace_name(10.5) == "페이스 5:43"


# ---------- WebSocket ----------
@pytest.fixture
def runner(db_client, db):  # noqa: F811
    """가입 + 보정 + 입장 → (Authorization 헤더, 입장 응답)."""

    def make(kakao):
        headers = signup(db_client, kakao)
        calibrate(db, user_id_of(headers))
        return headers, data(enter(db_client, headers))
    return make


def connect(client, entry):
    return client.websocket_connect("/v1/realtime",
                                    headers={"Authorization": f"Bearer {entry['realtime']['token']}"})


def recv(ws, kind, limit=20):
    """kind가 올 때까지 읽음(그 사이 states는 건너뜀)."""
    for _ in range(limit):
        msg = ws.receive_json()
        if msg["type"] == kind:
            return msg
    raise AssertionError(f"{kind}가 안 옴")


def closed_with(ws) -> int:
    with pytest.raises(WebSocketDisconnect) as e:
        for _ in range(20):
            ws.receive_json()
    return e.value.code


def state(ws, d=0.0, v=0.0, running=False):
    ws.send_json({"type": "state", "d": d, "v": v, "running": running})


def test_bad_or_missing_token_is_closed(db_client, runner):
    with db_client.websocket_connect("/v1/realtime", headers={"Authorization": "Bearer nope"}) as ws:
        assert closed_with(ws) == rt.CLOSE_UNAUTHORIZED
    with db_client.websocket_connect("/v1/realtime") as ws:
        assert closed_with(ws) == rt.CLOSE_UNAUTHORIZED
    _, e = runner("k_1")
    with db_client.websocket_connect(f"/v1/realtime?token={e['realtime']['token']}") as ws:  # 주소의 토큰은 안 받음
        assert closed_with(ws) == rt.CLOSE_UNAUTHORIZED


def test_token_for_a_map_you_left_is_closed(db_client, runner):
    headers, lake = runner("k_1")
    data(enter(db_client, headers, "crs_island_tour"))  # 자리를 옮김
    with connect(db_client, lake) as ws:
        assert closed_with(ws) == rt.CLOSE_NOT_IN_MAP


def test_two_runners_see_each_other(db_client, runner):
    _, ea = runner("k_1")
    _, eb = runner("k_2")
    with connect(db_client, ea) as wa:
        w = recv(wa, "welcome")
        assert w["runners"] == [] and w["courseLengthM"] == LAKE_M and w["mapInstanceId"] == LAKE
        state(wa, 50.0)
        with connect(db_client, eb) as wb:
            wb_welcome = recv(wb, "welcome")
            assert [r["nickname"] for r in wb_welcome["runners"]] == ["러너1"]
            state(wb, 30.0)
            joined = recv(wa, "joined")["runner"]
            assert joined["nickname"] == "러너2" and joined["d"] == 30.0

            state(wa, 123.4, 9.5, True)
            for _ in range(20):
                s = recv(wb, "states")
                if s["runners"] and s["runners"][0]["d"] == 123.4:
                    break
            else:
                raise AssertionError("A 상태가 B에게 안 감")
            assert s["runners"] == [{"userId": w["you"], "d": 123.4, "v": 9.5, "running": True}]

            wb.send_json({"type": "ping", "t": 42})
            pong = recv(wb, "pong")
            assert pong["t"] == 42 and pong["serverTime"] > 0
        # B가 끊겨도 30초 동안은 left 없음 → 바로 다시 붙으면 그대로 이어짐
        with connect(db_client, eb) as wb:
            assert [r["nickname"] for r in recv(wb, "welcome")["runners"]] == ["러너1"]
        assert data(db_client.get("/v1/courses/crs_lake_loop", headers=signup(db_client, "k_9")))["runnersNow"] == 2


def test_runners_carry_level(db_client, db, runner):
    """이름표·카드 Lv 배지(계획 0059): 입장 때 경험치로 계산한 레벨이 welcome·joined에 실린다."""
    ha, ea = runner("k_1")
    _, eb = runner("k_2")
    db.query(UserProgress).filter(UserProgress.user_id == user_id_of(ha)).update({UserProgress.xp_m: 4400})  # Lv.2
    db.commit()
    with connect(db_client, ea) as wa:
        recv(wa, "welcome")
        state(wa, 50.0)
        with connect(db_client, eb) as wb:
            assert [(r["nickname"], r["level"]) for r in recv(wb, "welcome")["runners"]] == [("러너1", 2)]
            state(wb, 30.0)
            joined = recv(wa, "joined")["runner"]
            assert (joined["nickname"], joined["level"]) == ("러너2", 1), "기록 없는 새 사람은 Lv.1"


def test_bots_have_no_level():
    h = Hub(bots=BotPolicy(fill_to=3, paces_kmh=(8.0, 9.0, 10.0)))
    conn, _ = h.join("map_x", "crs_x", 1000.0, "ua", "A", level=3)
    h.update(conn, 10.0, 9.0, True)   # 봇은 누가 달릴 때 들어옴
    _, w2 = h.join("map_x", "crs_x", 1000.0, "ub", "B", level=1)
    bots = [r for r in w2["runners"] if r.get("bot")]
    people = [r for r in w2["runners"] if not r.get("bot")]
    assert bots and all("level" not in r for r in bots), "봇은 레벨 없음"
    assert [(r["userId"], r["level"]) for r in people] == [("ua", 3)]


def test_leave_message_is_immediate(db_client, runner):
    _, ea = runner("k_1")
    _, eb = runner("k_2")
    with connect(db_client, ea) as wa:
        recv(wa, "welcome")
        with connect(db_client, eb) as wb:
            you = recv(wb, "welcome")["you"]
            state(wb)
            recv(wa, "joined")
            wb.send_json({"type": "leave"})
            assert closed_with(wb) == rt.CLOSE_LEFT
        assert recv(wa, "left")["userId"] == you


def test_leave_frees_the_seat_right_away(db_client, runner, db):  # noqa: F811
    """leave로 나가면 "지금 N명"에서 바로 빠짐(입장 2분이 안 지났어도)."""
    hb, eb = runner("k_2")
    viewer = signup(db_client, "k_9")
    count = lambda: data(db_client.get("/v1/courses/crs_lake_loop", headers=viewer))["runnersNow"]  # noqa: E731
    with connect(db_client, eb) as wb:
        recv(wb, "welcome")
        state(wb)
        assert count() == 1
        wb.send_json({"type": "leave"})
        assert closed_with(wb) == rt.CLOSE_LEFT
    assert count() == 0
    assert db.get(CourseEntry, user_id_of(hb)) is None


def test_freeing_a_seat_keeps_a_newer_entry(db_client, runner, db, test_db):  # noqa: F811
    """나간 사이 다시 입장했으면(같은 코스라도) 새 자리라 늦게 온 지우기가 안 지움."""
    headers, _ = runner("k_2")
    uid = user_id_of(headers)
    old_at = db.get(CourseEntry, uid).entered_at
    db.expire_all()
    db.execute(CourseEntry.__table__.update().where(CourseEntry.user_id == uid)
               .values(entered_at=old_at + timedelta(seconds=5)))  # 다시 입장(입장 시각이 바뀜)
    db.commit()
    rt._free_seat(test_db.Session, uid, "crs_lake_loop", old_at)
    db.expire_all()
    assert db.get(CourseEntry, uid) is not None
    rt._free_seat(test_db.Session, uid, "crs_island_tour", old_at + timedelta(seconds=5))  # 다른 코스 자리도 안 지움
    db.expire_all()
    assert db.get(CourseEntry, uid) is not None


def test_left_after_grace(db_client, runner, monkeypatch):
    monkeypatch.setattr(hub, "_grace_s", 0)
    _, ea = runner("k_1")
    _, eb = runner("k_2")
    with connect(db_client, ea) as wa:
        recv(wa, "welcome")
        with connect(db_client, eb) as wb:
            you = recv(wb, "welcome")["you"]
            state(wb)
            recv(wa, "joined")
        assert recv(wa, "left")["userId"] == you


def test_second_connection_closes_the_first(db_client, runner):
    _, ea = runner("k_1")
    with connect(db_client, ea) as first:
        recv(first, "welcome")
        with connect(db_client, ea) as second:
            recv(second, "welcome")
            assert closed_with(first) == rt.CLOSE_REPLACED


@pytest.mark.parametrize("bad", ["not json", "[1]", '{"type":"state","d":"x","v":1}', "x" * 2000, b"\x00binary"])
def test_bad_message_closes(db_client, runner, bad):
    _, ea = runner("k_1")
    with connect(db_client, ea) as ws:
        recv(ws, "welcome")
        if isinstance(bad, bytes):
            ws.send_bytes(bad)
        else:
            ws.send_text(bad)
        assert closed_with(ws) == rt.CLOSE_BAD_MESSAGE


def test_unknown_message_is_ignored(db_client, runner):
    _, ea = runner("k_1")
    with connect(db_client, ea) as ws:
        recv(ws, "welcome")
        ws.send_json({"type": "emoji", "x": 1})
        ws.send_json({"type": "ping", "t": 1})
        assert recv(ws, "pong")["t"] == 1


def test_entry_lands_behind_a_running_runner(db_client, runner):
    _, ea = runner("k_1")
    with connect(db_client, ea) as wa:
        recv(wa, "welcome")
        state(wa, 10.0, 9.0, True)
        wa.send_json({"type": "ping", "t": 1})
        recv(wa, "pong")  # 앞의 state가 처리됨(메시지는 순서대로)
        for i in range(5):
            _, eb = runner(f"k_{2 + i}")
            behind = (10.0 - eb["entryPositionM"]) % LAKE_M
            assert 20.0 <= behind <= 40.0, eb["entryPositionM"]


def test_bots_over_websocket(db_client, runner, monkeypatch):
    monkeypatch.setattr(hub, "_bots", BotPolicy(fill_to=2, paces_kmh=(9.0,)))
    _, ea = runner("k_1")
    with connect(db_client, ea) as wa:
        assert recv(wa, "welcome")["runners"] == []
        state(wa, 100.0)
        state(wa, 100.0, 9.0, True)  # 달리기 시작 → 봇
        bot = recv(wa, "joined")["runner"]
        assert bot["bot"] is True and bot["nickname"] == "페이스 6:40" and bot["userId"] == "bot_1"
        s = recv(wa, "states")
        assert [r["userId"] for r in s["runners"]] == ["bot_1"] and s["runners"][0]["bot"] is True
        assert data(db_client.get("/v1/courses/crs_lake_loop", headers=signup(db_client, "k_9")))["runnersNow"] == 1
