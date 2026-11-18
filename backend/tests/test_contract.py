"""스텁 계약 테스트: 모든 API가 공통 규칙(docs/api/README.md)대로 답하는지.

실행: backend 폴더에서 `python -m pytest`
"""
import re
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.core.errors import ErrorCode
from app.main import app
from app.stub_requests import CALLS, real_key

client = TestClient(app)
AUTH = {"Authorization": "Bearer demo"}
README = Path(__file__).resolve().parents[2] / "docs" / "api" / "README.md"

PUBLIC = {"/v1/auth/kakao", "/v1/agreements", "/v1/auth/refresh"}
# 진짜가 된 API는 tests/test_auth.py 등에서 실제 흐름으로 확인. 여기선 스텁만
STUB_CALLS = [c for c in CALLS if real_key(c[0], c[1]) is None]


def call(method, path, body, headers=None):
    if body == "PHOTO":
        return client.request(method, path, headers=headers, files={"photos": ("console.jpg", b"\xff\xd8fake", "image/jpeg")})
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


# 아직 스텁인 API로 본다(내 정보 /users/me는 계획 0028에서 진짜가 됨 → tests/test_me.py)
def test_expired_and_signup_tokens():
    assert_fail(client.get("/v1/users/me/weekly-goal", headers={"Authorization": "Bearer expired"}), 401, "TOKEN_EXPIRED")
    assert_fail(client.get("/v1/users/me/weekly-goal", headers={"Authorization": "Bearer signup"}), 403, "SIGNUP_REQUIRED")


def test_validation_is_400_with_fields():
    r = client.put("/v1/users/me/weekly-goal", headers=AUTH, json={"targetCount": 9})
    assert_fail(r, 400, "VALIDATION_ERROR")
    assert any(f["field"] == "targetCount" for f in r.json()["error"]["details"]["fields"])


def test_unknown_path_and_method():
    assert_fail(client.get("/v1/nope", headers=AUTH), 404, "NOT_FOUND")
    assert_fail(client.put("/v1/courses", headers=AUTH), 405, "METHOD_NOT_ALLOWED")


@pytest.mark.parametrize("method,path,body,status,code", [
    ("GET", "/v1/runs/run_x/rewards", None, 404, "RUN_NOT_FOUND"),
    ("GET", "/v1/runs/run_other/rewards", None, 403, "FORBIDDEN"),
    ("PUT", "/v1/users/me/impact", {"impactId": "imp_aurora"}, 400, "VALIDATION_ERROR"),
])
def test_stub_error_triggers(method, path, body, status, code):
    assert_fail(call(method, path, body, AUTH), status, code)


def test_error_codes_match_readme():
    """docs/api/README.md 오류 코드 표와 ErrorCode가 같아야 한다."""
    documented = set(re.findall(r"^\| `([A-Z_]+)` \|", README.read_text(encoding="utf-8"), re.M))
    assert documented == {c.value for c in ErrorCode}


def test_notion_pages_match_api():
    """노션 명세 페이지 표(scripts/notion_pages.py)와 실제 API가 같아야 한다(지운 API가 남으면 페이지 만들기가 깨짐)."""
    from scripts.notion_pages import NOTION_PAGE_IDS
    ops = {(m.upper(), p) for p, item in app.openapi()["paths"].items() for m in item}
    assert set(NOTION_PAGE_IDS) - ops == set(), "노션 표에만 있는 API"


def test_error_docs_are_not_merged_as_dicts():
    """문서의 오류 응답은 error_responses 한 번에 모아 만든다. {**a, **error_responses(...)}처럼 dict를 합치면 같은 상태 코드가
    덮어써져 코드가 문서에서 사라짐(11/18: confirm의 409 RUN_NOT_FINISHED·VERIFICATION_CLOSED, 기록 API의 403 SIGNUP_REQUIRED)."""
    import ast
    bad = []
    for f in sorted((Path(__file__).resolve().parents[1] / "app" / "routers").glob("*.py")):
        for node in ast.walk(ast.parse(f.read_text(encoding="utf-8"))):
            if isinstance(node, ast.keyword) and node.arg == "responses" and isinstance(node.value, ast.Dict) \
                    and None in node.value.keys:
                bad.append(f"{f.name}:{node.value.lineno}")
    assert not bad, f"responses에 dict 합치기: {bad} → error_responses(*AUTH, ...) 한 번에"


@pytest.mark.parametrize("method,path,status,codes", [
    ("post", "/v1/runs/{runId}/verification/confirm", "409", ["RUN_NOT_FINISHED", "VERIFICATION_CLOSED", "CONFIRM_NOT_PENDING"]),
    ("get", "/v1/runs/{runId}", "403", ["SIGNUP_REQUIRED", "FORBIDDEN"]),
    ("get", "/v1/runs/{runId}/rewards", "403", ["SIGNUP_REQUIRED", "FORBIDDEN"]),
])
def test_same_status_error_codes_are_all_documented(method, path, status, codes):
    desc = app.openapi()["paths"][path][method]["responses"][status]["description"]
    assert all(c in desc for c in codes), desc
