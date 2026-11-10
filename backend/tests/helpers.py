"""진짜 API 테스트가 같이 쓰는 도우미(응답 껍데기 확인, 토큰 헤더, 동시 요청)."""
import threading

from fastapi.testclient import TestClient

from app.main import app


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


def run_together(calls):
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
