"""공통 응답 껍데기(docs/api/README.md "응답 껍데기"): resultType · success.data · error · meta."""
import uuid
from datetime import datetime, timezone
from typing import Any, Generic, Literal, Optional, TypeVar

from fastapi import Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

from .errors import ERRORS, ApiError, ErrorCode
from .model import CamelModel

T = TypeVar("T")


class Meta(CamelModel):
    timestamp: datetime
    path: str
    request_id: str


class ErrorBody(CamelModel):
    code: ErrorCode
    message: str
    details: Optional[Any] = None


class SuccessBody(CamelModel, Generic[T]):
    data: T


class Envelope(CamelModel, Generic[T]):
    result_type: Literal["SUCCESS"]
    success: SuccessBody[T]
    error: None = None
    meta: Meta


class ErrorEnvelope(CamelModel):
    result_type: Literal["FAIL"]
    success: None = None
    error: ErrorBody
    meta: Meta


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def _meta(request: Request) -> dict:
    return {
        "timestamp": utc_now(),
        "path": request.url.path,
        "requestId": getattr(request.state, "request_id", "req_unknown"),
    }


def ok(request: Request, data: Any) -> dict:
    """라우터는 return ok(request, data). response_model=Envelope[모델]이 검증·camelCase 변환."""
    return {"resultType": "SUCCESS", "success": {"data": data}, "error": None, "meta": _meta(request)}


def _fail(request: Request, status: int, code: ErrorCode, message: str, details: Any = None) -> JSONResponse:
    body = ErrorEnvelope(
        result_type="FAIL",
        error=ErrorBody(code=code, message=message, details=details),
        meta=Meta(**_meta(request)),
    )
    return JSONResponse(status_code=status, content=body.model_dump(mode="json", by_alias=True))


async def request_id_middleware(request: Request, call_next):
    request.state.request_id = "req_" + uuid.uuid4().hex[:12]
    response = await call_next(request)
    response.headers["X-Request-Id"] = request.state.request_id
    return response


async def api_error_handler(request: Request, exc: ApiError) -> JSONResponse:
    return _fail(request, exc.status, exc.code, exc.message, exc.details)


async def validation_error_handler(request: Request, exc: RequestValidationError) -> JSONResponse:
    # FastAPI 기본 422 대신 400 VALIDATION_ERROR. 틀린 칸은 details.fields
    fields = [
        {"field": ".".join(str(p) for p in e.get("loc", [])[1:]), "reason": e.get("msg", "")}
        for e in exc.errors()
    ]
    status, message = ERRORS[ErrorCode.VALIDATION_ERROR]
    return _fail(request, status, ErrorCode.VALIDATION_ERROR, message, {"fields": fields})


async def http_error_handler(request: Request, exc: StarletteHTTPException) -> JSONResponse:
    code = {404: ErrorCode.NOT_FOUND, 405: ErrorCode.METHOD_NOT_ALLOWED, 401: ErrorCode.UNAUTHORIZED}.get(
        exc.status_code, ErrorCode.INTERNAL_ERROR
    )
    status, message = ERRORS[code]
    return _fail(request, exc.status_code if code != ErrorCode.INTERNAL_ERROR else status, code, message)


async def unhandled_error_handler(request: Request, exc: Exception) -> JSONResponse:
    status, message = ERRORS[ErrorCode.INTERNAL_ERROR]
    return _fail(request, status, ErrorCode.INTERNAL_ERROR, message)


def error_responses(*codes: ErrorCode) -> dict:
    """라우트 문서(Swagger)에 오류 응답을 적는다. 같은 상태 코드는 한 칸에 코드 이름을 모아 씀."""
    by_status: dict[int, list[str]] = {}
    for c in codes:
        by_status.setdefault(ERRORS[c][0], []).append(c.value)
    return {s: {"model": ErrorEnvelope, "description": ", ".join(names)} for s, names in by_status.items()}
