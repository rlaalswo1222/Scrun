"""보정값 저장·조회 진짜 흐름(테스트 DB, BL-4). 맵 입장의 CALIBRATION_REQUIRED가 이 저장으로 풀리는지까지. 실행 전 DB가 떠 있어야 함."""
from tests.helpers import data, fail, signup

URL = "/v1/users/me/calibration"


def upload(speed=8.0, device="iPhone15,4") -> dict:
    return {
        "points": [{"speedKmh": 4.0, "gait": "WALK", "durationS": 20}, {"speedKmh": speed, "gait": "RUN", "durationS": 20}],
        "model": {"version": 1, "points": [{"v": speed, "limp": 2.4, "cad": 158, "amp": 0.62, "running": True}]},
        "device": device,
    }


def test_처음엔_보정이_없다(db_client):
    me = signup(db_client, "k_1")
    assert data(db_client.get(URL, headers=me)) is None


def test_올린_보정을_그대로_돌려준다(db_client):
    me = signup(db_client, "k_1")
    body = upload()
    saved = data(db_client.put(URL, json=body, headers=me))
    assert {k: saved[k] for k in ("points", "model", "device")} == body  # model은 해석하지 않고 그대로
    assert saved["createdAt"].endswith("Z")
    assert data(db_client.get(URL, headers=me)) == saved


def test_다시_올리면_가장_최근_것이_현재_보정(db_client):
    me = signup(db_client, "k_1")
    db_client.put(URL, json=upload(speed=8.0, device="iPhone15,4"), headers=me)
    db_client.put(URL, json=upload(speed=9.0, device="iPhone16,1"), headers=me)  # 같은 초에 올려도 나중 것
    now = data(db_client.get(URL, headers=me))
    assert now["device"] == "iPhone16,1" and now["points"][1]["speedKmh"] == 9.0


def test_남의_보정은_안_보인다(db_client):
    a = signup(db_client, "k_1")
    b = signup(db_client, "k_2")
    db_client.put(URL, json=upload(), headers=a)
    assert data(db_client.get(URL, headers=b)) is None


def test_형식이_틀리면_거절(db_client):
    me = signup(db_client, "k_1")
    bad = upload()
    bad["points"] = []
    fail(db_client.put(URL, json=bad, headers=me), 400, "VALIDATION_ERROR")
    assert data(db_client.get(URL, headers=me)) is None  # 저장 안 됨


def test_로그인_없이는_안_됨(db_client):
    fail(db_client.get(URL), 401, "UNAUTHORIZED")
    fail(db_client.put(URL, json=upload()), 401, "UNAUTHORIZED")


def test_보정을_올리면_맵에_들어갈_수_있다(db_client):
    me = signup(db_client, "k_1")
    fail(db_client.post("/v1/courses/crs_lake_loop/entries", headers=me), 409, "CALIBRATION_REQUIRED")
    db_client.put(URL, json=upload(), headers=me)
    r = db_client.post("/v1/courses/crs_lake_loop/entries", headers=me)
    assert r.status_code == 201, r.text
