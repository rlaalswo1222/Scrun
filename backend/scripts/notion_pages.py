"""노션 API 명세 표의 각 줄 페이지 본문(DU 모양)을 서버 코드에서 자동으로 만든다.

실행: backend 폴더에서 `python -m scripts.notion_pages` → build/notion/<동작>_<주소>.md + index.json
노션에 올리는 건 Claude(노션 MCP)가 index.json의 pageId마다 본문을 덮어쓴다. 손으로 고치지 않는다(다시 만들면 덮어씀).
순서: 설명 → Request Header → Path·Query Parameter → Request Body → Response(성공) → Response(실패)
"""
from app.testenv import use_test_settings

use_test_settings()  # app보다 먼저(임시 서명 키 + 가짜 카카오)

import json
import re
from pathlib import Path

from fastapi.testclient import TestClient

from app.core.errors import ERRORS, ErrorCode
from app.main import app
from app.stub_requests import CALLS, REAL
from scripts.real_examples import collect as collect_real

OUT = Path(__file__).resolve().parents[1] / "build" / "notion"
FIXED_META = {"timestamp": "2026-11-06T00:00:00Z", "requestId": "req_example"}
PUBLIC = {"/v1/auth/kakao", "/v1/agreements", "/v1/auth/refresh"}
SIGNUP_OK = {"/v1/auth/signup", "/v1/users/nickname/check"}

REPO = Path(__file__).resolve().parents[2]
GITHUB_BLOB = "https://github.com/rlaalswo1222/Scrun/blob/develop/"
# 표 맨 위 "공통 규칙 (먼저 읽기)" 줄(순서 0). 본문은 docs/api/README.md에서 만든다
NOTION_COMMON_PAGE_ID = "4104ae82f1de81498364d7309a9a2e00"

# 노션 API 명세 표(SCRUN > 문서 > API 명세)의 줄 ↔ API. 새 API를 노션에 추가하면 여기에도
NOTION_PAGE_IDS = {
    ("POST", "/v1/auth/kakao"): "4104ae82f1de81c697e0f43099d9b126",
    ("GET", "/v1/agreements"): "4104ae82f1de817ebb39d9a75228d19e",
    ("POST", "/v1/auth/signup"): "4104ae82f1de81b1a47ad337a1970dd5",
    ("POST", "/v1/auth/refresh"): "4104ae82f1de817bb869c64f21c6f61f",
    ("POST", "/v1/auth/logout"): "4104ae82f1de810382a5cafc65022bf5",
    ("GET", "/v1/users/nickname/check"): "4104ae82f1de811280a8c765fb201547",
    ("GET", "/v1/users/me"): "4104ae82f1de8152b69bda820103f2f9",
    ("PATCH", "/v1/users/me"): "4104ae82f1de8171a281fa062ed41432",
    ("DELETE", "/v1/users/me"): "4104ae82f1de8190b744dfeede227655",
    ("GET", "/v1/users/me/settings"): "4104ae82f1de81a7a355f4a243f04005",
    ("PATCH", "/v1/users/me/settings"): "4104ae82f1de8110b19ff047576ace3f",
    ("GET", "/v1/users/me/calibration"): "4104ae82f1de81aeac5de41012696567",
    ("PUT", "/v1/users/me/calibration"): "4104ae82f1de81ed837fd9c8bd1756ed",
    ("GET", "/v1/users/me/weekly-goal"): "4104ae82f1de81a5bcacca76e4091b9e",
    ("PUT", "/v1/users/me/weekly-goal"): "4104ae82f1de81e78c13f563244b38ce",
    ("GET", "/v1/home"): "4104ae82f1de81b9b7c4fe6b8928ffef",
    ("GET", "/v1/courses"): "4104ae82f1de814bbf5ff08a3c825d4c",
    ("GET", "/v1/courses/{courseId}"): "4104ae82f1de818c89a2c797df5e5b5e",
    ("POST", "/v1/courses/{courseId}/entries"): "4104ae82f1de8159a03fd4737d2993f0",
    ("POST", "/v1/runs"): "4104ae82f1de81ae9df6c1c9dd69f098",
    ("PUT", "/v1/runs/{runId}/samples"): "4104ae82f1de811082e3cd467c77e900",
    ("POST", "/v1/runs/{runId}/finish"): "4104ae82f1de81bf8bd9f9e80cac74a9",
    ("GET", "/v1/runs/active"): "4104ae82f1de8137949ce40b96f46915",
    ("POST", "/v1/runs/{runId}/discard"): "4104ae82f1de81579012f779c08fe5cf",
    ("GET", "/v1/runs"): "4104ae82f1de815e94d3e6ef2a603ccd",
    ("GET", "/v1/runs/{runId}"): "4104ae82f1de81babf89de033231505b",
    ("PATCH", "/v1/runs/{runId}"): "4104ae82f1de819daa35eec86ba82f1b",
    ("GET", "/v1/runs/{runId}/ghost"): "4104ae82f1de812eacdefd526ae35b95",
    ("POST", "/v1/runs/{runId}/verification/photo"): "4104ae82f1de81428f89ddfdf174fd5d",
    ("POST", "/v1/runs/{runId}/verification/reading"): "4104ae82f1de8168ba52c78bf1d7f0ec",
    ("POST", "/v1/runs/{runId}/verification/skip"): "4104ae82f1de8101ba7cdc98df833a6f",
    ("GET", "/v1/runs/{runId}/rewards"): "4104ae82f1de81b989cacc09a6286375",
    ("GET", "/v1/users/me/progress"): "4104ae82f1de8122bea9dc92f863d4f2",
    ("PUT", "/v1/users/me/impact"): "4104ae82f1de81669a80dc9ffe10577a",
    ("POST", "/v1/friends"): "4104ae82f1de8107b26ec03cdf5c039f",
    ("GET", "/v1/friends"): "4104ae82f1de813fa0f0f4586bb50243",
    ("GET", "/v1/friends/{userId}"): "4104ae82f1de81f2a6dbd2f9fc569756",
    ("DELETE", "/v1/friends/{userId}"): "4104ae82f1de81b399c1f8e24ea10f69",
    ("GET", "/v1/feed"): "4104ae82f1de81baa3a2d39324d5d1ab",
    ("GET", "/v1/friends/running"): "4104ae82f1de81da8e26e50b951f98c8",
    ("POST", "/v1/friends/{userId}/run-together"): "4104ae82f1de81899b2ade07bb3f9969",
    ("POST", "/v1/events"): "4104ae82f1de81f7be15d5970444f779",
}


