"""계기판 인증(BL-16 ⑤⑥, BL-17, BL-18, 계획 0038). 끝난 러닝(PENDING)을 VERIFIED·UNVERIFIED로 정한다.

- 사진: 브래킷 여러 장 → OCR(core/ocr) → 센서 기록과 대조(core/verification). 사진은 메모리에서만 읽고(디스크에 안 씀)
  요청이 끝나면 버려짐, 해시만 남김
- 못 읽으면 RETAKE_REQUIRED(저장되는 상태 아님). 다시 찍기를 다 쓰면 UNVERIFIED(UNSUPPORTED_CONSOLE)
- 건너뛰기: 촬영 없이 마무리·촬영 시간 초과·다시 찍기 포기 → UNVERIFIED
- 마감(서버가 종료를 받은 뒤 설정 시간, core/run_access)이 지나면 기록은 UNVERIFIED(TIMEOUT)로 닫히고 사진은 계속 VERIFICATION_CLOSED
- 한 번 정해지면 바뀌지 않는다. 응답을 못 받은 앱이 다시 보내면 처음 결과를 돌려준다
- 동시에 온 요청: OCR은 줄을 잠그지 않고 하고(DB 연결도 놓음), 저장 직전에 줄을 잠가 상태·횟수·같은 사진을 다시 본다
"""
import hashlib
import threading
from concurrent.futures import ThreadPoolExecutor
from concurrent.futures import TimeoutError as FutureTimeout
from datetime import datetime, timezone
from typing import Optional

from fastapi import APIRouter, Depends, Request
from sqlalchemy import func, select
from sqlalchemy.orm import Session
from starlette.formparsers import MultiPartException, MultiPartParser

from ..core.config import settings
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..core.ocr import UNREADABLE, ConsoleRead, OcrEngine, Photo, get_ocr
from ..core.run_access import ACTIVE, DISCARDED, PENDING, UNVERIFIED, invalid, my_run, verification_deadline
from ..core.security import current_user
from ..core.verification import Outcome, decide
from ..db.ids import new_id
from ..db.models import Run, User, Verification
from ..db.session import get_db
from ..schemas.verification import SkipRequest, UnverifiedReason, VerificationResult, VerificationStatus

router = APIRouter(prefix="/v1", tags=["Verification"])
E = ErrorCode
POLICY = settings.verification
S, R = VerificationStatus, UnverifiedReason
ERRS = {**error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED, E.RUN_NOT_FOUND, E.FORBIDDEN),
        **error_responses(E.VALIDATION_ERROR, E.RUN_NOT_FINISHED, E.VERIFICATION_CLOSED, E.SERVICE_UNAVAILABLE)}
OUTCOME = ("\n\n못 읽음(RETAKE_REQUIRED)·미인증(UNVERIFIED)은 오류가 아니라 200 + status. "
           "이미 정해진 기록에 다시 보내면 처음 결과를 그대로 돌려준다(응답을 못 받은 앱의 재시도).")
PHOTO, SKIP, EXPIRED = "PHOTO", "SKIP", "EXPIRED"  # verifications.method
JPEG_MAGIC = b"\xff\xd8\xff"
MB = 1024 * 1024


def _now() -> datetime:
    return datetime.now(timezone.utc)


# ---------- 사진 받기 ----------
BODY_LIMIT = POLICY.max_photos * POLICY.max_photo_bytes + 64 * 1024  # 사진 + multipart 머리


class _InMemoryParser(MultiPartParser):
    """요청 크기 상한까지는 임시 파일(디스크)로 넘기지 않는다(Starlette 기본은 1 MB부터 디스크). 이 API에서만 씀."""

    spool_max_size = BODY_LIMIT


