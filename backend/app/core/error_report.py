"""서버 오류를 Sentry로 보낸다(계획 0087 1단계, ADR-0015).

- SENTRY_DSN이 있을 때만 켠다(app/core/config.py ErrorReportSettings). 없으면 init을 부르지 않아 아무것도 안 보냄
  → 로컬·테스트·부하 측정은 그대로
- 보내는 것
  - 처리 못 한 예외(500): envelope.unhandled_error_handler가 report_exception을 부름. 요청 ID·라우트 틀·메서드를 태그로
    → Sentry에서 본 오류를 서버 로그 줄(`journalctl … | grep req_…`)과 이어 봄
  - ERROR 이상 로그(예외가 붙어 있으면 그 스택까지): OCR 프로세스가 죽음, 카카오 어드민 키 없음, WebSocket·백그라운드에서 터진
    예외(uvicorn이 ERROR로 남김) 등. 500 처리기에서 이미 보낸 예외를 uvicorn이 다시 로그로 남기는 것은 Sentry가 같은 예외로 보고 버림
- 보내지 않는 것: 4xx·의도한 503(바쁨·OCR 시간 초과는 ApiError라 500 처리기를 안 지남), 요청 본문·머리(토큰·쿠키),
  사용자 정보(data_collection), 스택의 지역 변수(토큰·사진 바이트가 들어 있을 수 있음), INFO 로그 빵부스러기
  (요청 로그 줄이 다른 요청의 오류에 섞임)
- 자동 연동(FastAPI·Starlette·SQLAlchemy·httpx 감싸기)은 끔: 요청마다 감싸는 비용 없이(0073에서 미들웨어 감싸기가 이벤트 루프 CPU의
  큰 몫이었음) 500 처리기·로그에서만 보냄. 성능 추적(트랜잭션)도 안 씀(무료 한도는 오류에만)
"""
import logging
from typing import Optional

import sentry_sdk
from sentry_sdk.integrations.logging import LoggingIntegration
from starlette.requests import Request

from .config import ErrorReportSettings
from .request_log import request_id, route_path

# 지울 칸: 자동 연동을 꺼서 보통은 안 붙지만, 다른 길로 붙어도 나가지 않게(토큰·쿠키·IP)
_DROP_KEYS = ("request", "user")
_REPORTED = "_scrun_reported"   # report_exception이 보낸 예외 표시


def _scrub(event: dict, hint: Optional[dict]) -> Optional[dict]:
    # 500 처리기가 보낸 예외를 uvicorn이 "Exception in ASGI application" ERROR 로그로 또 남기면 버림(같은 오류 두 번).
    # Sentry의 같은 예외 거르기(Dedupe)는 같은 컨텍스트일 때만 확실해서 표시로 한 번 더 막음
    exc_info = (hint or {}).get("exc_info")
    if event.get("logger") and exc_info and getattr(exc_info[1], _REPORTED, False):
        return None
    for key in _DROP_KEYS:
        event.pop(key, None)
    return event


def init_error_report(cfg: ErrorReportSettings, transport=None) -> bool:
    """켜졌으면 True. transport는 테스트가 보낸 내용을 받아 보려고 바꿔 끼움."""
    if cfg.dsn is None:
        return False
    sentry_sdk.init(
        dsn=cfg.dsn,
        environment=cfg.environment,
        release=cfg.release,
        server_name=cfg.service,
        # 사용자 정보·쿠키·요청 머리·본문·주소 쿼리·DB 쿼리 값·스택 지역 변수를 모두 안 모음(send_default_pii는 2.77에서 폐기 예정).
        # include_local_variables도 끔: 2.77은 스택 프레임을 만들 때 아직 이 옛 옵션을 읽음(sentry_sdk/client.py)
        data_collection={"user_info": False, "cookies": {"mode": "off"}, "http_headers": {"request": {"mode": "off"}},
                         "http_bodies": [], "url_query_params": {"mode": "off"}, "database_query_data": False,
                         "stack_frame_variables": False},
        include_local_variables=False,
        auto_enabling_integrations=False,
        integrations=[LoggingIntegration(level=None, event_level=logging.ERROR)],  # 빵부스러기 없음, ERROR 이상만 오류로
        before_send=_scrub,
        transport=transport,
    )
    sentry_sdk.get_global_scope().set_tag("service", cfg.service)
    return True


def report_exception(request: Request, exc: BaseException) -> None:
    """처리 못 한 예외 하나를 요청 ID와 함께 보낸다. 꺼져 있으면 아무것도 안 함."""
    if not sentry_sdk.get_client().is_active():
        return
    try:
        setattr(exc, _REPORTED, True)
    except AttributeError:   # __slots__ 예외 등은 표시를 못 붙임 → 로그 쪽은 Sentry의 같은 예외 거르기에 맡김
        pass
    with sentry_sdk.new_scope() as scope:
        scope.set_tag("request_id", request_id(request.scope) or "-")
        scope.set_tag("route", route_path(request.scope))
        scope.set_tag("method", request.method)
        sentry_sdk.capture_exception(exc)
