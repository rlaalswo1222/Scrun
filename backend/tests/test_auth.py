"""로그인 API 5개 + 닉네임 확인 진짜 흐름(테스트 DB + 가짜 카카오). 실행 전 `docker compose up -d`."""
import threading

from fastapi.testclient import TestClient
from sqlalchemy import func, select, text

from app.core import sessions, tokens
from app.db.models import AuthIdentity, RefreshToken, User, UserAgreement, UserProgress
from app.main import app
from app.routers import auth as auth_router

SIGNUP_BODY = {"agreedIds": ["terms", "privacy"], "nickname": "새벽러너", "heightCm": 175, "weightKg": 70}


def bearer(token: str) -> dict:
    return {"Authorization": f"Bearer {token}"}


def data(r):
    j = r.json()
    assert j["resultType"] == "SUCCESS", r.text
    assert j["meta"]["requestId"].startswith("req_") and j["meta"]["timestamp"].endswith("Z")
    return j["success"]["data"]


def fail(r, status: int, code: str):
    assert r.status_code == status, r.text
    j = r.json()
    assert j["resultType"] == "FAIL" and j["error"]["code"] == code, r.text


def signup_token(client, kakao="k_1") -> str:
    first = data(client.post("/v1/auth/kakao", json={"kakaoAccessToken": kakao}))
    assert first["isNewUser"] is True
    return first["signupToken"]


def signup(client, kakao="k_1", device=None, **override):
    first = data(client.post("/v1/auth/kakao", json={"kakaoAccessToken": kakao}))
    assert first["isNewUser"] is True and first["signupToken"] and first["accessToken"] is None
    body = {**SIGNUP_BODY, **override, **({"deviceId": device} if device else {})}
    r = client.post("/v1/auth/signup", headers=bearer(first["signupToken"]), json=body)
    assert r.status_code == 201, r.text
    return data(r)


def login(client, kakao="k_1", device=None):
    body = {"kakaoAccessToken": kakao, **({"deviceId": device} if device else {})}
    d = data(client.post("/v1/auth/kakao", json=body))
    assert d["isNewUser"] is False
    return d


def refresh(client, t):
    return client.post("/v1/auth/refresh", json={"refreshToken": t["refreshToken"]})


def test_agreements_from_db(db_client):
    items = data(db_client.get("/v1/agreements"))
    assert [a["agreementId"] for a in items] == ["terms", "privacy", "notice"]
    assert [a["required"] for a in items] == [True, True, False]


def test_first_login_signup_then_login_again(db_client, test_db):
    tokens1 = signup(db_client)
    assert tokens1["accessToken"] and tokens1["refreshToken"] and tokens1["expiresInS"] == 3600

    again = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_1"}))
    assert again["isNewUser"] is False and again["accessToken"] and again["signupToken"] is None

    with test_db.Session() as db:
        user = db.scalar(select(User))
        assert user.nickname == "새벽러너" and user.friend_code.startswith("SCR-")
        assert db.scalar(select(AuthIdentity.provider_user_id)) == "1"
        assert db.scalar(select(func.count()).select_from(UserAgreement)) == 2
        assert db.get(UserProgress, user.id).level == 1
        # refresh 토큰은 해시만 저장
        assert db.scalar(select(func.count()).select_from(RefreshToken).where(
            RefreshToken.token_hash == tokens1["refreshToken"])) == 0
        assert tokens.decode(tokens1["accessToken"])["sub"] == user.id


def test_signup_twice_returns_same_account(db_client, test_db):
    signup(db_client)
    first = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_9"}))
    signup_token_ = first["signupToken"]
    r1 = db_client.post("/v1/auth/signup", headers=bearer(signup_token_), json={**SIGNUP_BODY, "nickname": "둘째"})
    r2 = db_client.post("/v1/auth/signup", headers=bearer(signup_token_), json={**SIGNUP_BODY, "nickname": "셋째"})
    assert r1.status_code == r2.status_code == 201
    with test_db.Session() as db:
        assert db.scalar(select(func.count()).select_from(User)) == 2


