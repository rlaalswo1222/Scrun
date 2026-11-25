"""SCRUN API 서버. 지금은 스텁(계획 0014 2단계): 모든 API가 stub_data의 고정 예시를 돌려준다.

실행: backend 폴더에서 `uvicorn app.main:app --reload` → http://localhost:8000/docs
"""
import asyncio
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from starlette.exceptions import HTTPException as StarletteHTTPException

from .core.admission import admission
from .core.config import settings
from .core.envelope import (
    api_error_handler,
    http_error_handler,
    request_id_middleware,
    unhandled_error_handler,
    validation_error_handler,
)
from .core.errors import ApiError
from .core.kakao import close_client as close_kakao_client
from .core.ocr import start_ocr, stop_ocr
from .core.request_log import install_request_log, request_log_middleware
from .realtime import router as realtime
from .routers import auth, courses, events, friends, home, progress, runs, users, verification

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


app = FastAPI(title="SCRUN API", version="0.1.0-stub", description=DESCRIPTION, lifespan=lifespan)

# 미들웨어는 나중에 붙인 것이 바깥: 요청 ID → 요청 로그 → 번호표(0071) → 라우터. 요청 로그가 번호표 기다림·503도 남김
if settings.admission.limit:
    app.middleware("http")(admission)
if settings.request_log:   # 요청마다 처리 시간 한 줄(계획 0070). 요청 ID 미들웨어 안쪽이라 rid를 같이 남김
    install_request_log()
    app.middleware("http")(request_log_middleware)
app.middleware("http")(request_id_middleware)
app.add_exception_handler(ApiError, api_error_handler)
app.add_exception_handler(RequestValidationError, validation_error_handler)
app.add_exception_handler(StarletteHTTPException, http_error_handler)
app.add_exception_handler(Exception, unhandled_error_handler)

for r in (auth, users, home, courses, runs, verification, progress, friends, events):
    app.include_router(r.router)
app.include_router(realtime.router)  # WebSocket /v1/realtime(계획 0029)
