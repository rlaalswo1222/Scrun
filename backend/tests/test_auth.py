"""로그인 API 5개 진짜 흐름(테스트 DB + 가짜 카카오). 실행 전 `docker compose up -d`."""
from sqlalchemy import func, select, text

from app.core import tokens
from app.db.models import AuthIdentity, RefreshToken, User, UserAgreement, UserProgress

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


def signup(client, kakao="k_1", **override):
    first = data(client.post("/v1/auth/kakao", json={"kakaoAccessToken": kakao}))
    assert first["isNewUser"] is True and first["signupToken"] and first["accessToken"] is None
    r = client.post("/v1/auth/signup", headers=bearer(first["signupToken"]), json={**SIGNUP_BODY, **override})
    assert r.status_code == 201, r.text
    return data(r)


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
    signup_token = first["signupToken"]
    r1 = db_client.post("/v1/auth/signup", headers=bearer(signup_token), json={**SIGNUP_BODY, "nickname": "둘째"})
    r2 = db_client.post("/v1/auth/signup", headers=bearer(signup_token), json={**SIGNUP_BODY, "nickname": "셋째"})
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


def test_refresh_rotates_and_reuse_revokes_all(db_client):
    t1 = signup(db_client)
    t2 = data(db_client.post("/v1/auth/refresh", json={"refreshToken": t1["refreshToken"]}))
    assert t2["refreshToken"] != t1["refreshToken"]
    t3 = data(db_client.post("/v1/auth/refresh", json={"refreshToken": t2["refreshToken"]}))
    # 이미 쓴 t1을 다시 쓰면 탈취로 보고 전부 폐기 → t3도 못 씀
    fail(db_client.post("/v1/auth/refresh", json={"refreshToken": t1["refreshToken"]}), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/refresh", json={"refreshToken": t3["refreshToken"]}), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/refresh", json={"refreshToken": "unknown"}), 401, "UNAUTHORIZED")


def test_refresh_expired(db_client, test_db):
    t = signup(db_client)
    with test_db.engine.begin() as conn:
        conn.execute(text("UPDATE refresh_tokens SET expires_at = now() - interval '1 second'"))
    fail(db_client.post("/v1/auth/refresh", json={"refreshToken": t["refreshToken"]}), 401, "TOKEN_EXPIRED")


def test_logout_revokes_refresh_token(db_client):
    t = signup(db_client)
    r = db_client.post("/v1/auth/logout", headers=bearer(t["accessToken"]), json={"refreshToken": t["refreshToken"]})
    assert r.status_code == 204 and r.content == b""
    fail(db_client.post("/v1/auth/refresh", json={"refreshToken": t["refreshToken"]}), 401, "UNAUTHORIZED")


def test_logout_needs_real_login(db_client):
    t = signup(db_client)
    signup_token = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_3"}))["signupToken"]
    body = {"refreshToken": t["refreshToken"]}
    fail(db_client.post("/v1/auth/logout", json=body), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/logout", headers=bearer("demo"), json=body), 401, "UNAUTHORIZED")
    fail(db_client.post("/v1/auth/logout", headers=bearer(signup_token), json=body), 403, "SIGNUP_REQUIRED")


def test_deleted_user_token_rejected(db_client, test_db):
    t = signup(db_client)
    with test_db.engine.begin() as conn:
        conn.execute(text("DELETE FROM users"))
        # 사용자에 딸린 행이 같이 지워짐(CASCADE)
        for table in ("auth_identities", "refresh_tokens", "user_agreements", "user_progress"):
            assert conn.execute(text(f"SELECT count(*) FROM {table}")).scalar() == 0
    fail(db_client.post("/v1/auth/logout", headers=bearer(t["accessToken"]), json={"refreshToken": "x"}), 401, "UNAUTHORIZED")
