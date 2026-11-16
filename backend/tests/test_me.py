"""내 정보·설정 진짜 흐름(테스트 DB, BL-36, 계획 0028). 실행 전 DB가 떠 있어야 함."""
import pytest

from app.core import tokens

from tests.helpers import bearer, data, fail, run_together, signup

ME = "/v1/users/me"
SETTINGS = "/v1/users/me/settings"


# ---------- 내 정보 조회 ----------
def test_가입한_값을_그대로_돌려준다(db_client):
    me = signup(db_client, "k_1")
    got = data(db_client.get(ME, headers=me))
    assert got["nickname"] == "러너1" and got["heightCm"] == 175 and got["weightKg"] == 70
    assert got["timezone"] == "Asia/Seoul"
    assert got["friendCode"].startswith("SCR-") and got["userId"]
    assert got["createdAt"].endswith("Z")


def test_로그인_없이는_안_됨(db_client):
    for method, path in (("GET", ME), ("PATCH", ME), ("GET", SETTINGS), ("PATCH", SETTINGS)):
        fail(db_client.request(method, path, json={}), 401, "UNAUTHORIZED")


def test_만료된_토큰은_TOKEN_EXPIRED(db_client):
    me = signup(db_client, "k_1")
    user_id = data(db_client.get(ME, headers=me))["userId"]
    expired = bearer(tokens._encode(user_id, tokens.ACCESS, -60))  # 1분 전에 만료된 access 토큰
    fail(db_client.get(ME, headers=expired), 401, "TOKEN_EXPIRED")
    fail(db_client.patch(SETTINGS, json={"feedShareDefault": False}, headers=expired), 401, "TOKEN_EXPIRED")


def test_가입용_토큰으로는_안_됨(db_client):
    signup_token = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_9"}))["signupToken"]
    fail(db_client.get(ME, headers=bearer(signup_token)), 403, "SIGNUP_REQUIRED")


# ---------- 내 정보 수정 ----------
def test_보낸_칸만_바뀐다(db_client):
    me = signup(db_client, "k_1")
    got = data(db_client.patch(ME, json={"weightKg": 68}, headers=me))
    assert got["weightKg"] == 68 and got["heightCm"] == 175 and got["nickname"] == "러너1"
    assert data(db_client.get(ME, headers=me)) == got  # 저장됨


def test_빈_본문이면_그대로(db_client):
    me = signup(db_client, "k_1")
    before = data(db_client.get(ME, headers=me))
    assert data(db_client.patch(ME, json={}, headers=me)) == before


def test_닉네임은_정규화해서_저장(db_client):
    me = signup(db_client, "k_1")
    got = data(db_client.patch(ME, json={"nickname": "  아침러너  "}, headers=me))
    assert got["nickname"] == "아침러너"


@pytest.mark.parametrize("name", ["a", "공백 있음", "이모지😀", "열세글자가넘는닉네임입니다"])
def test_닉네임_형식이_틀리면_400(db_client, name):
    me = signup(db_client, "k_1")
    r = db_client.patch(ME, json={"nickname": name}, headers=me)
    fail(r, 400, "VALIDATION_ERROR")
    assert any(f["field"] == "nickname" for f in r.json()["error"]["details"]["fields"])
    assert data(db_client.get(ME, headers=me))["nickname"] == "러너1"


def test_금칙어는_400(db_client):
    me = signup(db_client, "k_1")
    fail(db_client.patch(ME, json={"nickname": "Scrun_관리"}, headers=me), 400, "NICKNAME_NOT_ALLOWED")


def test_남이_쓰는_닉네임은_409(db_client):
    signup(db_client, "k_2")  # 러너2
    me = signup(db_client, "k_1")
    fail(db_client.patch(ME, json={"nickname": "러너2"}, headers=me), 409, "NICKNAME_DUPLICATED")


def test_대소문자만_달라도_남의_것이면_409(db_client):
    other = signup(db_client, "k_2")
    db_client.patch(ME, json={"nickname": "RunnerA"}, headers=other)
    me = signup(db_client, "k_1")
    fail(db_client.patch(ME, json={"nickname": "runnera"}, headers=me), 409, "NICKNAME_DUPLICATED")