class Spec:
    def __init__(self, openapi: dict):
        self.s = openapi
        self.schemas = openapi["components"]["schemas"]

    def resolve(self, node: dict) -> dict:
        while "$ref" in node:
            node = self.schemas[node["$ref"].split("/")[-1]]
        return node

    def type_of(self, node: dict) -> str:
        node = self.resolve(node) if "$ref" in node else node
        if "anyOf" in node:
            parts = [self.type_of(n) for n in node["anyOf"] if n.get("type") != "null"]
            nullable = any(n.get("type") == "null" for n in node["anyOf"])
            return " \\| ".join(parts) + (" \\| null" if nullable else "")
        if "enum" in node:
            return " \\| ".join(str(v) for v in node["enum"])
        t = node.get("type", node.get("title", "object"))
        if t == "array":
            return f"{self.type_of(node.get('items', {}))}[]"
        if t == "string" and node.get("format") in ("date-time", "date"):
            return f"string({node['format']})"
        if t == "object" and "title" in node and node["title"] not in ("Model", "Props"):
            return node["title"]
        return t

    def rules(self, node: dict) -> str:
        node = node if "anyOf" not in node else next((n for n in node["anyOf"] if n.get("type") != "null"), node)
        out = []
        for k, label in (("minLength", "최소 {}자"), ("maxLength", "최대 {}자"), ("minimum", "≥ {}"), ("maximum", "≤ {}"),
                         ("exclusiveMinimum", "> {}"), ("minItems", "최소 {}개"), ("maxItems", "최대 {}개"), ("pattern", "형식 {}")):
            if k in node:
                v = node[k]
                v = int(v) if isinstance(v, float) and v.is_integer() else v
                out.append(label.format(f"`{v}`" if k == "pattern" else v))
        return ", ".join(out)

    def field_table(self, ref_or_schema: dict) -> str:
        node = self.resolve(ref_or_schema)
        props = node.get("properties", {})
        if not props:
            return ""
        req = set(node.get("required", []))
        rows = ["| 필드 | 형식 | 필수 | 설명 |", "|---|---|---|---|"]
        for name, p in props.items():
            desc = p.get("description", "")
            rule = self.rules(p)
            rows.append(f"| `{name}` | {self.type_of(p)} | {'예' if name in req else ''} | {_join(desc, rule)} |")
        return "\n".join(rows)

    def data_schema(self, op: dict):
        """성공 응답 껍데기 안 success.data의 스키마."""
        for status in ("200", "201"):
            r = op.get("responses", {}).get(status)
            if not r or "content" not in r:
                continue
            env = self.resolve(r["content"]["application/json"]["schema"])
            body = self.resolve(env["properties"]["success"])
            return body["properties"]["data"]
        return None


