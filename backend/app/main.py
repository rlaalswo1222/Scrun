"""SCRUN API 서버. 지금은 스텁(계획 0014 2단계): 모든 API가 stub_data의 고정 예시를 돌려준다.

실행: backend 폴더에서 `uvicorn app.main:app --reload` → http://localhost:8000/docs
"""
import asyncio
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from sqlalchemy.exc import TimeoutError as PoolTimeoutError
from starlette.exceptions import HTTPException as StarletteHTTPException

from .core.admission import ADMISSION_KWARGS, AdmissionMiddleware
from .core.config import settings
from .core.envelope import (
    api_error_handler,
    http_error_handler,
    pool_timeout_handler,
    unhandled_error_handler,
    validation_error_handler,
)
from .core.error_report import init_error_report
from .core.errors import ApiError
from .core.kakao import close_client as close_kakao_client
from .core.ocr import start_ocr, stop_ocr
from .core.request_log import RequestContextMiddleware, install_request_log
from .realtime import internal as realtime_internal
from .realtime import router as realtime
from .routers import auth, courses, events, friends, health, home, progress, runs, users, verification

DESCRIPTION = """
러닝머신 실내 러닝 앱 SCRUN의 서버 API. **지금은 스텁**이라 고정 예시만 돌려준다.

- 공통 규칙(응답 껍데기·상태 코드·오류 코드·단위): 저장소 `docs/api/README.md`
- API 목록·진행: 노션 SCRUN > 문서 > API 명세
- 로그인: 오른쪽 위 **Authorize**에 아무 토큰(예: `demo`)을 넣으면 예시 사용자로 호출된다.
  `expired` → TOKEN_EXPIRED, `signup` → 가입용 토큰. 각 API 설명의 "스텁:"에 오류 화면을 부르는 값이 있다
"""

@asynccontextmanager
async def lifespan(_: FastAPI):
    start_ocr()   # OCR_ENGINE=onnx면 읽기 프로세스를 미리 띄움(계획 0046)
    yield
    await asyncio.to_thread(stop_ocr)   # 프로세스 join이 이벤트 루프(실시간 WebSocket 정리)를 막지 않게
    close_kakao_client()


init_error_report(settings.error_report)   # Sentry(계획 0087): SENTRY_DSN이 있을 때만. 앱보다 먼저(시작 중 오류도 받게)

app = FastAPI(title="SCRUN API", version="0.1.0-stub", description=DESCRIPTION, lifespan=lifespan)

# 미들웨어는 나중에 붙인 것이 바깥: 요청 ID·요청 로그 → 번호표(0071) → 라우터. 요청 로그가 번호표 기다림·503도 남김.
# 둘 다 순수 ASGI(BaseHTTPMiddleware·@app.middleware("http")는 요청마다 작업·통로를 더 만들어 CPU를 씀, 계획 0073 측정)
if settings.admission.limit:
    app.add_middleware(AdmissionMiddleware, **ADMISSION_KWARGS)
if settings.request_log:   # 요청마다 처리 시간 한 줄(계획 0070)
    install_request_log()
app.add_middleware(RequestContextMiddleware, log_requests=settings.request_log)
app.add_exception_handler(ApiError, api_error_handler)
app.add_exception_handler(PoolTimeoutError, pool_timeout_handler)  # 연결 풀 기다림 초과 → 503(계획 0071)
app.add_exception_handler(RequestValidationError, validation_error_handler)
app.add_exception_handler(StarletteHTTPException, http_error_handler)
app.add_exception_handler(Exception, unhandled_error_handler)

for r in (auth, users, home, courses, runs, verification, progress, friends, events, health):
    app.include_router(r.router)
if settings.realtime_url is None:   # 허브를 가진 프로세스만(계획 0073). 일반 API 프로세스는 REALTIME_URL로 그 프로세스에 묻는다
    app.include_router(realtime.router)  # WebSocket /v1/realtime(계획 0029)
    app.include_router(realtime_internal.router)  # 허브 내부 API(일반 API 프로세스가 부름, 밖에선 못 부름)
