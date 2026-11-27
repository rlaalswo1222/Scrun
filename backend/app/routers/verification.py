"""계기판 인증(BL-16 ⑤⑥, BL-17, BL-18, 계획 0038). 끝난 러닝(PENDING)을 VERIFIED·UNVERIFIED로 정한다.

- 사진: 브래킷 여러 장 → OCR(core/ocr) → 센서 기록과 대조(core/verification). 사진은 메모리에서만 읽고(디스크에 안 씀)
  요청이 끝나면 버려짐, 해시만 남김
- 거리·시간을 읽으면 바로 정하지 않고 CONFIRM_REQUIRED(계획 0045): 사용자가 읽은 값만 보고 "맞아요"면 confirm이 그때 판정
  (오차 초과도 다시 찍기 없이 UNVERIFIED), "틀렸어요"면 다시 찍기(횟수 안 씀, 마감까지). 값을 고칠 수는 없음(BL-17 ④)
- 읽을 수 없는 계기판·못 읽음은 다시 찍기가 남았으면 RETAKE_REQUIRED + 이유(저장되는 상태 아님), 다 쓰면 UNVERIFIED
  (core/verification.decide)
- 건너뛰기: 촬영 없이 마무리·촬영 시간 초과·다시 찍기 포기 → UNVERIFIED. 포기는 마지막 시도의 이유(오차 초과 등)로
- 마감(서버가 종료를 받은 뒤 설정 시간, core/run_access)은 요청이 도착한 때로 판단. 지났으면 기록은 UNVERIFIED(TIMEOUT)로 닫히고
  사진은 계속 VERIFICATION_CLOSED(앱이 skip TIMEOUT으로 닫은 기록은 처음 결과)
- 한 번 정해지면 바뀌지 않는다. 응답을 못 받은 앱이 다시 보내면 처음 결과를 돌려준다
- 동시에 온 요청: OCR은 줄을 잠그지 않고 하고(DB 연결도 놓음), 저장 직전에 줄을 잠가 상태·횟수·같은 사진을 다시 본다
- 사진 본문은 내 기록·끝난 러닝·엔진 있음을 확인한 뒤에 읽는다
"""
import hashlib
import logging
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from concurrent.futures import TimeoutError as FutureTimeout
from datetime import datetime, timedelta
from typing import Optional

from fastapi import APIRouter, Depends, Request
from sqlalchemy import func, select
from sqlalchemy.orm import Session
from starlette.formparsers import MultiPartException, MultiPartParser

from ..core import metrics
from ..core.config import settings
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode, invalid
from ..core.ocr import UNREADABLE, BadPhoto, ConsoleRead, EngineUnavailable, OcrEngine, Photo, get_ocr
from ..core.run_access import (ACTIVE, DISCARDED, PENDING, TIMEOUT, UNVERIFIED, close_expired, is_expired, my_run, now_utc,
                               verification_deadline)
from ..core.security import current_user
from ..core.growth import rewards_for, settle
from ..core.verification import Outcome, confirmed, decide
from ..db.ids import new_id
from ..db.models import Run, User, Verification
from ..db.session import get_db
from ..schemas.verification import (ConfirmRequest, SkipRequest, UnverifiedReason, VerificationResult,
                                    VerificationStatus)

router = APIRouter(prefix="/v1", tags=["Verification"])
log = logging.getLogger("scrun.verification")
E = ErrorCode
POLICY = settings.verification
S, R = VerificationStatus, UnverifiedReason
# 문서는 error_responses 한 번에(dict를 합치면 같은 상태 코드 409가 덮어써짐)
ERR_CODES = (E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED, E.RUN_NOT_FOUND, E.FORBIDDEN,
             E.VALIDATION_ERROR, E.RUN_NOT_FINISHED, E.VERIFICATION_CLOSED, E.SERVICE_UNAVAILABLE)
ERRS = error_responses(*ERR_CODES)
OUTCOME = ("\n\n값 확인(CONFIRM_REQUIRED)·못 읽음(RETAKE_REQUIRED)·미인증(UNVERIFIED)은 오류가 아니라 200 + status. "
           "이미 정해진 기록에 다시 보내면 처음 결과를 그대로 돌려준다(응답을 못 받은 앱의 재시도).")
