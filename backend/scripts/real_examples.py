"""진짜로 구현된 API의 예시를 실제 흐름으로 만든다(테스트 DB + 가짜 카카오). export_openapi·notion_pages가 쓴다.

흐름: 카카오 로그인(처음) → 약관 → 닉네임 확인 → 가입 → 내 정보·설정 조회·수정 → 코스 목록·상세 → 보정 저장·조회 → 맵 입장 →
러닝 시작 → 시계열 올리기 → 끝나지 않은 러닝 → 종료 → 계기판 사진 인증(가짜 OCR) → (다른 러닝 시작 → 버리기) →
(또 다른 러닝 → 종료 → 인증 없이 마무리) → 기록 목록·상세·공유 끄기·고스트 → 성장 → 주간 목표 정하기·바꾸기 →
친구(두 번째 사람이 내 코드로 추가 → 목록·달리는 친구·프로필·피드 → 삭제) → 토큰 재발급 → 로그아웃.
토큰 값·저장 시각·사용자 ID·친구 코드는 매번 바뀌므로 고정 값으로 바꿔 넣는다. 입장 위치(랜덤)는 시드를 고정한 난수로 바꿔 끼워 매번 같게.
"""
from app.testenv import use_test_settings

use_test_settings()  # app보다 먼저(임시 서명 키 + 가짜 카카오)

import random
import re
from datetime import datetime, timezone

from sqlalchemy import update

from fastapi.testclient import TestClient

from app.core.ocr import FakeOcr, get_ocr
from app.db.models import CourseEntry
from app.db.testing import TestDatabase
from app.main import app
from app.realtime.hub import hub
from app.routers.courses import get_rng
from app.routers.users import get_now
from app.stub_requests import REAL

FIXED_META = {"timestamp": "2026-11-06T00:00:00Z", "requestId": "req_example"}
DEVICE_ID = "6F1A2B3C-0000-4000-8000-000000000001"  # 예시 identifierForVendor
PLACEHOLDER = {"accessToken": "eyJhbGciOi...(access)", "refreshToken": "(refresh 토큰 원문)", "signupToken": "eyJhbGciOi...(signup)",
               "userId": "01JC0Z8H3K6Q2W9EXAMPLE0000", "friendCode": "K7Q2M9XA", "runId": "01JC0ZA4R7N2X5EXAMPLERUN00",
               "attemptId": "01JC0ZB2M5P8T1EXAMPLEVER00"}


_ULID = re.compile(r"/[0-9A-HJKMNP-TV-Z]{26}(?=/|$)")  # 주소 안의 기록 ID(db.ids.new_id)


def _clean_path(path: str) -> str:
    """실제 요청 주소의 기록·사용자 ID를 예시 값으로(다시 만들 때마다 바뀌지 않게)."""
    return _ULID.sub("/" + PLACEHOLDER["userId" if path.startswith("/v1/friends/") else "runId"], path)