async def read_photos(request: Request) -> list[Photo]:
    """multipart `photos`(JPEG 1–3장) → 바이트. 크기는 본문을 읽기 전에 Content-Length로 거른다."""
    length = request.headers.get("content-length", "")
    if not length.isascii() or not length.isdigit():
        raise invalid("photos", "Content-Length가 있어야 합니다")
    if int(length) > BODY_LIMIT:
        raise invalid("photos", f"사진이 너무 큽니다(한 장 {POLICY.max_photo_bytes // MB} MB, {POLICY.max_photos}장까지)")
    if not request.headers.get("content-type", "").startswith("multipart/form-data"):
        raise invalid("photos", "multipart/form-data로 보내야 합니다")
    parser = _InMemoryParser(request.headers, request.stream(), max_files=POLICY.max_photos, max_fields=0)
    try:
        form = await parser.parse()
    except MultiPartException as e:
        raise invalid("photos", f"사진은 {POLICY.max_photos}장까지, 다른 칸 없이 보냅니다({e.message})") from None
    try:
        if set(form.keys()) - {"photos"}:
            raise invalid("photos", "photos 말고 다른 칸이 있습니다")
        files = form.getlist("photos")
        if not files:
            raise invalid("photos", "사진이 없습니다")
        photos = []
        for f in files:
            data = await f.read()
            if len(data) > POLICY.max_photo_bytes:
                raise invalid("photos", f"한 장이 {POLICY.max_photo_bytes // MB} MB를 넘습니다(가이드 영역만 잘라 줄여 보내기)")
            if not data.startswith(JPEG_MAGIC):
                raise invalid("photos", "JPEG 사진이 아닙니다")
            photos.append(Photo(data, f.filename or ""))
        return photos
    finally:
        await form.close()


# ---------- OCR ----------
# OCR은 CPU를 쓰므로 요청 스레드와 따로, 시간 제한(BL-17 ⑥). 시간을 넘긴 작업은 멈출 수 없어 자리를 계속 차지하므로
# 자리가 없으면 줄 세우지 않고 바로 503(사용자 잘못이 아니라 다시 찍기 횟수에 안 셈)
OCR_WORKERS = 2
_ocr_pool = ThreadPoolExecutor(max_workers=OCR_WORKERS, thread_name_prefix="ocr")
_ocr_free = threading.BoundedSemaphore(OCR_WORKERS)


def _ocr(engine: OcrEngine, photos: list[Photo]) -> ConsoleRead:
    slots = _ocr_free
    if not slots.acquire(blocking=False):
        raise ApiError(E.SERVICE_UNAVAILABLE, "계기판 읽기가 밀려 있습니다. 잠시 뒤 다시 보내세요.")
    try:
        future = _ocr_pool.submit(engine.read, photos)
    except BaseException:
        slots.release()
        raise
    future.add_done_callback(lambda _: slots.release())
    try:
        return future.result(timeout=POLICY.ocr_timeout_s)
    except FutureTimeout:
        raise ApiError(E.SERVICE_UNAVAILABLE, "계기판 읽기가 시간 안에 끝나지 않았습니다.") from None


# ---------- 결과 ----------
def _result(run: Run, status: str, reason: Optional[str] = None, rows: Optional[list] = None,
            retakes_left: Optional[int] = None) -> dict:
    console = None
    if run.console_distance_m is not None:
        console = {"distanceM": run.console_distance_m, "durationS": run.console_duration_s}
    return {"status": status, "reason": reason, "console": console,
            "sensor": {"distanceM": run.sensor_distance_m, "durationS": run.sensor_duration_s},
            "comparison": rows or [],
            "retakeDeadline": verification_deadline(run) if status == S.RETAKE_REQUIRED else None,
            "retakesLeft": retakes_left, "rewards": None}


def _stored(db: Session, run: Run) -> dict:
    """이미 정해진 기록의 결과(정한 시도의 대조 표)."""
    last = db.scalar(select(Verification).where(Verification.run_id == run.id, Verification.status != S.RETAKE_REQUIRED)
                     .order_by(Verification.created_at.desc()).limit(1))
    return _result(run, run.status, run.unverified_reason, last.comparison if last else [])


