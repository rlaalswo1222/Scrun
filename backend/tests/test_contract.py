"""스텁 계약 테스트: 모든 API가 공통 규칙(docs/api/README.md)대로 답하는지.

실행: backend 폴더에서 `python -m pytest`
"""
import re
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.core.errors import ErrorCode
from app.main import app
from app.stub_requests import CALLS, REAL

client = TestClient(app)
AUTH = {"Authorization": "Bearer demo"}
README = Path(__file__).resolve().parents[2] / "docs" / "api" / "README.md"

PUBLIC = {"/v1/auth/kakao", "/v1/agreements", "/v1/auth/refresh"}
# 진짜가 된 API는 tests/test_auth.py 등에서 실제 흐름으로 확인. 여기선 스텁만
STUB_CALLS = [c for c in CALLS if (c[0], c[1].split("?")[0]) not in REAL]


def call(method, path, body, headers=None):
    if body == "PHOTO":
        return client.request(method, path, headers=headers, files={"photo": ("console.jpg", b"\xff\xd8fake", "image/jpeg")})
    return client.request(method, path, headers=headers, json=body)


def template(path: str) -> str:
    """실제 주소 → OpenAPI 경로 모양(/v1/runs/run_01HZX → /v1/runs/{runId})."""
    path = path.split("?")[0]
    paths = app.openapi()["paths"]
    if path in paths:
        return path
    return next((p for p in paths if re.fullmatch(re.sub(r"\{[^/]+\}", "[^/]+", p), path)), path)


def assert_success(r, status):
    assert r.status_code == status, r.text
    if status == 204:
        assert r.content == b""
        return
    j = r.json()
    assert j["resultType"] == "SUCCESS" and j["error"] is None and "data" in j["success"]
    assert_meta(j, r)


def assert_fail(r, status, code):
    assert r.status_code == status, r.text
    j = r.json()
    assert j["resultType"] == "FAIL" and j["success"] is None
    assert j["error"]["code"] == code
    assert_meta(j, r)


def assert_meta(j, r):
    m = j["meta"]
    assert m["timestamp"].endswith("Z"), "시각은 UTC(Z)"
    assert m["requestId"].startswith("req_") and r.headers["X-Request-Id"] == m["requestId"]
    assert m["path"] == r.request.url.path


def test_all_operations_listed():
    ops = {(m.upper(), p) for p, item in app.openapi()["paths"].items() for m in item}
    listed = {(m, template(p)) for m, p, _, _ in CALLS}
    assert ops == listed, f"CALLS에 빠짐: {ops - listed}, 없는 API: {listed - ops}"


@pytest.mark.parametrize("method,path,body,status", STUB_CALLS, ids=[f"{m} {p}" for m, p, _, _ in STUB_CALLS])
def test_success_envelope(method, path, body, status):
    assert_success(call(method, path, body, AUTH), status)


@pytest.mark.parametrize("method,path,body,status", [c for c in STUB_CALLS if c[1].split("?")[0] not in PUBLIC],
                         ids=[f"{m} {p}" for m, p, _, _ in STUB_CALLS if p.split("?")[0] not in PUBLIC])
def test_requires_login(method, path, body, status):
    assert_fail(call(method, path, body), 401, "UNAUTHORIZED")


def test_expired_and_signup_tokens():
    assert_fail(client.get("/v1/users/me", headers={"Authorization": "Bearer expired"}), 401, "TOKEN_EXPIRED")
    assert_fail(client.get("/v1/users/me", headers={"Authorization": "Bearer signup"}), 403, "SIGNUP_REQUIRED")


def test_validation_is_400_with_fields():
    r = client.patch("/v1/users/me", headers=AUTH, json={"heightCm": 500})
    assert_fail(r, 400, "VALIDATION_ERROR")
    assert any(f["field"] == "heightCm" for f in r.json()["error"]["details"]["fields"])


def test_unknown_path_and_method():
    assert_fail(client.get("/v1/nope", headers=AUTH), 404, "NOT_FOUND")
    assert_fail(client.put("/v1/courses", headers=AUTH), 405, "METHOD_NOT_ALLOWED")


@pytest.mark.parametrize("method,path,body,status,code", [
    ("GET", "/v1/courses/crs_x", None, 404, "COURSE_NOT_FOUND"),
    ("POST", "/v1/courses/crs_full/entries", None, 409, "COURSE_FULL"),
    ("POST", "/v1/courses/crs_nocalib/entries", None, 409, "CALIBRATION_REQUIRED"),
    ("GET", "/v1/runs/run_x", None, 404, "RUN_NOT_FOUND"),
    ("GET", "/v1/runs/run_other", None, 403, "FORBIDDEN"),
    ("POST", "/v1/friends", {"friendCode": "SCR-7K2Q"}, 400, "SELF_FRIEND_CODE"),
    ("POST", "/v1/friends", {"friendCode": "SCR-NONE"}, 404, "FRIEND_CODE_NOT_FOUND"),
    ("POST", "/v1/friends", {"friendCode": "SCR-FRND"}, 409, "ALREADY_FRIEND"),
    ("POST", "/v1/friends", {"friendCode": "SCR-FULL"}, 409, "FRIEND_LIMIT_EXCEEDED"),
    ("POST", "/v1/friends/usr_f2/run-together", None, 409, "FRIEND_NOT_RUNNING"),
    ("PUT", "/v1/users/me/impact", {"impactId": "imp_aurora"}, 400, "VALIDATION_ERROR"),
])
def test_stub_error_triggers(method, path, body, status, code):
    assert_fail(call(method, path, body, AUTH), status, code)


def test_retake_is_not_an_error():
    r = client.post("/v1/runs/run_01J00/verification/photo", headers=AUTH,
                    files={"photo": ("blur.jpg", b"\xff\xd8", "image/jpeg")})
    assert_success(r, 200)
    assert r.json()["success"]["data"]["status"] == "RETAKE_REQUIRED"


def test_error_codes_match_readme():
    """docs/api/README.md 오류 코드 표와 ErrorCode가 같아야 한다."""
    documented = set(re.findall(r"^\| `([A-Z_]+)` \|", README.read_text(encoding="utf-8"), re.M))
    assert documented == {c.value for c in ErrorCode}