def test_signup_validation(db_client):
    signup(db_client)
    token = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_2"}))["signupToken"]
    h = bearer(token)
    fail(db_client.post("/v1/auth/signup", headers=h, json={**SIGNUP_BODY, "agreedIds": ["terms"], "nickname": "다른"}),
         400, "VALIDATION_ERROR")
    fail(db_client.post("/v1/auth/signup", headers=h, json={**SIGNUP_BODY, "agreedIds": ["terms", "privacy", "nope"],
                                                            "nickname": "다른"}), 400, "VALIDATION_ERROR")
    fail(db_client.post("/v1/auth/signup", headers=h, json={**SIGNUP_BODY, "nickname": "새벽러너"}), 409, "NICKNAME_DUPLICATED")
    fail(db_client.post("/v1/auth/signup", headers=h, json={**SIGNUP_BODY, "nickname": "운영자입니다"}), 400, "NICKNAME_NOT_ALLOWED")
    fail(db_client.post("/v1/auth/signup", headers=h, json={**SIGNUP_BODY, "nickname": "다른", "heightCm": 500}),
         400, "VALIDATION_ERROR")


def test_signup_needs_signup_token(db_client):
    access = signup(db_client)["accessToken"]
    fail(db_client.post("/v1/auth/signup", json=SIGNUP_BODY), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/signup", headers=bearer(access), json=SIGNUP_BODY), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/signup", headers=bearer("garbage"), json=SIGNUP_BODY), 401, "UNAUTHORIZED")


def test_invalid_kakao_token(db_client):
    fail(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "not-kakao"}), 401, "INVALID_KAKAO_TOKEN")


def test_refresh_rotates_and_reuse_revokes_that_login(db_client):
    t1 = signup(db_client)
    t2 = data(refresh(db_client, t1))
    assert t2["refreshToken"] != t1["refreshToken"]
    t3 = data(refresh(db_client, t2))
    # 이미 쓴 t1을 다시 쓰면(대신 나간 t2도 이미 씀) 탈취로 보고 그 로그인 폐기 → t3도 못 씀
    fail(refresh(db_client, t1), 401, "UNAUTHORIZED")
    fail(refresh(db_client, t3), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/refresh", json={"refreshToken": "unknown"}), 401, "UNAUTHORIZED")


def test_refresh_expired(db_client, test_db):
    t = signup(db_client)
    with test_db.engine.begin() as conn:
        conn.execute(text("UPDATE refresh_tokens SET expires_at = now() - interval '1 second'"))
    fail(refresh(db_client, t), 401, "TOKEN_EXPIRED")


def test_logout_revokes_refresh_token(db_client):
    t = signup(db_client)
    r = db_client.post("/v1/auth/logout", headers=bearer(t["accessToken"]), json={"refreshToken": t["refreshToken"]})
    assert r.status_code == 204 and r.content == b""
    fail(refresh(db_client, t), 401, "UNAUTHORIZED")


def test_logout_needs_real_login(db_client):
    t = signup(db_client)
    signup_token_ = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_3"}))["signupToken"]
    body = {"refreshToken": t["refreshToken"]}
    fail(db_client.post("/v1/auth/logout", json=body), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/logout", headers=bearer("demo"), json=body), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/logout", headers=bearer(signup_token_), json=body), 403, "SIGNUP_REQUIRED")


def test_deleted_user_token_rejected(db_client, test_db):
    t = signup(db_client)
    with test_db.engine.begin() as conn:
        conn.execute(text("DELETE FROM users"))
        # 사용자에 딸린 행이 같이 지워짐(CASCADE)
        for table in ("auth_identities", "refresh_tokens", "user_agreements", "user_progress"):
            assert conn.execute(text(f"SELECT count(*) FROM {table}")).scalar() == 0
    fail(db_client.post("/v1/auth/logout", headers=bearer(t["accessToken"]), json={"refreshToken": "x"}), 401, "UNAUTHORIZED")


# ---------- 리뷰 후 수정(계획 0016): 닉네임 ----------

def test_signup_nickname_normalized_and_validated(db_client, test_db):
    h = bearer(signup_token(db_client, "k_1"))
    for bad in ("   ", "새벽 러너", "러너​", "ㅋㅋ러너", "가"):
        r = db_client.post("/v1/auth/signup", headers=h, json={**SIGNUP_BODY, "nickname": bad})
        fail(r, 400, "VALIDATION_ERROR")
        assert r.json()["error"]["details"]["fields"][0]["field"] == "nickname"
    fail(db_client.post("/v1/auth/signup", headers=h, json={**SIGNUP_BODY, "nickname": "ＡＤＭＩＮ"}), 400, "NICKNAME_NOT_ALLOWED")
    r = db_client.post("/v1/auth/signup", headers=h, json={**SIGNUP_BODY, "nickname": "  ＲＵＮＮＥＲ  "})
    assert r.status_code == 201, r.text
    with test_db.Session() as db:
        assert db.scalar(select(User.nickname)) == "RUNNER"
    # 대소문자만 다른 닉네임은 중복
    h2 = bearer(signup_token(db_client, "k_2"))
    fail(db_client.post("/v1/auth/signup", headers=h2, json={**SIGNUP_BODY, "nickname": "runner"}), 409, "NICKNAME_DUPLICATED")


