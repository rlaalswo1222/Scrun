from fastapi import APIRouter, Depends, File, Request, UploadFile

from .. import stub_data as d
from ..core.auth import Principal, stub_user
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ErrorCode
from ..schemas.verification import ConsoleReading, SkipRequest, VerificationResult
from .runs import KNOWN, RUN_ERRORS, STUB_NOTE, _check_run

router = APIRouter(prefix="/v1", tags=["Verification"])
E = ErrorCode
AUTH = error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)
ERRS = {**AUTH, **RUN_ERRORS, **error_responses(E.VALIDATION_ERROR, E.VERIFICATION_CLOSED)}
OUTCOME = (
    "\n\n재촬영 필요(OCR 실패)·미인증(오차 초과, OCR 불가 계기판)은 오류가 아니라 200 + status. "
    "인증 완료·미인증이면 rewards에 반영 결과(E-11)."
)


@router.post(
    "/runs/{runId}/verification/photo",
    summary="계기판 사진으로 인증",
    description="화면 E-04에서 E-09 · BL-16, BL-17, BL-18. **OCR 위치 미정**(서버 vs 앱): 서버에서 하면 이 API, 사진은 OCR 직후 폐기."
    + OUTCOME + "\n\n스텁: 파일 이름에 `blur` → RETAKE_REQUIRED, 그 밖 → VERIFIED" + STUB_NOTE,
    response_model=Envelope[VerificationResult],
    responses=ERRS,
)
def verify_photo(runId: str, request: Request, photo: UploadFile = File(description="앱 카메라로 찍은 계기판 사진(JPEG)"),
                 _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, d.RETAKE_REQUIRED if "blur" in (photo.filename or "") else d.VERIFIED)


@router.post(
    "/runs/{runId}/verification/reading",
    summary="앱이 읽은 계기판 값으로 인증",
    description="화면 E-05, E-06 · BL-17, BL-18. **OCR 위치 미정**: 앱에서 OCR하면 이 API. 정해지면 photo/reading 중 하나만 남김."
    + OUTCOME + STUB_NOTE,
    response_model=Envelope[VerificationResult],
    responses=ERRS,
)
def verify_reading(runId: str, body: ConsoleReading, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, d.VERIFIED)


@router.post(
    "/runs/{runId}/verification/skip",
    summary="인증 없이 마무리",
    description="화면 D-22, E-10 · BL-16, BL-19. 촬영 없이 마무리·시간 초과·재촬영 포기 → 미인증 저장. 나중에 인증 불가" + STUB_NOTE,
    response_model=Envelope[VerificationResult],
    responses=ERRS,
)
def verify_skip(runId: str, body: SkipRequest, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, {**d.UNVERIFIED_SKIPPED, "reason": body.reason.value})