PHOTO, SKIP = "PHOTO", "SKIP"  # verifications.method(마감으로 닫힌 것은 시도가 아니라 줄 없음)
JPEG_MAGIC = b"\xff\xd8\xff"
MB = 1024 * 1024


# ---------- 사진 받기 ----------
BODY_LIMIT = POLICY.max_photos * POLICY.max_photo_bytes + 64 * 1024  # 사진 + multipart 머리


class _InMemoryParser(MultiPartParser):
    """요청 크기 상한까지는 임시 파일(디스크)로 넘기지 않는다(Starlette 기본은 1 MB부터 디스크). 이 API에서만 씀."""

    spool_max_size = BODY_LIMIT


def photo_target(runId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db),
                 engine: Optional[OcrEngine] = Depends(get_ocr)) -> None:
    """사진 본문을 읽기 전에: 도착 시각(마감 판단 기준), 내 기록·끝난 러닝, 인증 대기인데 엔진이 없으면 503."""
    request.state.arrived = now_utc()
    run = _finished(my_run(db, runId, user))
    blocked = run.status == PENDING and engine is None and not is_expired(run, request.state.arrived)
    # 사진 본문(최대 수 MB, 느린 LTE)을 읽는 동안 DB 연결·트랜잭션을 잡고 있지 않게(#55 리뷰: 동시 업로드 15개면 연결이 바닥남)
    db.rollback()
    if blocked:
        raise ApiError(E.SERVICE_UNAVAILABLE, "계기판 읽기를 아직 쓸 수 없습니다.")


async def read_photos(request: Request, _: None = Depends(photo_target)) -> list[Photo]:
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
class OcrUnavailable(ApiError):
    """OCR을 지금 못 함 → 503. outcome은 지표 결과 이름(계획 0087, metrics.observe_ocr): busy·timeout·stuck·엔진 reason·error"""

    def __init__(self, outcome: str, message: str, details=None):
        super().__init__(E.SERVICE_UNAVAILABLE, message, details)
        self.outcome = outcome


class _OcrRunner:
    """OCR은 CPU를 쓰므로 요청 스레드와 따로, 시간 제한(BL-17 ⑥). 동시에 WORKERS개까지, 자리가 없으면 줄 세우지 않고 바로 503.
    시간을 넘긴 작업은 스레드를 멈출 수 없으므로 자리를 돌려주고 "버려진 작업"으로 센다(새 요청이 그 뒤에 줄 서지 않게
    일꾼을 그만큼 더 둠). 버려진 작업이 ABANDONED_MAX개 쌓이면 엔진이 멈춘 것이라 503 + 오류 기록(재시작 필요).
    진짜 엔진(OCR_ENGINE=onnx)은 자식 프로세스에서 돌고 이 제한보다 1초 먼저 스스로 끊어 그 프로세스를 죽인다(계획 0046).
    그래서 버려지는 스레드는 엔진 바깥 고장 때만 생긴다."""

    WORKERS, ABANDONED_MAX = 2, 2

    def __init__(self):
        self.pool = ThreadPoolExecutor(max_workers=self.WORKERS + self.ABANDONED_MAX, thread_name_prefix="ocr")
        self.lock = threading.Lock()
        self.running = 0
        self.abandoned = 0

    def read(self, engine: OcrEngine, photos: list[Photo], timeout_s: float) -> ConsoleRead:
        """읽기 + 지표(묶음 시간·결과, 계획 0087). 결과는 읽기 상태(READ 등) 또는 busy·timeout·error"""
        started = time.perf_counter()
        outcome = "error"
        try:
            result = self._read(engine, photos, timeout_s)
            outcome = result.status.value
            return result
        except OcrUnavailable as e:
            outcome = e.outcome
            raise
        finally:
            metrics.observe_ocr(outcome, time.perf_counter() - started)

    def _read(self, engine: OcrEngine, photos: list[Photo], timeout_s: float) -> ConsoleRead:
        with self.lock:
            if self.abandoned >= self.ABANDONED_MAX:
                log.error("OCR 엔진이 시간을 넘긴 작업 %d개에 멈춰 있음 — 서버 재시작 필요", self.abandoned)
                raise OcrUnavailable("stuck", "계기판 읽기를 잠시 쓸 수 없습니다.")
            if self.running >= self.WORKERS:
                raise OcrUnavailable("busy", "계기판 읽기가 밀려 있습니다. 잠시 뒤 다시 보내세요.")
            self.running += 1
        state = {"abandoned": False}

        def done(_):
            with self.lock:
                if state["abandoned"]:
                    self.abandoned -= 1
                else:
                    self.running -= 1

        try:
            future = self.pool.submit(engine.read, photos)
        except BaseException:
            with self.lock:
                self.running -= 1
            raise
        future.add_done_callback(done)
        try:
            try:
                return future.result(timeout=timeout_s)
            except FutureTimeout:
                with self.lock:
                    late = future.done()  # 시간을 막 넘기며 끝났으면 그 결과를 씀(버리면 다시 찍어야 함)
                    if not late:
                        state["abandoned"] = True
                        self.running -= 1
                        self.abandoned += 1
                if not late:   # 엔진 바깥 고장(진짜 엔진은 1초 먼저 스스로 끊음) → 사진 탓이 아니라 reason 없음(리뷰)
                    raise OcrUnavailable("timeout", "계기판 읽기가 시간 안에 끝나지 않았습니다.") from None
                return future.result()
        except BadPhoto:
            return UNREADABLE  # 사진을 못 엶 → 다시 찍기(횟수 셈)
        except EngineUnavailable as e:
            log.warning("계기판 OCR 503: %s", e)
            raise OcrUnavailable((e.reason or "unavailable").lower(), str(e),
                                 {"reason": e.reason} if e.reason else None) from None
        except ApiError:
            raise
        except Exception:
            # 엔진 고장은 503: 다시 찍기 횟수를 깎지 않음(고장 난 동안 기록이 미인증으로 굳지 않게). 원인은 서버 기록으로
            log.exception("OCR 엔진 오류")
            raise OcrUnavailable("error", "계기판을 읽다가 오류가 났습니다. 다시 보내 주세요.") from None