def _save(db: Session, run: Run, outcome: Outcome, method: str, hashes: tuple[str, ...] = ()) -> dict:
    """한 번의 시도를 기록. VERIFIED·UNVERIFIED면 기록 상태도 정한다(공식 값: 인증 완료 = 계기판, 미인증 = finish 때의 센서)."""
    read, rows = outcome.read, [r.model_dump(by_alias=True) for r in outcome.rows]
    reason = outcome.reason.value if outcome.reason else None
    if outcome.status != S.RETAKE_REQUIRED:
        run.status, run.unverified_reason = outcome.status.value, reason
        if read is not None:
            run.console_distance_m, run.console_duration_s = read.distance_m, read.duration_s
        if outcome.status == S.VERIFIED:
            run.official_source = "CONSOLE"
            run.official_distance_m, run.official_duration_s = read.distance_m, read.duration_s
    db.add(Verification(id=new_id(), run_id=run.id, method=method, status=outcome.status.value, reason=reason,
                        read_distance_m=read.distance_m if read else None, read_duration_s=read.duration_s if read else None,
                        comparison=rows, photo_hashes=list(hashes)))
    db.commit()
    return _result(run, outcome.status.value, reason, rows)


def _finished(run: Run) -> Run:
    if run.status == ACTIVE:
        raise ApiError(E.RUN_NOT_FINISHED)
    if run.status == DISCARDED:
        raise ApiError(E.VERIFICATION_CLOSED)
    return run


def _closed_by_deadline(run: Run) -> bool:
    return run.status == UNVERIFIED and run.unverified_reason == R.TIMEOUT.value


def _close_if_expired(db: Session, run: Run) -> bool:
    """잠근 줄에서: 마감이 지난 PENDING을 UNVERIFIED(TIMEOUT)로 닫음."""
    if run.status == PENDING and _now() > verification_deadline(run):
        _save(db, run, Outcome(S.UNVERIFIED, R.TIMEOUT), EXPIRED)
        return True
    return False


def _retakes_used(db: Session, run_id: str, before: Optional[datetime] = None) -> int:
    """이 기록에서 못 읽어 다시 찍게 한 횟수(before가 있으면 그 시도 전까지)."""
    q = select(func.count()).select_from(Verification).where(Verification.run_id == run_id,
                                                              Verification.status == S.RETAKE_REQUIRED)
    if before is not None:
        q = q.where(Verification.created_at < before)
    return db.scalar(q) or 0


def _seen(db: Session, run: Run, hashes: tuple[str, ...]) -> tuple[Optional[Verification], bool]:
    """(이 기록에 똑같은 사진 묶음으로 다시 찍기를 받은 시도, 다른 기록에 쓴 사진이 섞였는지)."""
    used = db.scalars(select(Verification).where(Verification.photo_hashes.overlap(list(hashes)))).all()
    same = next((v for v in used if v.run_id == run.id and v.status == S.RETAKE_REQUIRED
                 and tuple(sorted(v.photo_hashes)) == hashes), None)
    return same, any(v.run_id != run.id for v in used)


def _retake_again(db: Session, run: Run, attempt: Verification) -> dict:
    """같은 사진 묶음을 다시 보냄(응답을 못 받은 재시도) → 그때와 같은 답, 횟수 안 셈."""
    left = POLICY.max_retakes - _retakes_used(db, run.id, before=attempt.created_at)
    return _result(run, S.RETAKE_REQUIRED.value, retakes_left=left)


