"""OpenAPI 설명서와 예시 응답 JSON을 docs/api/에 내보낸다(앱 개발 참고·노션 확인용).

실행: backend 폴더에서 `python -m scripts.export_openapi` (진짜 API 예시 때문에 `docker compose up -d` 필요)
- docs/api/openapi.json: 표준 설명서(서버 코드에서 자동)
- docs/api/examples/<동작>_<주소>.json: 정상 응답 예시(요청 본문 포함)
- docs/api/examples/errors/<오류 코드>.json: 오류 응답 예시
시각·requestId는 다시 내보내도 바뀌지 않게 고정값으로 바꾼다.
"""
from app.testenv import use_test_settings

use_test_settings()  # app보다 먼저(임시 서명 키 + 가짜 카카오)

import json
import re
from pathlib import Path

from fastapi.testclient import TestClient

from app.core.errors import ERRORS, ErrorCode
from app.main import app
from app.stub_requests import CALLS, real_key
from scripts.real_examples import collect as collect_real

OUT = Path(__file__).resolve().parents[2] / "docs" / "api"
FIXED_META = {"timestamp": "2026-11-06T00:00:00Z", "requestId": "req_example"}


def _slug(method: str, path: str) -> str:
    path = path.split("?")[0].removeprefix("/v1/")
    return f"{method}_" + re.sub(r"[^A-Za-z0-9]+", "_", path).strip("_")


def _fix_meta(body: dict) -> dict:
    body["meta"].update(FIXED_META)
    return body


def _write(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    # 줄바꿈은 어느 OS에서 만들어도 CRLF(저장소에 처음 올라간 Windows 출력과 같게, 맥에서 다시 만들어도 전 파일이 바뀌지 않게)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\r\n")


def main() -> None:
    _write(OUT / "openapi.json", app.openapi())

    client = TestClient(app)
    auth = {"Authorization": "Bearer demo"}
    examples = OUT / "examples"
    for old in examples.rglob("*.json"):
        old.unlink()

    real = collect_real()  # 진짜 API는 실제 흐름(테스트 DB)으로
    for method, path, body, status in CALLS:
        key = real_key(method, path)
        if key is not None:
            ex = real[key]
            _write(examples / f"{_slug(method, path)}.json", {
                "request": {"method": method, "path": path, "auth": ex["auth"], "body": ex["body"]},
                "status": ex["status"],
                "response": ex["response"],
            })
            continue
        if body == "PHOTO":
            r = client.request(method, path, headers=auth, files={"photo": ("console.jpg", b"\xff\xd8", "image/jpeg")})
            request_body = "(multipart: photo=계기판 사진 JPEG)"
        else:
            r = client.request(method, path, headers=auth, json=body)
            request_body = body
        assert r.status_code == status, (method, path, r.text)
        _write(examples / f"{_slug(method, path)}.json", {
            "request": {"method": method, "path": path, "body": request_body},
            "status": status,
            "response": _fix_meta(r.json()) if r.content else None,
        })

    for code in ErrorCode:
        status, message = ERRORS[code]
        _write(examples / "errors" / f"{code.value}.json", {
            "status": status,
            "response": {
                "resultType": "FAIL",
                "success": None,
                "error": {"code": code.value, "message": message, "details": None},
                "meta": {**FIXED_META, "path": "/v1/..."},
            },
        })
    print(f"openapi.json + 예시 {len(CALLS)}개 + 오류 {len(ErrorCode)}개 → {OUT}")


if __name__ == "__main__":
    main()