_ocr = _OcrRunner()


# ---------- 결과 ----------
OPEN = (S.RETAKE_REQUIRED.value, S.CONFIRM_REQUIRED.value)   # 시도 줄 중 기록을 정하지 않은 것
# 마감 전에 받은 사진의 값 확인은 이만큼 더 받음: 9분 59초에 보낸 사진이 읽는 동안·확인하는 동안 마감이 지나 "맞아요"를
# 못 누르는 일이 없게(계획 0045). 확인 응답의 retakeDeadline이 이 시각이라 앱은 그때까지 E-05b, 지나면 E-10
CONFIRM_GRACE = timedelta(minutes=2)


def _confirm_deadline(run: Run, attempt: Verification) -> datetime:
    """값 확인 마감. 확인할 시도가 있다는 것은 사진이 마감 전에 도착했다는 뜻(늦은 사진은 시도 없이 VERIFICATION_CLOSED)이라
    읽기가 마감을 넘겨 끝났어도 시도가 저장된 때부터 유예를 준다."""
    return max(verification_deadline(run), attempt.created_at + CONFIRM_GRACE)


def _result(run: Run, status: str, reason: Optional[str] = None, rows: Optional[list] = None,
            retakes_left: Optional[int] = None, console_read: Optional[tuple[Optional[int], Optional[int]]] = None,
            attempt_id: Optional[str] = None, deadline: Optional[datetime] = None, rewards: Optional[dict] = None) -> dict:
    """console_read: 값 확인(아직 정하지 않음)일 때 이번에 읽은 값(기록 칸엔 안 씀). deadline: 값 확인의 마감(유예 포함)."""
    console = None
    if console_read and console_read[0] is not None:
        console = {"distanceM": console_read[0], "durationS": console_read[1]}
    elif status not in OPEN and run.console_distance_m is not None:
        console = {"distanceM": run.console_distance_m, "durationS": run.console_duration_s}
    return {"status": status, "reason": reason, "console": console,
            "sensor": {"distanceM": run.sensor_distance_m, "durationS": run.sensor_duration_s},
            "comparison": rows or [],
            "retakeDeadline": (deadline or verification_deadline(run)) if status in OPEN else None,
            "retakesLeft": retakes_left, "attemptId": attempt_id, "rewards": rewards}



def _stored(db: Session, run: Run) -> dict:
    """이미 정해진 기록의 결과(정한 시도의 대조 표)."""
    last = db.scalar(select(Verification).where(Verification.run_id == run.id, Verification.status.not_in(OPEN))
                     .order_by(Verification.created_at.desc()).limit(1))
    return _result(run, run.status, run.unverified_reason, last.comparison if last else [], rewards=rewards_for(db, run))