def test_내_닉네임은_그대로_쓸_수_있고_대소문자만_바꿔도_된다(db_client):
    me = signup(db_client, "k_1")
    db_client.patch(ME, json={"nickname": "RunnerA"}, headers=me)
    assert data(db_client.patch(ME, json={"nickname": "RunnerA"}, headers=me))["nickname"] == "RunnerA"
    assert data(db_client.patch(ME, json={"nickname": "runnerA"}, headers=me))["nickname"] == "runnerA"


def test_두_사람이_같은_닉네임으로_동시에_바꾸면_한_명만(db_client):
    a = signup(db_client, "k_1")
    b = signup(db_client, "k_2")
    rs = run_together([lambda c, h=h: c.patch(ME, json={"nickname": "새벽러너"}, headers=h) for h in (a, b)])
    codes = sorted(r.status_code for r in rs)
    assert codes == [200, 409], [r.text for r in rs]
    loser = next(r for r in rs if r.status_code == 409)
    assert loser.json()["error"]["code"] == "NICKNAME_DUPLICATED"


@pytest.mark.parametrize("field,value", [("heightCm", 99), ("heightCm", 231), ("weightKg", 19), ("weightKg", 251)])
def test_키_몸무게_범위(db_client, field, value):
    me = signup(db_client, "k_1")
    fail(db_client.patch(ME, json={field: value}, headers=me), 400, "VALIDATION_ERROR")


def test_시간대는_IANA_이름만(db_client):
    me = signup(db_client, "k_1")
    assert data(db_client.patch(ME, json={"timezone": "America/New_York"}, headers=me))["timezone"] == "America/New_York"
    for bad in ("Korea Standard Time", "KST", "../etc/passwd", "localtime", "", "Asia", "asia/seoul", "Factory"):
        fail(db_client.patch(ME, json={"timezone": bad}, headers=me), 400, "VALIDATION_ERROR")
    assert data(db_client.get(ME, headers=me))["timezone"] == "America/New_York"


def test_가입도_시간대는_IANA_이름만(db_client):
    first = data(db_client.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_1"}))
    body = {"agreedIds": ["terms", "privacy"], "nickname": "러너1", "heightCm": 175, "weightKg": 70,
            "timezone": "Korea Standard Time"}
    fail(db_client.post("/v1/auth/signup", json=body, headers=bearer(first["signupToken"])), 400, "VALIDATION_ERROR")


def test_남의_정보는_안_바뀐다(db_client):
    a = signup(db_client, "k_1")
    b = signup(db_client, "k_2")
    db_client.patch(ME, json={"nickname": "바뀐이름", "heightCm": 180}, headers=a)
    other = data(db_client.get(ME, headers=b))
    assert other["nickname"] == "러너2" and other["heightCm"] == 175


# ---------- 설정 ----------
def test_피드_공유_기본값은_켜짐(db_client):
    me = signup(db_client, "k_1")
    assert data(db_client.get(SETTINGS, headers=me)) == {"feedShareDefault": True}


def test_설정을_바꾸면_저장된다(db_client):
    me = signup(db_client, "k_1")
    assert data(db_client.patch(SETTINGS, json={"feedShareDefault": False}, headers=me)) == {"feedShareDefault": False}
    assert data(db_client.get(SETTINGS, headers=me)) == {"feedShareDefault": False}
    assert data(db_client.patch(SETTINGS, json={}, headers=me)) == {"feedShareDefault": False}  # 빈 본문이면 그대로


@pytest.mark.parametrize("value", ["yes", 1, "false"])
def test_설정은_true_false만(db_client, value):
    me = signup(db_client, "k_1")
    fail(db_client.patch(SETTINGS, json={"feedShareDefault": value}, headers=me), 400, "VALIDATION_ERROR")
    assert data(db_client.get(SETTINGS, headers=me)) == {"feedShareDefault": True}
