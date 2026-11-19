"""계정 삭제(계획 0058, BL-48): DELETE /users/me와 카카오 연결 끊기 웹훅. 실행 전 DB가 떠 있어야 함."""
import pytest
from sqlalchemy import func, select

from app.core.errors import ApiError, ErrorCode
from app.core.kakao import FakeKakao, get_kakao
from app.db.models import Base, RunCompanion
from app.main import app
from app.realtime.hub import Hub
from app.routers.auth import kakao_webhook_secret
from tests.helpers import data, fail, signup
from tests.test_friends import befriend, friends
from tests.test_run_records import add_run, my_id
from tests.test_runs import ago, db, enter  # noqa: F401 (픽스처)

APP_ID = 1641065
ADMIN = "test-admin-key"
USER_COLUMNS = ("user_id", "companion_user_id", "user_a_id", "user_b_id")


def rows_of(db, uid) -> dict:
    """사용자 ID를 담는 모든 칸에서 그 사람 줄 수(0이 아닌 표만)."""
    out = {}
    for table in Base.metadata.sorted_tables:
        cols = [table.c[name] for name in USER_COLUMNS if name in table.c]
        if table.name == "users":
            cols = [table.c.id]
        for col in cols:
            n = db.scalar(select(func.count()).select_from(table).where(col == uid))
            if n:
                out[f"{table.name}.{col.name}"] = n
    return out


class RecordingKakao(FakeKakao):
    def __init__(self, fail_with=None):
        self.unlinked = []
        self.fail_with = fail_with

    def unlink(self, kakao_user_id: str) -> None:
        if self.fail_with:
            raise self.fail_with
        self.unlinked.append(kakao_user_id)


@pytest.fixture
def kakao():
    k = RecordingKakao()
    app.dependency_overrides[get_kakao] = lambda: k
    yield k
    app.dependency_overrides.pop(get_kakao, None)


@pytest.fixture
def webhook():
    app.dependency_overrides[kakao_webhook_secret] = lambda: (ADMIN, APP_ID)
    yield
    app.dependency_overrides.pop(kakao_webhook_secret, None)


def full_user(client, db, kakao_token="k_1"):
    """기록·친구·주간 목표·보정·입장까지 있는 사람과 친구."""
    a, b = signup(client, kakao_token), signup(client, "k_9")
    befriend(client, a, b)
    enter(client, db, a)
    ua, ub = my_id(client, a), my_id(client, b)
    assert client.put("/v1/users/me/weekly-goal", headers=a, json={"targetCount": 3}).status_code == 200
    add_run(db, ua, ago(hours=3), status="VERIFIED", duration=1200)
    their = add_run(db, ub, ago(hours=2), status="VERIFIED")
    db.add(RunCompanion(run_id=their, seq=0, kind="FRIEND", companion_user_id=ua))
    db.commit()
    return a, b, ua, their


# ---------- 앱에서 삭제 ----------
def test_delete_removes_everything_and_unlinks_kakao(db_client, db, kakao):
    a, b, ua, their = full_user(db_client, db)
    assert rows_of(db, ua), "지우기 전엔 줄이 있음"
    r = db_client.delete("/v1/users/me", headers=a)
    assert r.status_code == 204, r.text
    assert kakao.unlinked == ["1"], "k_1의 카카오 회원번호로 연결 끊기"
    db.expire_all()
    assert rows_of(db, ua) == {}, "사용자 ID를 담는 모든 표에서 사라짐"
    assert friends(db_client, b) == [], "친구 목록에서 빠짐"
    assert db.scalar(select(func.count()).select_from(RunCompanion).where(RunCompanion.run_id == their)) == 0, \
        "친구 기록의 함께 달린 러너 줄도 지움(정할 것 4 가)"
    fail(db_client.get("/v1/users/me", headers=a), 401, "UNAUTHORIZED")


def test_kakao_failure_deletes_nothing(db_client, db):
    a = signup(db_client, "k_1")
    ua = my_id(db_client, a)
    before = rows_of(db, ua)
    app.dependency_overrides[get_kakao] = lambda: RecordingKakao(ApiError(ErrorCode.SERVICE_UNAVAILABLE, "카카오 안 됨"))
    try:
        fail(db_client.delete("/v1/users/me", headers=a), 503, "SERVICE_UNAVAILABLE")
    finally:
        app.dependency_overrides.pop(get_kakao, None)
    db.expire_all()
    assert rows_of(db, ua) == before
    assert data(db_client.get("/v1/users/me", headers=a))["userId"] == ua


def test_same_kakao_signs_up_as_new_account(db_client, kakao):
    a = signup(db_client, "k_1")
    old = my_id(db_client, a)
    assert db_client.delete("/v1/users/me", headers=a).status_code == 204
    again = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_1"}))
    assert again["isNewUser"] is True, "약관부터 다시"
    b = signup(db_client, "k_1")
    assert my_id(db_client, b) != old


def test_refresh_token_dies_with_account(db_client, kakao):
    first = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_1"}))
    r = db_client.post("/v1/auth/signup", headers={"Authorization": f"Bearer {first['signupToken']}"},
                       json={"agreedIds": ["terms", "privacy", "age14"], "nickname": "러너1", "heightCm": 175, "weightKg": 70})
    tok = data(r)
    assert db_client.delete("/v1/users/me", headers={"Authorization": f"Bearer {tok['accessToken']}"}).status_code == 204
    fail(db_client.post("/v1/auth/refresh", json={"refreshToken": tok["refreshToken"]}), 401, "UNAUTHORIZED")