def _decide_run(db: Session, run: Run, outcome: Outcome) -> None:
    """VERIFIED·UNVERIFIED면 기록 상태를 정하고 성장을 반영한다(공식 값: 인증 완료 = 계기판, 미인증 = finish 때의 센서).
    커밋은 부른 쪽(판정·반영이 한 트랜잭션)."""
    read = outcome.read
    run.status, run.unverified_reason = outcome.status.value, outcome.reason.value if outcome.reason else None
    if read is not None:
        run.console_distance_m, run.console_duration_s = read.distance_m, read.duration_s
    if outcome.status == S.VERIFIED:
        run.official_source = "CONSOLE"
        run.official_distance_m, run.official_duration_s = read.distance_m, read.duration_s
    settle(db, run)


def _save(db: Session, run: Run, outcome: Outcome, method: str, hashes: tuple[str, ...] = ()) -> dict:
    """한 번의 시도를 기록. 값 확인이면 읽은 값과 시도 번호를 돌려준다(센서 대조는 판정 뒤에)."""
    read, rows = outcome.read, [r.model_dump(by_alias=True) for r in outcome.rows]
    reason = outcome.reason.value if outcome.reason else None
    if outcome.status.value not in OPEN:
        _decide_run(db, run, outcome)
    attempt = Verification(id=new_id(), run_id=run.id, method=method, status=outcome.status.value, reason=reason,
                           read_distance_m=read.distance_m if read else None,
                           read_duration_s=read.duration_s if read else None, comparison=rows, photo_hashes=list(hashes))
    db.add(attempt)
    db.commit()
    if outcome.status == S.CONFIRM_REQUIRED:
        return _result(run, S.CONFIRM_REQUIRED.value, console_read=(read.distance_m, read.duration_s), attempt_id=attempt.id,
                       deadline=_confirm_deadline(run, attempt))
    return _result(run, outcome.status.value, reason, rows, rewards=rewards_for(db, run))


def _finished(run: Run) -> Run:
    if run.status == ACTIVE:
        raise ApiError(E.RUN_NOT_FINISHED)
    if run.status == DISCARDED:
        raise ApiError(E.VERIFICATION_CLOSED)
    return run


def _closed_by_deadline(db: Session, run: Run) -> bool:
    """마감으로 닫힌 기록(앱이 skip TIMEOUT으로 닫은 것과 구분: 그건 SKIP 줄이 있음)."""
    if run.status != UNVERIFIED or run.unverified_reason != TIMEOUT:
        return False
    return db.scalar(select(func.count()).select_from(Verification)
                     .where(Verification.run_id == run.id, Verification.method == SKIP)) == 0


def _close_if_expired(db: Session, run: Run, at: datetime) -> bool:
    """잠근 줄에서: at(요청이 도착한 때)에 마감이 지났으면 UNVERIFIED(TIMEOUT)로 닫음."""
    if not is_expired(run, at):
        return False
    close_expired(db, at, run_id=run.id)
    db.commit()
    db.refresh(run)
    return True


def _retakes_used(db: Session, run_id: str, before: Optional[datetime] = None) -> int:
    """이 기록에서 못 읽어 다시 찍게 한 횟수(before가 있으면 그 시도 전까지). 틀렸어요(값 확인 줄)는 안 셈(계획 0045)."""
    q = select(func.count()).select_from(Verification).where(Verification.run_id == run_id,
                                                              Verification.status == S.RETAKE_REQUIRED)
    if before is not None:
        q = q.where(Verification.created_at < before)
    return db.scalar(q) or 0


def _latest_photo(db: Session, run_id: str) -> Optional[Verification]:
    return db.scalar(select(Verification).where(Verification.run_id == run_id, Verification.method == PHOTO)
                     .order_by(Verification.created_at.desc(), Verification.id.desc()).limit(1))


def _seen(db: Session, run: Run, hashes: tuple[str, ...]) -> tuple[Optional[Verification], bool]:
    """(이 기록에 똑같은 사진 묶음으로 보낸, 아직 정하지 않은 시도, 다른 기록에 쓴 사진이 섞였는지)."""
    used = db.scalars(select(Verification).where(Verification.photo_hashes.overlap(list(hashes)))).all()
    same = next((v for v in used if v.run_id == run.id and v.status in OPEN
                 and tuple(sorted(v.photo_hashes)) == hashes), None)
    return same, any(v.run_id != run.id for v in used)