def test_nickname_check_same_rules_as_signup(db_client):
    me = signup(db_client, "k_1", nickname="새벽러너")
    h = bearer(signup_token(db_client, "k_2"))

    def check(nickname, headers=h):
        return data(db_client.get("/v1/users/nickname/check", params={"nickname": nickname}, headers=headers))

    assert check("  달밤러너 ") == {"nickname": "달밤러너", "available": True, "reason": None}
    assert check("새벽러너")["reason"] == "DUPLICATED"
    assert check("ＡＤＭＩＮ") == {"nickname": "ADMIN", "available": False, "reason": "NOT_ALLOWED"}
    assert check("   ")["reason"] == "INVALID"
    assert check("새벽 러너")["reason"] == "INVALID"
    # 로그인한 사람의 지금 닉네임은 사용 가능(내 정보 수정 화면)
    assert check("새벽러너", bearer(me["accessToken"]))["available"] is True
    fail(db_client.get("/v1/users/nickname/check", params={"nickname": "a1"}), 401, "UNAUTHORIZED")
    fail(db_client.get("/v1/users/nickname/check", params={"nickname": "a1"}, headers=bearer("signup")), 401, "UNAUTHORIZED")


# ---------- 리뷰 후 수정: 동시 가입 ----------

def _run_together(calls):
    """calls를 스레드로 동시에(스레드마다 자기 TestClient). 응답 목록."""
    out = [None] * len(calls)

    def run(i, fn):
        out[i] = fn(TestClient(app))

    threads = [threading.Thread(target=run, args=(i, fn)) for i, fn in enumerate(calls)]
    for t in threads:
        t.start()
    for t in threads:
        t.join(timeout=30)
    return out


def _sync_friend_code(monkeypatch, n, code=None):
    """n개 요청이 존재 확인을 다 지난 뒤 함께 넣게 만든다(첫 친구 코드를 고를 때 서로 기다림)."""
    barrier = threading.Barrier(n, timeout=10)
    original = auth_router._new_friend_code
    waited = set()
    lock = threading.Lock()

    def synced(db):
        tid = threading.get_ident()
        with lock:
            first = tid not in waited
            waited.add(tid)
        if first:
            barrier.wait()
            if code:
                return code
        return original(db)

    monkeypatch.setattr(auth_router, "_new_friend_code", synced)


def test_concurrent_same_signup_returns_same_account(db_client, test_db, monkeypatch):
    token = signup_token(db_client, "k_7")
    _sync_friend_code(monkeypatch, 2)
    body = {**SIGNUP_BODY, "nickname": "동시가입"}
    r1, r2 = _run_together([lambda c: c.post("/v1/auth/signup", headers=bearer(token), json=body)] * 2)
    assert r1.status_code == r2.status_code == 201, (r1.text, r2.text)
    sub1 = tokens.decode(r1.json()["success"]["data"]["accessToken"])["sub"]
    sub2 = tokens.decode(r2.json()["success"]["data"]["accessToken"])["sub"]
    assert sub1 == sub2
    with test_db.Session() as db:
        assert db.scalar(select(func.count()).select_from(User)) == 1


def test_concurrent_same_nickname_one_wins(db_client, monkeypatch):
    t1, t2 = signup_token(db_client, "k_1"), signup_token(db_client, "k_2")
    _sync_friend_code(monkeypatch, 2)
    body = {**SIGNUP_BODY, "nickname": "같은이름"}
    rs = _run_together([lambda c: c.post("/v1/auth/signup", headers=bearer(t1), json=body),
                        lambda c: c.post("/v1/auth/signup", headers=bearer(t2), json=body)])
    assert sorted(r.status_code for r in rs) == [201, 409], [r.text for r in rs]
    assert next(r for r in rs if r.status_code == 409).json()["error"]["code"] == "NICKNAME_DUPLICATED"


