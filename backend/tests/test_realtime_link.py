"""허브 내부 API와 원격 허브(계획 0073, app/realtime/internal.py·link.py). 허브를 거치는 흐름은 test_link_*.py가
기존 테스트를 원격 허브로 한 번 더 돌린다."""
import pytest
from fastapi.testclient import TestClient

from app.core.config import ConfigError, load_settings, settings
from app.main import app
from app.realtime.hub import Companion, RunningNow, hub
from app.realtime.link import INTERNAL_HEADER, RemoteHub, internal_token

TOKEN = internal_token(settings.jwt_secret)
LAKE, LAKE_M = "map_crs_lake_loop", 6670.0


@pytest.fixture
def fresh_hub():
    hub.reset()
    yield hub
    hub.reset()


def remote(token=TOKEN) -> RemoteHub:
    return RemoteHub("http://testserver", token, client=TestClient(app))


def test_remote_answers_like_the_hub(fresh_hub):
    a, _ = hub.join(LAKE, "crs_lake_loop", LAKE_M, "U_A", "러너A")
    b, _ = hub.join(LAKE, "crs_lake_loop", LAKE_M, "U_B", "러너B")
    hub.start_tracking("U_A", "run_1")
    assert hub.update(b, 100.0, 9.0, True, 1200.0) and hub.update(a, 90.0, 8.0, True)
    r = remote()
    assert r.present_user_ids() == hub.present_user_ids() == {"U_A", "U_B"}
    assert r.running_positions("crs_lake_loop", except_user="U_A") == hub.running_positions("crs_lake_loop", "U_A") == [100.0]
    assert r.in_map("U_B") == hub.in_map("U_B") == ("crs_lake_loop", 100.0)
    assert r.in_map("U_nobody") is None
    assert r.running_now(["U_B", "U_nobody"]) == {"U_B": RunningNow(course_id="crs_lake_loop", run_m=1200.0, kmh=9.0)}
    assert r.companions("U_A", "run_1") == hub.companions("U_A", "run_1") == [Companion("RUNNER", "U_B", "러너B")]
    r.stop_tracking("U_A", "run_1")
    assert hub.companions("U_A", "run_1") == []
    r.start_tracking("U_A", "run_2")
    assert hub.companions("U_A", "run_2") == []        # 새 러닝은 비어서 시작
    assert hub.update(a, 95.0, 8.0, True)
    assert r.companions("U_A", "run_2") == [Companion("RUNNER", "U_B", "러너B")]
    r.forget_user("U_B")
    assert hub.present_user_ids() == {"U_A"}


def test_wrong_secret_is_404_and_remote_falls_back(fresh_hub):
    hub.join(LAKE, "crs_lake_loop", LAKE_M, "U_A", "러너A")
    c = TestClient(app)
    assert c.post("/internal/hub/present_user_ids", json={}).status_code == 404
    assert c.post("/internal/hub/present_user_ids", json={}, headers={INTERNAL_HEADER: "x" * 64}).status_code == 404
    assert c.post("/internal/hub/nope", json={}, headers={INTERNAL_HEADER: TOKEN}).status_code == 404
    assert c.post("/internal/hub/in_map", json={}, headers={INTERNAL_HEADER: TOKEN}).status_code == 400
    assert remote("wrong").present_user_ids() == set()   # 허브 없이 진행
    assert "/internal/hub/{op}" not in app.openapi()["paths"]


def test_unreachable_realtime_does_not_break_callers():
    r = RemoteHub("http://127.0.0.1:9", TOKEN, timeout_s=0.5)   # 아무도 안 듣는 곳
    assert r.present_user_ids() == set()
    assert r.running_positions("crs_lake_loop") == []
    assert r.in_map("U_A") is None
    assert r.running_now(["U_A"]) == {}
    assert r.companions("U_A", "run_1") == []
    r.start_tracking("U_A", "run_1")
    r.stop_tracking("U_A", "run_1")
    r.forget_user("U_A")


@pytest.mark.parametrize("url", ["realtime:8000", "ftp://realtime", "http://realtime:8000/v1", "http://a b"])
def test_realtime_url_must_be_plain_http_address(url):
    with pytest.raises(ConfigError):
        load_settings({"JWT_SECRET": "s" * 40, "KAKAO_MODE": "fake", "REALTIME_URL": url})


def test_realtime_url_ok():
    s = load_settings({"JWT_SECRET": "s" * 40, "KAKAO_MODE": "fake", "REALTIME_URL": "http://realtime:8000"})
    assert s.realtime_url == "http://realtime:8000"
    assert load_settings({"JWT_SECRET": "s" * 40, "KAKAO_MODE": "fake"}).realtime_url is None