def _rejected(db: Session, run: Run) -> dict:
    """틀렸어요 → 다시 찍기(횟수 안 씀, 마감까지)."""
    left = POLICY.max_retakes - _retakes_used(db, run.id)
    return _result(run, S.RETAKE_REQUIRED.value, R.USER_REJECTED.value, retakes_left=left)


def _again(db: Session, run: Run, attempt: Verification) -> dict:
    """같은 사진 묶음을 다시 보냄(응답을 못 받은 재시도) → 그때와 같은 답, 횟수 안 셈."""
    left = POLICY.max_retakes - _retakes_used(db, run.id, before=attempt.created_at)
    if attempt.status == S.CONFIRM_REQUIRED:
        if attempt.confirmed is False:
            return _rejected(db, run)
        return _result(run, S.CONFIRM_REQUIRED.value, console_read=(attempt.read_distance_m, attempt.read_duration_s),
                       retakes_left=left, attempt_id=attempt.id, deadline=_confirm_deadline(run, attempt))
    return _result(run, S.RETAKE_REQUIRED.value, attempt.reason, attempt.comparison, left)


def _given_up(db: Session, run: Run) -> Outcome:
    """다시 찍기를 그만두고 미인증으로 저장(E-07·09). 마지막 시도가 읽을 수 없는 계기판이면 그 이유로."""
    last = _latest_photo(db, run.id)
    if last is not None and last.status == S.RETAKE_REQUIRED and last.reason == R.UNSUPPORTED_CONSOLE.value:
        return Outcome(S.UNVERIFIED, R.UNSUPPORTED_CONSOLE)
    return Outcome(S.UNVERIFIED, R.RETAKE_GIVEN_UP)


@router.post(
    "/runs/{runId}/verification/photo",
    summary="계기판 사진으로 인증",
    description=(
        "화면 E-04에서 E-09 · BL-16 ⑥, BL-17, BL-18. 앱 카메라로 찍은 계기판 사진(브래킷 1–"
        f"{POLICY.max_photos}장, JPEG, 한 장 {POLICY.max_photo_bytes // MB} MB 이하, 가이드 영역만 잘라서)을 multipart "
        "`photos`로 여러 번 붙여 보낸다. 서버가 읽고, 거리·시간을 다 읽었으면 **판정하지 않고** CONFIRM_REQUIRED + console(읽은 값)·"
        "attemptId·retakesLeft·retakeDeadline(계획 0045, E-05b: 앱은 읽은 값만 보여 주고 맞아요·틀렸어요 → POST …/verification/confirm). "
        f"못 읽으면 다시 찍기({POLICY.max_retakes}번)가 남았으면 RETAKE_REQUIRED + reason: UNSUPPORTED_CONSOLE(읽을 수 없는 계기판, "
        "E-09)·null(못 읽음, 한 칸만 읽힘 포함, E-07), 다 썼으면 UNVERIFIED(UNSUPPORTED_CONSOLE). "
        "값은 서버 설정. 사진은 메모리에서 읽고 버림(해시만 남김). "
        "다른 기록에 쓴 사진이 섞이면 못 읽은 것으로 봄. "
        f"마감(종료를 받은 뒤 {POLICY.deadline_s // 60}분, 요청이 도착한 때 기준)이 지나면 VERIFICATION_CLOSED"
        "(기록은 UNVERIFIED·TIMEOUT으로 닫힘). OCR 엔진이 없거나 밀렸거나 오류·시간 초과면 503(다시 찍기 횟수에 안 셈), 시간 초과는 details.reason=OCR_TIMEOUT. "
        "같은 값으로 읽힌 사진이 두 장이면 나머지는 안 읽음(앱은 0 EV를 먼저 보냄)." + OUTCOME),
    response_model=Envelope[VerificationResult],
    responses=ERRS,
    openapi_extra={"requestBody": {"required": True, "content": {"multipart/form-data": {"schema": {
        "type": "object", "required": ["photos"],
        "properties": {"photos": {"type": "array", "items": {"type": "string", "format": "binary"},
                                  "description": f"계기판 사진(JPEG) 1–{POLICY.max_photos}장. 브래킷이면 같은 이름으로 여러 장"}}}}}}},
)
def verify_photo(runId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db),
                 photos: list[Photo] = Depends(read_photos), engine: Optional[OcrEngine] = Depends(get_ocr)):
    arrived = request.state.arrived  # 마감은 도착한 때로(OCR이 오래 걸려도 마감 직전 사진이 거절되지 않게)
    run = _finished(my_run(db, runId, user))
    if is_expired(run, arrived):
        run = _finished(my_run(db, runId, user, lock=True))
        _close_if_expired(db, run, arrived)
    if _closed_by_deadline(db, run):
        raise ApiError(E.VERIFICATION_CLOSED)
    if run.status != PENDING:
        return ok(request, _stored(db, run))

    hashes = tuple(sorted({hashlib.sha256(p.data).hexdigest() for p in photos}))
    same, reused = _seen(db, run, hashes)
    if same is not None:
        return ok(request, _again(db, run, same))
    read = UNREADABLE
    if not reused:
        if engine is None:  # photo_target이 먼저 거르지만, 그 사이 상태가 바뀐 경우
            raise ApiError(E.SERVICE_UNAVAILABLE, "계기판 읽기를 아직 쓸 수 없습니다.")
        db.rollback()  # OCR 동안 DB 연결·트랜잭션을 잡지 않게
        read = _ocr.read(engine, photos, POLICY.ocr_timeout_s)

    run = _finished(my_run(db, runId, user, lock=True))  # OCR 동안 다른 요청이 정했을 수 있음
    if _closed_by_deadline(db, run):
        if arrived > verification_deadline(run):
            raise ApiError(E.VERIFICATION_CLOSED)
        # 이 사진은 마감 전에 왔는데 OCR 동안 늦게 온 다른 요청·목록 조회가 마감으로 닫음 → 이 사진으로 정함(#55 리뷰)
        run.status, run.unverified_reason = PENDING, None
    if run.status != PENDING:
        return ok(request, _stored(db, run))
    same, reused = _seen(db, run, hashes)  # 같은 사진이 동시에 온 경우
    if same is not None:
        return ok(request, _again(db, run, same))

    used = _retakes_used(db, run.id)
    outcome = decide(POLICY, UNREADABLE if reused else read, used)
    out = _save(db, run, outcome, PHOTO, hashes)
    if outcome.status.value in OPEN:
        out["retakesLeft"] = POLICY.max_retakes - used
    return ok(request, out)