def _join(desc: str, rule: str) -> str:
    if not rule:
        return desc
    return f"{desc} ({rule})" if desc else rule


def _code(lang: str, text: str) -> str:
    return f"```{lang}\n{text}\n```"


def _slug(method: str, path: str) -> str:
    return f"{method}_" + re.sub(r"[^A-Za-z0-9]+", "_", path.removeprefix("/v1/")).strip("_")


def build_page(spec: Spec, method: str, path: str, call, client: TestClient, real: dict) -> str:
    op = spec.s["paths"][path][method.lower()]
    parts = []

    # 설명
    parts.append(f"## 설명\n\n<b>{op.get('summary', '')}</b>\n\n{op.get('description', '')}")

    # Request Header
    headers = ["Content-Type: application/json"]
    if (method, path) in real and real[(method, path)]["auth"]:
        headers.append("Authorization: " + real[(method, path)]["auth"])
    elif path not in PUBLIC and (method, path) not in real:
        headers.append("Authorization: Bearer {accessToken}" if path not in SIGNUP_OK
                       else "Authorization: Bearer {accessToken 또는 signupToken}")
    if call and call[2] == "PHOTO":
        headers[0] = "Content-Type: multipart/form-data"
    parts.append("## Request Header\n\n" + _code("plaintext", "\n".join(headers)))

    # Path·Query Parameter
    params = op.get("parameters", [])
    if params:
        rows = ["| 이름 | 위치 | 형식 | 필수 | 설명 |", "|---|---|---|---|---|"]
        for p in params:
            rule = spec.rules(p.get("schema", {}))
            rows.append(f"| `{p['name']}` | {p['in']} | {spec.type_of(p.get('schema', {}))} | {'예' if p.get('required') else ''} | "
                        f"{_join(p.get('description', ''), rule)} |")
        parts.append("## Path·Query Parameter\n\n" + "\n".join(rows))
    else:
        parts.append("## Path·Query Parameter\n\n없음")

    # Request Body
    rb = op.get("requestBody")
    if rb:
        content = rb["content"]
        if "multipart/form-data" in content:
            parts.append("## Request Body\n\nmultipart/form-data\n\n" + spec.field_table(content["multipart/form-data"]["schema"]))
        else:
            req = real[(method, path)]["body"] if (method, path) in real else (call[2] if call else {})
            example = json.dumps(req, ensure_ascii=False, indent=2)
            parts.append("## Request Body\n\n" + _code("json", example) + "\n\n" + spec.field_table(content["application/json"]["schema"]))
    else:
        parts.append("## Request Body\n\n없음")

    # Response(성공)
    status = real[(method, path)]["status"] if (method, path) in real else (call[3] if call else 200)
    if status == 204:
        parts.append("## Response (성공)\n\n204 No Content(본문 없음)")
    elif (method, path) in real:
        j = real[(method, path)]["response"]
        data_s = spec.data_schema(op)
        table = spec.field_table(data_s) if data_s else ""
        parts.append(f"## Response (성공)\n\n{status}\n\n" + _code("json", json.dumps(j, ensure_ascii=False, indent=2))
                     + (f"\n\n<b>success.data</b>\n\n{table}" if table else ""))
    else:
        m, p, body, _ = call
        if body == "PHOTO":
            r = client.request(m, p, headers={"Authorization": "Bearer demo"},
                               files={"photo": ("console.jpg", b"\xff\xd8", "image/jpeg")})
        else:
            r = client.request(m, p, headers={"Authorization": "Bearer demo"}, json=body)
        j = r.json()
        j["meta"].update(FIXED_META)
        data_s = spec.data_schema(op)
        table = spec.field_table(data_s) if data_s else ""
        parts.append(f"## Response (성공)\n\n{status}\n\n" + _code("json", json.dumps(j, ensure_ascii=False, indent=2))
                     + (f"\n\n<b>success.data</b>\n\n{table}" if table else ""))

    # Response(실패)
    rows = ["| 상태 | 오류 코드 |", "|---|---|"]
    first_code = None
    for s, r in sorted(op.get("responses", {}).items()):
        if s.startswith("4") or s.startswith("5"):
            if s == "422":
                continue
            rows.append(f"| {s} | {r.get('description', '')} |")
            first_code = first_code or r.get("description", "").split(",")[0].strip()
    rows.append("| 400 | VALIDATION_ERROR (값 형식·범위, 틀린 칸은 details.fields) |" if rb or params else "")
    rows = [r for r in rows if r]
    fail = ""
    if first_code and first_code in ErrorCode.__members__:
        st, msg = ERRORS[ErrorCode[first_code]]
        fail = "\n\n" + _code("json", json.dumps({
            "resultType": "FAIL", "success": None,
            "error": {"code": first_code, "message": msg, "details": None},
            "meta": {**FIXED_META, "path": path},
        }, ensure_ascii=False, indent=2))
    parts.append("## Response (실패)\n\n" + "\n".join(rows) + fail
                 + "\n\n공통 규칙·전체 오류 코드: 저장소 `docs/api/README.md`")

    parts.append("<p />\n\n<span color=\"gray\">자동 생성(`backend/scripts/notion_pages.py`). 손으로 고치지 말고 서버 코드를 고친 뒤 다시 만든다.</span>")
    return "\n\n".join(parts)


