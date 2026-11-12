"""진짜로 구현된 API의 예시를 실제 흐름으로 만든다(테스트 DB + 가짜 카카오). export_openapi·notion_pages가 쓴다.

흐름: 카카오 로그인(처음) → 약관 → 닉네임 확인 → 가입 → 코스 목록·상세 → 보정 저장·조회 → 맵 입장 → 토큰 재발급 → 로그아웃.
토큰 값·저장 시각은 매번 바뀌므로 고정 값으로 바꿔 넣는다. 입장 위치(랜덤)는 시드를 고정한 난수로 바꿔 끼워 매번 같게.
"""
from app.testenv import use_test_settings

use_test_settings()  # app보다 먼저(임시 서명 키 + 가짜 카카오)

import random

from fastapi.testclient import TestClient

from app.db.testing import TestDatabase
from app.main import app
from app.routers.courses import get_rng
from app.stub_requests import REAL

FIXED_META = {"timestamp": "2026-11-06T00:00:00Z", "requestId": "req_example"}
DEVICE_ID = "6F1A2B3C-0000-4000-8000-000000000001"  # 예시 identifierForVendor
PLACEHOLDER = {"accessToken": "eyJhbGciOi...(access)", "refreshToken": "(refresh 토큰 원문)", "signupToken": "eyJhbGciOi...(signup)"}


def _clean(j):
    if j is None:
        return None
    j["meta"].update(FIXED_META)
    data = (j.get("success") or {}).get("data")
    if isinstance(data, dict):
        for k, v in PLACEHOLDER.items():
            if data.get(k):
                data[k] = v
        rt = data.get("realtime")
        if isinstance(rt, dict) and rt.get("token"):  # 실시간 토큰도 다시 만들 때마다 바뀜
            rt["token"] = "eyJhbGciOi...(realtime)"
        if data.get("createdAt"):  # 저장 시각(보정 등)도 다시 만들 때마다 바뀌지 않게
            data["createdAt"] = FIXED_META["timestamp"]
    return j


def collect() -> dict:
    """{(동작, 주소 틀): {"body": 요청 본문, "auth": 헤더 설명, "status": 상태, "response": 응답}}"""
    tdb = TestDatabase(app).start()
    try:
        tdb.reset()
        c = TestClient(app)
        out = {}

        def rec(method, path, body, auth, r):
            if r.status_code >= 400:  # 실패한 응답을 예시로 남기지 않게
                raise RuntimeError(f"예시 흐름 실패: {method} {path} → {r.status_code} {r.text}")
            out[(method, path)] = {"body": body, "auth": auth, "status": r.status_code,
                                   "response": _clean(r.json()) if r.content else None}
            return r.json() if r.content else None

        body = {"kakaoAccessToken": "k_1", "deviceId": DEVICE_ID}
        first = rec("POST", "/v1/auth/kakao", body, None, c.post("/v1/auth/kakao", json=body))["success"]["data"]
        rec("GET", "/v1/agreements", None, None, c.get("/v1/agreements"))
        rec("GET", "/v1/users/nickname/check", None, "Bearer {accessToken 또는 signupToken}",
            c.get("/v1/users/nickname/check", params={"nickname": "새벽러너"},
                  headers={"Authorization": f"Bearer {first['signupToken']}"}))
        body = {"agreedIds": ["terms", "privacy", "age14"], "nickname": "새벽러너", "heightCm": 175, "weightKg": 70,
                "timezone": "Asia/Seoul", "deviceId": DEVICE_ID}
        tok = rec("POST", "/v1/auth/signup", body, "Bearer {signupToken}",
                  c.post("/v1/auth/signup", json=body, headers={"Authorization": f"Bearer {first['signupToken']}"}))
        tok = tok["success"]["data"]
        auth = {"Authorization": f"Bearer {tok['accessToken']}"}
        rec("GET", "/v1/courses", None, "Bearer {accessToken}", c.get("/v1/courses", headers=auth))
        rec("GET", "/v1/courses/{courseId}", None, "Bearer {accessToken}", c.get("/v1/courses/crs_lake_loop", headers=auth))
        cal = {  # 예시 값(실측 아님). model은 폰 SpeedEstimator가 만든 것을 서버는 해석하지 않고 보관
            "points": [{"speedKmh": 4.0, "gait": "WALK", "durationS": 20}, {"speedKmh": 8.0, "gait": "RUN", "durationS": 20}],
            "model": {"version": 1, "points": [{"v": 4.0, "limp": 0.9, "cad": 112, "amp": 0.31, "running": False},
                                               {"v": 8.0, "limp": 2.4, "cad": 158, "amp": 0.62, "running": True}]},
            "device": "iPhone15,4",
        }
        rec("PUT", "/v1/users/me/calibration", cal, "Bearer {accessToken}",
            c.put("/v1/users/me/calibration", json=cal, headers=auth))
        rec("GET", "/v1/users/me/calibration", None, "Bearer {accessToken}", c.get("/v1/users/me/calibration", headers=auth))
        app.dependency_overrides[get_rng] = lambda: random.Random(21)
        try:
            rec("POST", "/v1/courses/{courseId}/entries", None, "Bearer {accessToken}",
                c.post("/v1/courses/crs_lake_loop/entries", headers=auth))
        finally:
            app.dependency_overrides.pop(get_rng, None)
        body = {"refreshToken": tok["refreshToken"]}
        tok2 = rec("POST", "/v1/auth/refresh", {"refreshToken": PLACEHOLDER["refreshToken"]}, None,
                   c.post("/v1/auth/refresh", json=body))["success"]["data"]
        rec("POST", "/v1/auth/logout", {"refreshToken": PLACEHOLDER["refreshToken"]}, "Bearer {accessToken}",
            c.post("/v1/auth/logout", json={"refreshToken": tok2["refreshToken"]},
                   headers={"Authorization": f"Bearer {tok2['accessToken']}"}))
        missing = REAL - set(out)
        assert not missing, f"real_examples에 흐름이 없는 진짜 API: {missing}"
        tdb.reset()
        return out
    finally:
        tdb.stop()