@router.post(
    "/runs/{runId}/verification/confirm",
    summary="읽은 계기판 값 확인",
    description=(
        "화면 E-05b · BL-17 ⑤, 계획 0045. 사진 응답(CONFIRM_REQUIRED)의 읽은 값을 사용자가 사진과 비교해 답한다. 값은 고칠 수 없다. "
        "correct=true(맞아요) → 그 값으로 지금 판정: VERIFIED 또는 오차 초과면 다시 찍기 없이 UNVERIFIED(MISMATCH, 대조 표 포함). "
        "correct=false(틀렸어요) → RETAKE_REQUIRED + reason USER_REJECTED(다시 찍기 횟수 안 씀, 마감까지 다시 찍을 수 있음, 시도는 오독 "
        "기록으로 남음). 기록의 마지막 사진 시도에만, 한 번만: 새 사진을 보냈거나 다른 답을 이미 했으면 CONFIRM_NOT_PENDING. "
        "같은 답을 다시 보내면 처음 결과(재시도). 마감이 지났으면 VERIFICATION_CLOSED(마감 전에 받은 사진은 확인에 "
        f"{int(CONFIRM_GRACE.total_seconds() // 60)}분 더, 확인 응답의 retakeDeadline이 그 시각)." + OUTCOME),
    response_model=Envelope[VerificationResult],
    responses=error_responses(*ERR_CODES, E.CONFIRM_NOT_PENDING),
)
def verify_confirm(runId: str, body: ConfirmRequest, request: Request, user: User = Depends(current_user),
                   db: Session = Depends(get_db)):
    arrived = now_utc()  # 잠금을 기다린 시간 말고 도착한 때로 마감을 봄
    run = _finished(my_run(db, runId, user, lock=True))
    attempt = db.get(Verification, body.attempt_id)
    if attempt is None or attempt.run_id != run.id or attempt.method != PHOTO:
        raise invalid("attemptId", "이 기록의 사진 시도가 아닙니다")
    if arrived > _confirm_deadline(run, attempt):
        if run.status == PENDING:
            _close_if_expired(db, run, arrived)
        if run.status == PENDING or _closed_by_deadline(db, run):
            raise ApiError(E.VERIFICATION_CLOSED)
    elif not body.correct and arrived > verification_deadline(run) and (run.status == PENDING or _closed_by_deadline(db, run)):
        # 유예는 맞아요를 누를 시간이지 다시 찍을 시간이 아님: 마감 뒤 틀렸어요는 오독만 남기고 마감으로 닫음(앱은 E-10).
        # 기록을 다시 열면 PENDING인 채 남고, 지난 마감으로 다시 찍기를 안내하게 됨
        latest = _latest_photo(db, run.id)
        if latest is not None and latest.id == attempt.id and attempt.status == S.CONFIRM_REQUIRED and attempt.confirmed is None:
            attempt.confirmed = False
        if not (run.status == PENDING and _close_if_expired(db, run, arrived)):
            db.commit()
        raise ApiError(E.VERIFICATION_CLOSED)
    elif _closed_by_deadline(db, run):
        # 마감 전에 받은 사진을 유예 안에 확인했는데 그 사이 다른 요청·목록 조회가 마감으로 닫음 → 이 확인으로 정함(사진과 같게)
        run.status, run.unverified_reason = PENDING, None
    if run.status != PENDING:
        if attempt.confirmed is True and body.correct:   # 맞아요로 정한 뒤 응답을 못 받은 재시도
            return ok(request, _stored(db, run))
        raise ApiError(E.CONFIRM_NOT_PENDING)
    latest = _latest_photo(db, run.id)
    if latest is None or latest.id != attempt.id or attempt.status != S.CONFIRM_REQUIRED:
        raise ApiError(E.CONFIRM_NOT_PENDING)
    if attempt.confirmed is not None:   # 이미 틀렸어요(맞아요면 기록이 정해져 위에서 끝남)
        if attempt.confirmed == body.correct:
            return ok(request, _rejected(db, run))
        raise ApiError(E.CONFIRM_NOT_PENDING)

    attempt.confirmed = body.correct
    if not body.correct:
        db.commit()
        return ok(request, _rejected(db, run))
    read = ConsoleRead.read(attempt.read_distance_m, attempt.read_duration_s)
    paused_s = (run.ended_at - run.started_at).total_seconds() - run.sensor_duration_s
    outcome = confirmed(POLICY, read, run.sensor_distance_m, run.sensor_duration_s, paused_s)
    rows = [r.model_dump(by_alias=True) for r in outcome.rows]
    attempt.status, attempt.reason, attempt.comparison = (outcome.status.value,
                                                          outcome.reason.value if outcome.reason else None, rows)
    _decide_run(db, run, outcome)
    db.commit()
    return ok(request, _result(run, run.status, run.unverified_reason, rows, rewards=rewards_for(db, run)))