@router.post(
    "/runs/{runId}/verification/photo",
    summary="계기판 사진으로 인증",
    description=(
        "화면 E-04에서 E-09 · BL-16 ⑥, BL-17, BL-18. 앱 카메라로 찍은 계기판 사진(브래킷 1–"
        f"{POLICY.max_photos}장, JPEG, 한 장 {POLICY.max_photo_bytes // MB} MB 이하, 가이드 영역만 잘라서)을 multipart "
        "`photos`로 여러 번 붙여 보낸다. 서버가 읽고 센서 기록과 대조: "
        f"거리 {POLICY.distance_tolerance:.0%} 이내·시간(계기판 − 센서) {POLICY.duration_min_s}초에서 "
        f"+{POLICY.duration_max_s}초(일시정지가 있었으면 + 쉰 시간)면 VERIFIED, 넘으면 UNVERIFIED(MISMATCH), "
        f"못 읽으면 RETAKE_REQUIRED(다시 찍기 {POLICY.max_retakes}번, 남은 횟수 retakesLeft), 다 써도 못 읽거나 "
        "읽을 수 없는 계기판이면 UNVERIFIED(UNSUPPORTED_CONSOLE). 값은 서버 설정. 사진은 메모리에서 읽고 버림(해시만 남김). "
        "다른 기록에 쓴 사진이 섞이면 못 읽은 것으로 봄. "
        f"마감(종료를 받은 뒤 {POLICY.deadline_s // 60}분)이 지나면 VERIFICATION_CLOSED(기록은 UNVERIFIED·TIMEOUT으로 닫힘). "
        "OCR 엔진이 없거나 밀렸거나 시간 안에 못 읽으면 503(다시 찍기 횟수에 안 셈)." + OUTCOME),
    response_model=Envelope[VerificationResult],
    responses=ERRS,
    openapi_extra={"requestBody": {"required": True, "content": {"multipart/form-data": {"schema": {
        "type": "object", "required": ["photos"],
        "properties": {"photos": {"type": "array", "items": {"type": "string", "format": "binary"},
                                  "description": f"계기판 사진(JPEG) 1–{POLICY.max_photos}장. 브래킷이면 같은 이름으로 여러 장"}}}}}}},
)
def verify_photo(runId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db),
                 photos: list[Photo] = Depends(read_photos), engine: Optional[OcrEngine] = Depends(get_ocr)):
    run = _finished(my_run(db, runId, user))
    if run.status == PENDING and _now() > verification_deadline(run):
        run = _finished(my_run(db, runId, user, lock=True))
        _close_if_expired(db, run)
    if _closed_by_deadline(run):
        raise ApiError(E.VERIFICATION_CLOSED)
    if run.status != PENDING:
        return ok(request, _stored(db, run))

    hashes = tuple(sorted({hashlib.sha256(p.data).hexdigest() for p in photos}))
    same, reused = _seen(db, run, hashes)
    if same is not None:
        return ok(request, _retake_again(db, run, same))
    read = UNREADABLE
    if not reused:
        if engine is None:
            raise ApiError(E.SERVICE_UNAVAILABLE, "계기판 읽기를 아직 쓸 수 없습니다.")
        db.rollback()  # OCR 동안 DB 연결·트랜잭션을 잡지 않게
        read = _ocr(engine, photos)

    run = _finished(my_run(db, runId, user, lock=True))  # OCR 동안 다른 요청이 정했거나 마감이 지났을 수 있음
    if _close_if_expired(db, run) or _closed_by_deadline(run):
        raise ApiError(E.VERIFICATION_CLOSED)
    if run.status != PENDING:
        return ok(request, _stored(db, run))
    same, reused = _seen(db, run, hashes)  # 같은 사진이 동시에 온 경우
    if same is not None:
        return ok(request, _retake_again(db, run, same))

    used = _retakes_used(db, run.id)
    paused_s = (run.ended_at - run.started_at).total_seconds() - run.sensor_duration_s
    outcome = decide(POLICY, UNREADABLE if reused else read, run.sensor_distance_m, run.sensor_duration_s, paused_s, used)
    out = _save(db, run, outcome, PHOTO, hashes)
    if outcome.status == S.RETAKE_REQUIRED:
        out["retakesLeft"] = POLICY.max_retakes - used
    return ok(request, out)


@router.post(
    "/runs/{runId}/verification/skip",
    summary="인증 없이 마무리",
    description="화면 D-22, E-09, E-10 · BL-16 ⑤, BL-19. 촬영 없이 마무리(SKIPPED)·촬영 시간 초과(TIMEOUT)·다시 찍기 포기"
    "(RETAKE_GIVEN_UP) → UNVERIFIED로 저장, 공식 값은 센서 값. 나중에 인증할 수 없다. 마감이 지났으면 이유는 TIMEOUT." + OUTCOME,
    response_model=Envelope[VerificationResult],
    responses=ERRS,
)
def verify_skip(runId: str, body: SkipRequest, request: Request, user: User = Depends(current_user),
                db: Session = Depends(get_db)):
    run = _finished(my_run(db, runId, user, lock=True))
    if run.status == PENDING and not _close_if_expired(db, run):
        return ok(request, _save(db, run, Outcome(S.UNVERIFIED, R(body.reason.value)), SKIP))
    return ok(request, _stored(db, run))