def test_signup_token_cannot_delete(db_client):
    first = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_1"}))
    fail(db_client.delete("/v1/users/me", headers={"Authorization": f"Bearer {first['signupToken']}"}),
         403, "SIGNUP_REQUIRED")


# ---------- 카카오 쪽에서 끊음(웹훅) ----------
def hook(client, method="POST", key=ADMIN, **params):
    params = {"app_id": str(APP_ID), "user_id": "1", "referrer_type": "UNLINK_FROM_APPS", **params}
    headers = {"Authorization": f"KakaoAK {key}"}
    if method == "GET":
        return client.get("/v1/auth/kakao/unlink", params=params, headers=headers)
    return client.post("/v1/auth/kakao/unlink", data=params, headers=headers)


def test_webhook_deletes_account(db_client, db, webhook):
    a, b, ua, _ = full_user(db_client, db)
    r = hook(db_client)
    assert r.status_code == 200, r.text
    db.expire_all()
    assert rows_of(db, ua) == {}
    assert friends(db_client, b) == []


def test_webhook_by_get(db_client, webhook):
    a = signup(db_client, "k_1")
    assert hook(db_client, method="GET").status_code == 200
    fail(db_client.get("/v1/users/me", headers=a), 401, "UNAUTHORIZED")


def test_webhook_rejects_wrong_key_or_app(db_client, webhook):
    a = signup(db_client, "k_1")
    fail(hook(db_client, key="nope"), 401, "UNAUTHORIZED")
    fail(hook(db_client, app_id="999"), 400, "VALIDATION_ERROR")
    fail(hook(db_client, user_id="abc"), 400, "VALIDATION_ERROR")
    assert data(db_client.get("/v1/users/me", headers=a))["nickname"] == "러너1", "안 지워짐"


def test_webhook_unknown_user_is_ok(db_client, webhook):
    assert hook(db_client, user_id="424242").status_code == 200, "가입 전에 끊었거나 이미 지움 → 카카오가 다시 보내지 않게 200"


def test_webhook_without_admin_key_is_503(db_client):
    app.dependency_overrides[kakao_webhook_secret] = lambda: (None, APP_ID)
    try:
        fail(hook(db_client), 503, "SERVICE_UNAVAILABLE")
    finally:
        app.dependency_overrides.pop(kakao_webhook_secret, None)


def test_webhook_not_in_public_docs(db_client):
    assert "/v1/auth/kakao/unlink" not in db_client.get("/openapi.json").json()["paths"]


# ---------- 실시간 방 ----------
def test_hub_forgets_deleted_user():
    h = Hub(grace_s=30)
    conn_a, _ = h.join("map_x", "crs_x", 1000.0, "ua", "A")
    conn_b, _ = h.join("map_x", "crs_x", 1000.0, "ub", "B")
    h.start_tracking("ub", "run_b")
    h.update(conn_a, 10.0, 9.0, True, run=10.0)
    h.update(conn_b, 20.0, 9.0, True, run=20.0)
    assert [c.user_id for c in h.companions("ub", "run_b")] == ["ua"]
    h.forget_user("ua")
    assert conn_a.replaced, "연결은 닫힘"
    assert h.in_map("ua") is None
    assert h.update(conn_a, 30.0, 9.0, True) is False
    again, welcome = h.join("map_x", "crs_x", 1000.0, "ua", "A")
    assert again.replaced and welcome["runners"] == [], "삭제 순간 다시 붙어도 방에 안 들어감(리뷰)"
    assert h.in_map("ua") is None


def test_companion_deleted_mid_run_is_not_saved(db_client, db, kakao):
    """같이 달리던 사람이 그 러닝 중에 계정을 지우면 남의 기록에 함께 달린 러너로 남지 않음(_save_companions가 거름)."""
    from app.realtime.hub import hub
    from tests.test_runs import finish, started_id
    a, b = signup(db_client, "k_1"), signup(db_client, "k_2")
    enter(db_client, db, b)
    run_id = started_id(db_client, b)
    ua = my_id(db_client, a)
    conn_a, _ = hub.join("map_crs_lake_loop", "crs_lake_loop", 6670.0, ua, "러너1")
    conn_b, _ = hub.join("map_crs_lake_loop", "crs_lake_loop", 6670.0, my_id(db_client, b), "러너2")
    try:
        hub.update(conn_a, 10.0, 9.0, True, run=10.0)
        hub.update(conn_b, 20.0, 9.0, True, run=20.0)
        assert [c.user_id for c in hub.companions(my_id(db_client, b), run_id)] == [ua]
        assert db_client.delete("/v1/users/me", headers=a).status_code == 204
        assert finish(db_client, b, run_id).status_code == 200
        db.expire_all()
        assert db.scalar(select(func.count()).select_from(RunCompanion).where(RunCompanion.run_id == run_id)) == 0
    finally:
        hub.leave(conn_b, intentional=True)


def test_expired_token_cannot_delete(db_client):
    from app.core import tokens
    a = signup(db_client, "k_1")
    expired = tokens._encode(my_id(db_client, a), tokens.ACCESS, -10)
    fail(db_client.delete("/v1/users/me", headers={"Authorization": f"Bearer {expired}"}), 401, "TOKEN_EXPIRED")
    assert data(db_client.get("/v1/users/me", headers=a))["nickname"] == "러너1"

