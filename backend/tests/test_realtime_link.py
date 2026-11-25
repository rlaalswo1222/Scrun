"""허브 내부 API와 원격 허브(계획 0073, app/realtime/internal.py·link.py). 허브를 거치는 흐름은 test_link_*.py가
기존 테스트를 원격 허브로 한 번 더 돌린다."""
import httpx
import pytest
from fastapi.testclient import TestClient

from app.core.config import ConfigError, load_settings, settings
from app.main import app
from app.realtime.hub import Companion, RunningNow, hub
from app.realtime import link
from app.realtime.link import INTERNAL_HEADER, HubBusy, RemoteHub, internal_token
from tests.test_courses import db, me  # noqa: F401 (픽스처)

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
    r = RemoteHub("http://127.0.0.1:9", TOKEN)   # 아무도 안 듣는 곳 = 죽음
    assert r.present_user_ids() == set()
    assert r.running_positions("crs_lake_loop") == []
    assert r.in_map("U_A") is None
    assert r.running_now(["U_A"]) == {}
    assert r.companions("U_A", "run_1") == []
    r.start_tracking("U_A", "run_1")
    r.stop_tracking("U_A", "run_1")
    r.forget_user("U_A")
    assert r.present_user_ids(strict=True) == set(), "죽었으면 방에 붙은 사람도 없음 → 입장은 DB 기준으로 진행"


class _Answers:
    """정해 둔 답을 내는 가짜 클라이언트(부른 횟수를 셈)."""

    def __init__(self, *answers):
        self.answers, self.calls = list(answers), 0

    def post(self, *_a, **_k):
        self.calls += 1
        a = self.answers.pop(0) if len(self.answers) > 1 else self.answers[0]
        if isinstance(a, Exception):
            raise a
        return httpx.Response(a, json={"ids": ["U_A"]}) if a == 200 else httpx.Response(a)


def test_busy_realtime_refuses_entry_check_but_not_reads():
    """살아 있는데 바쁨(시간 초과·오류 답): 입장 정원(strict)만 HubBusy, 다른 읽기는 대체 값(리뷰: 정원을 넘기지 않게)"""
    r = RemoteHub("http://x", TOKEN, client=_Answers(httpx.ReadTimeout("느림")))
    with pytest.raises(HubBusy):
        r.present_user_ids(strict=True)
    assert r.present_user_ids() == set() and r.in_map("U_A") is None


def test_circuit_breaker_skips_calls_for_a_while():
    """한 번 실패하면 잠깐 부르지 않음(실시간이 죽은 동안 요청마다 연결 실패를 기다리지 않게), 지나면 다시 부름"""
    now = [100.0]
    fake = _Answers(httpx.ConnectError("죽음"), 200)
    r = RemoteHub("http://x", TOKEN, client=fake, clock=lambda: now[0])
    assert r.present_user_ids() == set() and fake.calls == 1
    assert r.present_user_ids() == set() and r.running_now(["U_A"]) == {} and fake.calls == 1, "열린 동안은 안 부름"
    now[0] += link.DOWN_S
    assert r.present_user_ids() == {"U_A"} and fake.calls == 2, "지나면 다시 물어 살아난 것을 앎"


def test_entry_is_503_while_realtime_is_busy(db_client, me, db, monkeypatch):
    """맵 입장: 실시간이 바쁘면 정원을 빈 값으로 세지 않고 503 REALTIME_BUSY. 목록의 인원 표시는 그대로 됨"""
    from tests.helpers import fail
    from tests.test_courses import calibrate, enter
    calibrate(db, me[1])
    monkeypatch.setattr(link, "hub_link", RemoteHub("http://x", TOKEN, client=_Answers(httpx.ReadTimeout("느림"))))
    r = enter(db_client, me[0])
    fail(r, 503, "SERVICE_UNAVAILABLE")
    assert r.json()["error"]["details"] == {"reason": "REALTIME_BUSY"}
    assert db_client.get("/v1/courses", headers=me[0]).status_code == 200


def test_routers_reach_the_hub_only_through_link():
    """허브를 직접 부르면 REALTIME_URL로 프로세스를 나눴을 때 다른 프로세스의 빈 허브를 봄. 실시간 안쪽(app/realtime)만 직접"""
    import re
    from pathlib import Path
    root = Path(__file__).resolve().parents[1] / "app"
    def direct(src: str) -> bool:
        for m in re.finditer(r"from \.+realtime\.hub import ([^\n]+)", src):
            if "hub" in [n.strip() for n in m.group(1).split(",")]:
                return True   # 허브 객체를 직접 가져옴(RunningNow·RUNNER 같은 형식·상수는 괜찮음)
        return re.search(r"(?<!link\.)\bhub_link\b", src) is not None   # link.hub_link가 아닌 이름으로 붙잡아 둠
    bad = [str(f.relative_to(root)) for f in root.rglob("*.py")
           if "realtime" not in f.parts and direct(f.read_text(encoding="utf-8"))]
    assert not bad, f"허브는 link.hub_link로: {bad}"


@pytest.mark.parametrize("url", ["realtime:8000", "ftp://realtime", "http://realtime:8000/v1", "http://a b"])
def test_realtime_url_must_be_plain_http_address(url):
    with pytest.raises(ConfigError):
        load_settings({"JWT_SECRET": "s" * 40, "KAKAO_MODE": "fake", "REALTIME_URL": url})


def test_realtime_url_ok():
    s = load_settings({"JWT_SECRET": "s" * 40, "KAKAO_MODE": "fake", "REALTIME_URL": "http://realtime:8000"})
    assert s.realtime_url == "http://realtime:8000"
    assert load_settings({"JWT_SECRET": "s" * 40, "KAKAO_MODE": "fake"}).realtime_url is None