def test_concurrent_friend_code_collision_retries(db_client, test_db, monkeypatch, caplog):
    t1, t2 = signup_token(db_client, "k_1"), signup_token(db_client, "k_2")
    _sync_friend_code(monkeypatch, 2, code="SCR-AAAA")
    caplog.set_level("WARNING", logger="scrun.auth")
    rs = _run_together([
        lambda c: c.post("/v1/auth/signup", headers=bearer(t1), json={**SIGNUP_BODY, "nickname": "첫째"}),
        lambda c: c.post("/v1/auth/signup", headers=bearer(t2), json={**SIGNUP_BODY, "nickname": "둘째"}),
    ])
    assert [r.status_code for r in rs] == [201, 201], [r.text for r in rs]
    with test_db.Session() as db:
        codes = db.scalars(select(User.friend_code)).all()
        assert len(set(codes)) == 2 and "SCR-AAAA" in codes
    # 진 쪽이 충돌 → 다시 시도 경로를 탔는지(마이그레이션 뒤에도 로그가 살아 있는지도 같이 확인)
    assert any("가입 중 충돌" in r.getMessage() for r in caplog.records)


# ---------- 리뷰 후 수정: refresh 재사용·로그인 수 ----------

def _live_tokens(test_db) -> int:
    with test_db.Session() as db:
        return db.scalar(select(func.count()).select_from(RefreshToken).where(RefreshToken.revoked_at.is_(None)))


def test_refresh_retry_within_grace_gets_new_tokens(db_client, test_db):
    t1 = signup(db_client)
    t2 = data(refresh(db_client, t1))
    # t2 응답을 못 받았다고 보고 t1으로 다시 → 새 토큰(t2를 아직 안 썼으므로). t2는 폐기되고 t2b로 넘어감
    t2b = data(refresh(db_client, t1))
    assert t2b["refreshToken"] not in (t1["refreshToken"], t2["refreshToken"])
    assert _live_tokens(test_db) == 1
    assert data(refresh(db_client, t2b))["refreshToken"]


def test_grace_replays_do_not_multiply_tokens(db_client, test_db):
    """30초 안에 같은 토큰을 몇 번 다시 보내도 살아 있는 토큰은 하나(갈래가 안 늘어남)."""
    t1 = signup(db_client)
    data(refresh(db_client, t1))
    replays = [data(refresh(db_client, t1)) for _ in range(5)]
    assert _live_tokens(test_db) == 1
    assert data(refresh(db_client, replays[-1]))["refreshToken"]


def test_grace_replay_after_child_used_is_theft(db_client, test_db):
    """대신 나간 토큰을 이미 썼는데 30초 안에 예전 토큰이 오면 탈취로 봄."""
    t1 = signup(db_client)
    t2 = data(refresh(db_client, t1))
    t3 = data(refresh(db_client, t2))
    fail(refresh(db_client, t1), 401, "UNAUTHORIZED")
    fail(refresh(db_client, t3), 401, "UNAUTHORIZED")
    assert _live_tokens(test_db) == 0


def test_concurrent_refresh_both_succeed_without_logout(db_client, test_db):
    t1 = signup(db_client)
    rs = _run_together([lambda c: refresh(c, t1)] * 2)
    assert [r.status_code for r in rs] == [200, 200], [r.text for r in rs]
    assert _live_tokens(test_db) == 1
    # 앱이 어느 응답을 저장했든 30초 안이면 이어서 쓸 수 있음
    assert data(refresh(db_client, rs[0].json()["success"]["data"]))["refreshToken"]


def test_refresh_reuse_after_grace_revokes_only_that_login(db_client, test_db, caplog):
    caplog.set_level("WARNING", logger="scrun.sessions")
    phone = signup(db_client, device="phone")
    tablet = login(db_client, device="tablet")
    phone2 = data(refresh(db_client, phone))
    with test_db.engine.begin() as conn:
        conn.execute(text("UPDATE refresh_tokens SET revoked_at = revoked_at - interval '31 seconds' "
                          "WHERE revoked_reason = 'ROTATED'"))
    fail(refresh(db_client, phone), 401, "UNAUTHORIZED")  # 30초 지난 재사용 = 탈취 의심
    fail(refresh(db_client, phone2), 401, "UNAUTHORIZED")  # 그 로그인은 폐기
    assert data(refresh(db_client, tablet))["refreshToken"]  # 다른 기기는 그대로
    with test_db.Session() as db:
        assert db.scalar(select(func.count()).select_from(RefreshToken)
                         .where(RefreshToken.revoked_reason == sessions.REUSED)) == 1
    assert any("재사용" in r.getMessage() for r in caplog.records)


