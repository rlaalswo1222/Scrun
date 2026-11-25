"""허브 내부 API(계획 0073): 허브를 가진 프로세스(실시간)가 일반 API 프로세스에게 허브 함수를 열어 준다(app/realtime/link.py).

- 밖에서 못 부르게: 비밀 머리(X-Scrun-Internal, JWT_SECRET에서 만든 값)가 맞아야 하고, Caddy도 /internal/*을 막는다.
  틀리면 404(있는지도 알리지 않음). OpenAPI 문서에 안 나옴
- 허브 함수는 짧게 잠그고 끝나므로 이벤트 루프에서 바로 부른다(스레드로 넘기지 않음)
- 허브를 가진 프로세스(REALTIME_URL이 없음)에서만 붙는다(main.py)
"""
import hmac

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse, Response

from ..core.config import settings
from .hub import hub
from .link import INTERNAL_HEADER, INTERNAL_PREFIX, internal_token

router = APIRouter(prefix=INTERNAL_PREFIX.rstrip("/"), include_in_schema=False)
_TOKEN = internal_token(settings.jwt_secret)


def _companion(c) -> dict:
    return {"kind": c.kind, "user_id": c.user_id, "label": c.label}


OPS = {
    "present_user_ids": lambda args: {"ids": sorted(hub.present_user_ids())},
    "running_positions": lambda args: {"positions": hub.running_positions(args["course_id"], args.get("except_user"))},
    "in_map": lambda args: {"where": hub.in_map(args["user_id"])},
    "running_now": lambda args: {"running": {uid: {"course_id": r.course_id, "run_m": r.run_m, "kmh": r.kmh}
                                          for uid, r in hub.running_now(args["user_ids"]).items()}},
    "companions": lambda args: {"companions": [_companion(c) for c in hub.companions(args["user_id"], args["run_id"])]},
    "start_tracking": lambda args: hub.start_tracking(args["user_id"], args["run_id"]) or {},
    "stop_tracking": lambda args: hub.stop_tracking(args["user_id"], args["run_id"]) or {},
    "forget_user": lambda args: hub.forget_user(args["user_id"]) or {},
}


@router.post("/{op}")
async def call(op: str, request: Request) -> Response:
    """응답은 공통 껍데기 없이 값만(내부용). 틀린 비밀·없는 op는 빈 404, 모양이 틀린 인자는 빈 400."""
    if not hmac.compare_digest(request.headers.get(INTERNAL_HEADER, ""), _TOKEN) or op not in OPS:
        return Response(status_code=404)
    try:
        args = await request.json()
        return JSONResponse(OPS[op](args))
    except (ValueError, KeyError, TypeError):
        return Response(status_code=400)