def _clean(j):
    if j is None:
        return None
    j["meta"].update(FIXED_META)
    j["meta"]["path"] = _clean_path(j["meta"]["path"])
    data = (j.get("success") or {}).get("data")
    for row in data if isinstance(data, list) else [data]:  # 친구 목록처럼 배열인 응답도 줄마다
        if not isinstance(row, dict):
            continue
        for k, v in PLACEHOLDER.items():
            if row.get(k):
                row[k] = v
        if row.get("feedId"):
            row["feedId"] = PLACEHOLDER["runId"]
    if isinstance(data, dict):
        rt = data.get("realtime")
        if isinstance(rt, dict) and rt.get("token"):  # 실시간 토큰도 다시 만들 때마다 바뀜
            rt["token"] = "eyJhbGciOi...(realtime)"
        near = data.get("nearFriend")  # 같이 달리기: 친구 ID
        if isinstance(near, dict) and near.get("userId"):
            near["userId"] = PLACEHOLDER["userId"]
        if data.get("createdAt"):  # 저장 시각(보정 등)도 다시 만들 때마다 바뀌지 않게
            data["createdAt"] = FIXED_META["timestamp"]
        if data.get("retakeDeadline"):  # 인증 마감(다시 찍기·값 확인)도
            data["retakeDeadline"] = "2026-11-06T00:10:00Z"
        for item in (data.get("items") or []) + (data.get("recentRuns") or []) + (data.get("runningFriends") or []):
            # 기록 목록·피드·프로필·홈: 줄마다 ID
            if isinstance(item, dict):
                for k in ("runId", "feedId"):
                    if item.get(k):
                        item[k] = PLACEHOLDER["runId"]
                if item.get("userId"):
                    item["userId"] = PLACEHOLDER["userId"]
        run = data.get("run")  # 끝나지 않은 러닝: 기록 ID·복구 기한(마지막 갱신 + 24시간)
        if isinstance(run, dict):
            run["runId"] = PLACEHOLDER["runId"]
            data["recoverableUntil"] = "2026-11-07T10:01:00Z"
            data["lastActiveAt"] = "2026-11-06T10:01:00Z"
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
            out[(method, path)] = {"path": _clean_path(r.request.url.path), "body": body, "auth": auth, "status": r.status_code,
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
        rec("GET", "/v1/users/me", None, "Bearer {accessToken}", c.get("/v1/users/me", headers=auth))
        body = {"nickname": "아침러너", "weightKg": 68}
        rec("PATCH", "/v1/users/me", body, "Bearer {accessToken}", c.patch("/v1/users/me", json=body, headers=auth))
        rec("GET", "/v1/users/me/settings", None, "Bearer {accessToken}", c.get("/v1/users/me/settings", headers=auth))
        body = {"feedShareDefault": False}
        rec("PATCH", "/v1/users/me/settings", body, "Bearer {accessToken}",
            c.patch("/v1/users/me/settings", json=body, headers=auth))
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
        # 러닝: 시작 → 1분 묶음 2개 → 끝나지 않은 러닝 → 종료. 시각은 예시 값이라 입장 시각을 그 앞으로 돌림(시작은 입장 뒤여야 함)
        with tdb.Session() as s:
            s.execute(update(CourseEntry).values(entered_at=datetime(2026, 11, 6, 9, 55, tzinfo=timezone.utc)))
            s.commit()
        body = {"clientRunId": "c-2026-11-06-001", "mapInstanceId": "map_crs_lake_loop",
                "goal": {"type": "DISTANCE", "value": 3000}, "startedAt": "2026-11-06T10:00:00Z"}
        run_id = rec("POST", "/v1/runs", body, "Bearer {accessToken}",
                     c.post("/v1/runs", json=body, headers=auth))["success"]["data"]["runId"]
        for i, v in enumerate((8.0, 8.4)):
            body = {"offsetS": 60.0 * i, "intervalS": 1.0, "speedsKmh": [v] * 60}
            rec("PUT", "/v1/runs/{runId}/samples", body, "Bearer {accessToken}",
                c.put(f"/v1/runs/{run_id}/samples", json=body, headers=auth))
        rec("GET", "/v1/runs/active", None, "Bearer {accessToken}", c.get("/v1/runs/active", headers=auth))
        body = {"endedAt": "2026-11-06T10:19:30Z", "sensor": {"distanceM": 3120, "durationS": 1080}}
        rec("POST", "/v1/runs/{runId}/finish", body, "Bearer {accessToken}",
            c.post(f"/v1/runs/{run_id}/finish", json=body, headers=auth))
        # 계기판 사진: 가짜 OCR은 파일 이름(d거리_t시간)으로 값을 정함. 브래킷 3장(노출만 다름)
        app.dependency_overrides[get_ocr] = FakeOcr
        try:
            shots = [("photos", (f"console_{i}_d3150_t1140.jpg", b"\xff\xd8\xff" + bytes([i]), "image/jpeg")) for i in range(3)]
            shown = rec("POST", "/v1/runs/{runId}/verification/photo", "(multipart: photos=계기판 사진 JPEG, 브래킷 3장)",
                        "Bearer {accessToken}", c.post(f"/v1/runs/{run_id}/verification/photo", files=shots, headers=auth))
            # 읽은 값 확인(계획 0045): 맞아요 → 지금 판정
            body = {"attemptId": shown["success"]["data"]["attemptId"], "correct": True}
            rec("POST", "/v1/runs/{runId}/verification/confirm", {**body, "attemptId": PLACEHOLDER["attemptId"]},
                "Bearer {accessToken}", c.post(f"/v1/runs/{run_id}/verification/confirm", json=body, headers=auth))
        finally:
            app.dependency_overrides.pop(get_ocr, None)
        other = c.post("/v1/runs", headers=auth, json={"clientRunId": "c-2026-11-06-002", "mapInstanceId": "map_crs_lake_loop",
                                                        "goal": {"type": "NONE"}, "startedAt": "2026-11-06T11:00:00Z"})
        rec("POST", "/v1/runs/{runId}/discard", None, "Bearer {accessToken}",
            c.post(f"/v1/runs/{other.json()['success']['data']['runId']}/discard", headers=auth))
        third = c.post("/v1/runs", headers=auth, json={"clientRunId": "c-2026-11-06-003", "mapInstanceId": "map_crs_lake_loop",
                                                        "goal": {"type": "NONE"}, "startedAt": "2026-11-06T12:00:00Z"})
        third_id = third.json()["success"]["data"]["runId"]
        c.post(f"/v1/runs/{third_id}/finish", headers=auth,
               json={"endedAt": "2026-11-06T12:15:00Z", "sensor": {"distanceM": 2400, "durationS": 900}})
        body = {"reason": "SKIPPED"}
        rec("POST", "/v1/runs/{runId}/verification/skip", body, "Bearer {accessToken}",
            c.post(f"/v1/runs/{third_id}/verification/skip", json=body, headers=auth))
        # 기록 읽기(계획 0043): 목록 → 상세(사진 인증한 첫 러닝) → 공유 끄기 → 고스트
        rec("GET", "/v1/runs", None, "Bearer {accessToken}", c.get("/v1/runs", params={"month": "2026-11"}, headers=auth))
        rec("GET", "/v1/runs/{runId}", None, "Bearer {accessToken}", c.get(f"/v1/runs/{run_id}", headers=auth))
        body = {"shared": False}
        rec("PATCH", "/v1/runs/{runId}", body, "Bearer {accessToken}",
            c.patch(f"/v1/runs/{run_id}", json=body, headers=auth))
        rec("GET", "/v1/runs/{runId}/ghost", None, "Bearer {accessToken}", c.get(f"/v1/runs/{run_id}/ghost", headers=auth))
        # 성장(계획 0048): 사진 인증한 첫 러닝의 반영 결과 → 레벨 → 임팩트 끄기(목록이 정해지기 전이라 null만)
        rec("GET", "/v1/runs/{runId}/rewards", None, "Bearer {accessToken}", c.get(f"/v1/runs/{run_id}/rewards", headers=auth))
        rec("GET", "/v1/users/me/progress", None, "Bearer {accessToken}", c.get("/v1/users/me/progress", headers=auth))
        body = {"impactId": None}
        rec("PUT", "/v1/users/me/impact", body, "Bearer {accessToken}", c.put("/v1/users/me/impact", json=body, headers=auth))
        # 주간 목표(계획 0054): 11/6에 주 3회로 정함(사진 인증한 첫 러닝이 1회) → 4회로 바꿈(다음 주기부터). "오늘"을 예시 날로 고정
        app.dependency_overrides[get_now] = lambda: datetime(2026, 11, 6, 12, 0, tzinfo=timezone.utc)
        try:
            c.put("/v1/users/me/weekly-goal", json={"targetCount": 3}, headers=auth)
            body = {"targetCount": 4}
            rec("PUT", "/v1/users/me/weekly-goal", body, "Bearer {accessToken}",
                c.put("/v1/users/me/weekly-goal", json=body, headers=auth))
            rec("GET", "/v1/users/me/weekly-goal", None, "Bearer {accessToken}", c.get("/v1/users/me/weekly-goal", headers=auth))
        finally:
            app.dependency_overrides.pop(get_now, None)
        # 친구(계획 0044): 두 번째 사람이 내 코드로 추가 → 둘 다 친구. 피드에 보이게 세 번째 러닝은 공유를 켬(설정에서 기본값을 껐음)
        c.patch(f"/v1/runs/{third_id}", json={"shared": True}, headers=auth)
        my = c.get("/v1/users/me", headers=auth).json()["success"]["data"]
        k2 = c.post("/v1/auth/kakao", json={"kakaoAccessToken": "k_2"}).json()["success"]["data"]
        r2 = c.post("/v1/auth/signup", headers={"Authorization": f"Bearer {k2['signupToken']}"},
                    json={"agreedIds": ["terms", "privacy", "age14"], "nickname": "주말러너", "heightCm": 168, "weightKg": 60})
        auth2 = {"Authorization": f"Bearer {r2.json()['success']['data']['accessToken']}"}
        body = {"friendCode": my["friendCode"]}
        rec("POST", "/v1/friends", {"friendCode": "K7Q2M9XA"}, "Bearer {accessToken}",
            c.post("/v1/friends", json=body, headers=auth2))
        rec("GET", "/v1/friends", None, "Bearer {accessToken}", c.get("/v1/friends", headers=auth2))
        rec("GET", "/v1/friends/running", None, "Bearer {accessToken}", c.get("/v1/friends/running", headers=auth2))
        # 같이 달리기(계획 0047): 첫 사람이 섬에서 달리는 중 → 두 번째 사람(보정 있음)이 그 뒤로 입장
        c.put("/v1/users/me/calibration", json=cal, headers=auth2)
        conn, _ = hub.join("map_crs_lake_loop", "crs_lake_loop", 6670.0, my["userId"], my["nickname"])
        hub.update(conn, 1240.0, 10.0, True, run=820.0)
        app.dependency_overrides[get_rng] = lambda: random.Random(21)
        try:
            body = {"courseId": "crs_lake_loop"}
            rec("POST", "/v1/friends/{userId}/run-together", body, "Bearer {accessToken}",
                c.post(f"/v1/friends/{my['userId']}/run-together", json=body, headers=auth2))
        finally:
            app.dependency_overrides.pop(get_rng, None)
            hub.leave(conn, intentional=True)
        # 홈(계획 0057): 친구(두 번째 사람)가 섬에서 달리는 중일 때 첫 사람의 홈. "오늘"은 주간 목표와 같은 예시 날
        me2 = c.get("/v1/users/me", headers=auth2).json()["success"]["data"]
        conn2, _ = hub.join("map_crs_lake_loop", "crs_lake_loop", 6670.0, me2["userId"], me2["nickname"])
        hub.update(conn2, 900.0, 9.5, True, run=1800.0)
        app.dependency_overrides[get_now] = lambda: datetime(2026, 11, 6, 12, 0, tzinfo=timezone.utc)
        try:
            rec("GET", "/v1/home", None, "Bearer {accessToken}", c.get("/v1/home", headers=auth))
        finally:
            app.dependency_overrides.pop(get_now, None)
            hub.leave(conn2, intentional=True)
        rec("GET", "/v1/friends/{userId}", None, "Bearer {accessToken}", c.get(f"/v1/friends/{my['userId']}", headers=auth2))
        rec("GET", "/v1/feed", None, "Bearer {accessToken}", c.get("/v1/feed", headers=auth2))
        rec("DELETE", "/v1/friends/{userId}", None, "Bearer {accessToken}",
            c.delete(f"/v1/friends/{my['userId']}", headers=auth2))
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
