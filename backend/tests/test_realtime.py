"""실시간 같이 뛰기(계획 0029): 방 로직(Hub)과 WebSocket 진짜 흐름(테스트 DB)."""
import math

import pytest
from starlette.websockets import WebSocketDisconnect

from app.realtime import router as rt
from app.realtime.hub import Hub, hub
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