def build_common() -> str:
    """docs/api/README.md → 노션 본문. 저장소 상대 링크는 GitHub 주소로, 첫 제목은 노션 제목과 겹쳐서 뺀다."""
    readme = REPO / "docs" / "api" / "README.md"
    text = readme.read_text(encoding="utf-8")
    text = re.sub(r"^# .*\n+", "", text, count=1)

    def fix(m: re.Match) -> str:
        label, target = m.group(1), m.group(2)
        if re.match(r"^[a-z]+://", target):
            return m.group(0)
        rel = (readme.parent / target).resolve().relative_to(REPO).as_posix()
        return f"[{label}]({GITHUB_BLOB}{rel})"

    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", fix, text)
    return text.rstrip() + ("\n\n<p />\n\n<span color=\"gray\">자동 생성(`docs/api/README.md` → `backend/scripts/notion_pages.py`). "
                            "손으로 고치지 말고 저장소 문서를 고친 뒤 다시 만든다.</span>")


def main() -> None:
    spec = Spec(app.openapi())
    client = TestClient(app)
    calls = {}
    for c in CALLS:
        tpl = c[1].split("?")[0]
        for p in spec.s["paths"]:
            if p == tpl or re.fullmatch(re.sub(r"\{[^/]+\}", "[^/]+", p), tpl) and tpl not in spec.s["paths"]:
                calls[(c[0], p)] = c
                break

    real = collect_real()  # 진짜 API는 실제 흐름(테스트 DB)으로
    OUT.mkdir(parents=True, exist_ok=True)
    index = []
    for (method, path), page_id in NOTION_PAGE_IDS.items():
        md = build_page(spec, method, path, calls.get((method, path)), client, real)
        name = _slug(method, path) + ".md"
        (OUT / name).write_text(md, encoding="utf-8")
        index.append({"method": method, "path": path, "pageId": page_id, "file": name})

    (OUT / "_common.md").write_text(build_common(), encoding="utf-8")
    index.insert(0, {"method": "-", "path": "공통 규칙", "pageId": NOTION_COMMON_PAGE_ID, "file": "_common.md"})

    missing = {(m.upper(), p) for p, item in spec.s["paths"].items() for m in item} - set(NOTION_PAGE_IDS)
    (OUT / "index.json").write_text(json.dumps(index, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{len(index)}개 → {OUT}" + (f" / 노션 줄 없는 API: {sorted(missing)}" if missing else ""))


if __name__ == "__main__":
    main()