def test_logged_out_token_does_not_revoke_other_devices(db_client):
    phone = signup(db_client, device="phone")
    tablet = login(db_client, device="tablet")
    r = db_client.post("/v1/auth/logout", headers=bearer(phone["accessToken"]), json={"refreshToken": phone["refreshToken"]})
    assert r.status_code == 204
    fail(refresh(db_client, phone), 401, "UNAUTHORIZED")
    fail(refresh(db_client, phone), 401, "UNAUTHORIZED")
    assert data(refresh(db_client, tablet))["refreshToken"]


def test_logout_revokes_grace_forks_too(db_client):
    t1 = signup(db_client)
    t2 = data(refresh(db_client, t1))
    t2b = data(refresh(db_client, t1))
    r = db_client.post("/v1/auth/logout", headers=bearer(t2b["accessToken"]), json={"refreshToken": t2b["refreshToken"]})
    assert r.status_code == 204
    fail(refresh(db_client, t2), 401, "UNAUTHORIZED")


def test_relogin_same_device_revokes_old_login(db_client):
    old = signup(db_client, device="phone")
    new = login(db_client, device="phone")
    fail(refresh(db_client, old), 401, "UNAUTHORIZED")
    assert data(refresh(db_client, new))["refreshToken"]
    # 기기 ID가 없으면 예전 로그인 유지
    a, b = login(db_client), login(db_client)
    assert data(refresh(db_client, a)) and data(refresh(db_client, b))


def test_sessions_capped_per_user(db_client, test_db):
    first = signup(db_client)
    later = [login(db_client) for _ in range(5)]
    fail(refresh(db_client, first), 401, "UNAUTHORIZED")  # 6번째 로그인에서 가장 오래된 것 폐기
    for t in later:
        assert data(refresh(db_client, t))["refreshToken"]
    with test_db.Session() as db:
        active = db.scalar(select(func.count(func.distinct(RefreshToken.family_id)))
                           .where(RefreshToken.revoked_at.is_(None)))
        assert active == 5


def _sync_session_start(monkeypatch, n):
    """n개 로그인이 sessions.start에 동시에 들어가게(잠금이 없으면 둘 다 같은 상태를 봄)."""
    barrier = threading.Barrier(n, timeout=10)
    original = sessions.start

    def synced(db, user_id, device_id):
        barrier.wait()
        return original(db, user_id, device_id)

    monkeypatch.setattr(auth_router.sessions, "start", synced)


def test_concurrent_logins_keep_session_cap(db_client, test_db, monkeypatch):
    signup(db_client)
    for _ in range(3):
        login(db_client)  # 살아 있는 로그인 4개
    _sync_session_start(monkeypatch, 2)
    rs = _run_together([lambda c: c.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_1"})] * 2)
    assert [r.status_code for r in rs] == [200, 200], [r.text for r in rs]
    assert _live_tokens(test_db) == 5


def test_concurrent_logins_same_device_leave_one(db_client, test_db, monkeypatch):
    signup(db_client, device="phone")
    _sync_session_start(monkeypatch, 2)
    body = {"kakaoAccessToken": "k_1", "deviceId": "phone"}
    rs = _run_together([lambda c: c.post("/v1/auth/kakao", json=body)] * 2)
    assert [r.status_code for r in rs] == [200, 200], [r.text for r in rs]
    assert _live_tokens(test_db) == 1


def test_expired_rows_cleaned(db_client, test_db):
    t = signup(db_client)
    with test_db.engine.begin() as conn:
        conn.execute(text("UPDATE refresh_tokens SET expires_at = now() - interval '1 second'"))
    login(db_client)  # 로그인할 때 그 사용자의 만료 줄 삭제
    with test_db.Session() as db:
        assert db.scalar(select(func.count()).select_from(RefreshToken)) == 1
    fail(refresh(db_client, t), 401, "UNAUTHORIZED")
    with test_db.engine.begin() as conn:
        conn.execute(text("UPDATE refresh_tokens SET expires_at = now() - interval '1 second'"))
    with test_db.Session() as db:
        assert sessions.purge_expired(db) == 1


def test_device_id_validation(db_client):
    signup(db_client)
    fail(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_1", "deviceId": ""}), 400, "VALIDATION_ERROR")
    fail(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_1", "deviceId": "x" * 101}), 400, "VALIDATION_ERROR")