@router.post(
    "/runs/{runId}/verification/skip",
    summary="인증 없이 마무리",
    description="화면 D-22, E-09, E-10 · BL-16 ⑤, BL-19. 촬영 없이 마무리(SKIPPED)·촬영 시간 초과(TIMEOUT)·다시 찍기 포기"
    "(RETAKE_GIVEN_UP, E-07·09의 미인증으로 저장) → UNVERIFIED로 저장, 공식 값은 센서 값. 포기는 마지막 시도가 "
    "읽을 수 없는 계기판이면 그 이유로. 나중에 인증할 수 없다. 마감이 지났으면 이유는 TIMEOUT." + OUTCOME,
    response_model=Envelope[VerificationResult],
    responses=ERRS,
)
def verify_skip(runId: str, body: SkipRequest, request: Request, user: User = Depends(current_user),
                db: Session = Depends(get_db)):
    arrived = now_utc()  # 잠금을 기다린 시간 말고 도착한 때로 마감을 봄(사진 API와 같게, #55 리뷰)
    run = _finished(my_run(db, runId, user, lock=True))
    if run.status == PENDING and not _close_if_expired(db, run, arrived):
        outcome = _given_up(db, run) if body.reason.value == R.RETAKE_GIVEN_UP.value else Outcome(S.UNVERIFIED, R(body.reason.value))
        return ok(request, _save(db, run, outcome, SKIP))
    return ok(request, _stored(db, run))
