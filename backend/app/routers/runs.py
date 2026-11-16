"""러닝 기록.

달리는 동안·끝낼 때 쓰는 API(시작·속도 시계열·종료·끝나지 않은 러닝·버리기)는 DB와 연결(계획 0037, 민재).
읽는 API(목록·상세·공유·고스트)는 아직 스텁(BL-19·20, 지환 S5).

상태: ACTIVE(달리는 중) → PENDING(끝, 인증 대기) → VERIFIED·UNVERIFIED(인증 쪽에서). 복구 안 하고 버리면 DISCARDED.
끝나지 않은 러닝은 마지막 갱신(시작·시계열 올리기)부터 24시간 안이면 복구할 수 있다(BL-37 ③). 그 뒤엔 시계열·종료도 받지 않는다.
복구 화면에 보이는 건 가장 최근에 시작한 러닝이 끝나지 않았을 때 그것 하나(옛 러닝은 남기되 다시 묻지 않음).
"""
import math
from datetime import datetime, timedelta, timezone
from typing import Optional

from fastapi import APIRouter, Depends, Query, Request, Response
from sqlalchemy import func, select, update
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.orm import Session

from .. import stub_data as d
from ..core.auth import Principal, stub_user
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..core.security import current_user
from ..db.ids import new_id
from ..db.models import Course, CourseEntry, Run, RunGhost, RunSample, User
from ..db.session import get_db
from ..realtime.router import map_id_of
from ..schemas.common import CursorPage
from ..schemas.run import (
    MAX_SPEED_KMH,
    ActiveRun,
    GhostTrack,
    RunDetail,
    RunFinishRequest,
    RunStarted,
    RunStartRequest,
    RunSummary,
    RunUpdate,
    SamplesUpload,
)

router = APIRouter(prefix="/v1", tags=["Run"])
E = ErrorCode
AUTH = error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)
RUN_ERRORS = error_responses(E.RUN_NOT_FOUND, E.FORBIDDEN)
STUB_NOTE = "\n\n스텁: runId `run_other` → FORBIDDEN(남의 기록), 예시에 없는 ID → RUN_NOT_FOUND"

ACTIVE, PENDING, DISCARDED = "ACTIVE", "PENDING", "DISCARDED"
RECOVERABLE = timedelta(hours=24)       # 앱 종료 후 복구 가능 시간(BL-37 ③, 11/06 확정)
CLOCK_SKEW = timedelta(minutes=5)       # 기기 시계가 이만큼 어긋나도 받아 줌
DISTANCE_SLACK_M = 50                   # 평균 속도 상한 검사에서 반올림·짧은 러닝 여유


def _check_run(run_id: str, known: set[str]) -> None:
    if run_id == "run_other":
        raise ApiError(E.FORBIDDEN)
    if run_id not in known:
        raise ApiError(E.RUN_NOT_FOUND)


KNOWN = {r["runId"] for r in d.RUNS} | {d.RUN_STARTED["runId"]}


def _invalid(field: str, reason: str) -> ApiError:
    return ApiError(E.VALIDATION_ERROR, details={"fields": [{"field": field, "reason": reason}]})


def _my_run(db: Session, run_id: str, user: User, lock: bool = False) -> Run:
    """내 기록. 없으면 RUN_NOT_FOUND, 남의 것이면 FORBIDDEN. lock이면 이 요청이 끝날 때까지 줄을 잠금(끝내기·버리기와 겹치지 않게)."""
    q = select(Run).where(Run.id == run_id)
    run = db.scalar(q.with_for_update() if lock else q)
    if run is None:
        raise ApiError(E.RUN_NOT_FOUND)
    if run.user_id != user.id:
        raise ApiError(E.FORBIDDEN)
    return run


def _expired(run: Run) -> bool:
    """끝나지 않았지만 복구 기한(마지막 갱신 + 24시간)이 지난 러닝."""
    return run.status == ACTIVE and run.updated_at < datetime.now(timezone.utc) - RECOVERABLE


def _pace(distance_m: int, duration_s: int) -> Optional[int]:
    return round(duration_s * 1000 / distance_m) if distance_m > 0 else None


def _summary(run: Run, course_name: str, distance_m: int, duration_s: int, verification: str) -> dict:
    return {"runId": run.id, "startedAt": run.started_at, "courseId": run.course_id, "courseName": course_name,
            "distanceM": distance_m, "durationS": duration_s, "avgPaceSPerKm": _pace(distance_m, duration_s),
            "verification": verification, "shared": run.shared}


def _course_name(db: Session, course_id: str) -> str:
    return db.scalar(select(Course.name).where(Course.id == course_id)) or ""


def _finished_summary(db: Session, run: Run) -> dict:
    verification = run.status if run.status in ("VERIFIED", "UNVERIFIED") else PENDING
    return _summary(run, _course_name(db, run.course_id), run.official_distance_m or 0,
                    run.official_duration_s or 0, verification)


def _samples_so_far(db: Session, run_id: str) -> tuple[int, int]:
    """올라온 시계열로 센 (거리 m, 달린 시간 초). 끝나지 않은 러닝의 복구 화면용.
    묶음이 겹치면(앱이 다시 나눠 보냄) 앞 묶음이 덮은 칸은 한 번만 센다."""
    distance_m, covered = 0.0, 0.0
    rows = db.execute(select(RunSample.offset_s, RunSample.interval_s, RunSample.speeds_kmh)
                      .where(RunSample.run_id == run_id).order_by(RunSample.offset_s)).all()
    for offset_s, interval_s, speeds in rows:
        skip = max(0, math.ceil((covered - offset_s) / interval_s - 1e-6))
        rest = speeds[skip:]
        if not rest:
            continue
        distance_m += sum(rest) / 3.6 * interval_s
        covered = offset_s + len(speeds) * interval_s
    return round(distance_m), round(covered)


@router.post(
    "/runs",
    status_code=201,
    summary="러닝 시작",
    description="화면 D-09 · BL-11, BL-24. 카운트다운이 끝나면. 같은 clientRunId가 다시 오면 처음 결과를 돌려줌. "
    "mapInstanceId는 맵 입장(POST /courses/{courseId}/entries)에서 받은 값이어야 함(마지막으로 들어간 맵), startedAt은 그 입장 뒤. "
    "ghostRunId는 내 끝난 기록만. 공유 여부는 내 설정 feedShareDefault로 시작. "
    "끝나지 않은 이전 러닝이 있어도 그대로 두고 새로 만듦(서버가 몰래 버리지 않음, 계획 0037)",
    response_model=Envelope[RunStarted],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR)},
)
def start_run(body: RunStartRequest, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    existing = db.scalar(select(Run).where(Run.user_id == user.id, Run.client_run_id == body.client_run_id))
    if existing is not None:
        return ok(request, _started(existing))

    entry = db.execute(select(CourseEntry.course_id, CourseEntry.entered_at)
                       .where(CourseEntry.user_id == user.id)).first()
    if entry is None or map_id_of(entry.course_id) != body.map_instance_id:
        raise _invalid("mapInstanceId", "마지막으로 입장한 맵이 아닙니다")
    entered = entry.course_id
    if body.started_at.tzinfo is None:
        raise _invalid("startedAt", "시간대(Z 또는 +09:00)가 있어야 합니다")
    if body.started_at > datetime.now(timezone.utc) + CLOCK_SKEW:
        raise _invalid("startedAt", "미래 시각입니다")
    if body.started_at < entry.entered_at - CLOCK_SKEW:  # 카운트다운은 맵에 들어온 뒤
        raise _invalid("startedAt", "맵 입장보다 앞입니다")
    if body.ghost_run_id is not None:
        ghost = db.scalar(select(Run).where(Run.id == body.ghost_run_id))
        if ghost is None or ghost.user_id != user.id or ghost.status in (ACTIVE, DISCARDED):
            raise _invalid("ghostRunId", "내 끝난 기록이 아닙니다")

    run_id = new_id()
    # 같은 clientRunId가 동시에 두 번 와도(앱 다시 보내기) 한 줄만: 유니크 제약에 걸린 쪽은 아무것도 안 넣음
    inserted = db.execute(
        insert(Run).values(id=run_id, user_id=user.id, client_run_id=body.client_run_id, course_id=entered,
                           map_instance_id=body.map_instance_id, goal_type=body.goal.type.value,
                           goal_value=body.goal.value,  # NONE이면 None(Goal 검사)
                           status=ACTIVE, started_at=body.started_at, shared=user.feed_share_default)
        .on_conflict_do_nothing(index_elements=[Run.user_id, Run.client_run_id])
        .returning(Run.id)).scalar()
    if inserted is not None and body.ghost_run_id is not None:
        db.add(RunGhost(run_id=run_id, ghost_run_id=body.ghost_run_id))
    db.commit()
    run = db.scalar(select(Run).where(Run.user_id == user.id, Run.client_run_id == body.client_run_id))
    return ok(request, _started(run))


def _started(run: Run) -> dict:
    return {"runId": run.id, "clientRunId": run.client_run_id, "startedAt": run.started_at}


@router.get(
    "/runs/active",
    summary="끝나지 않은 러닝 찾기",
    description="화면 I-04 · BL-37. 앱 재실행 때. 가장 최근에 시작한 러닝이 끝나지 않았고 마지막 갱신(시작·시계열 올리기)부터 "
    "24시간 안이면 그것. 그보다 옛 러닝은 다시 묻지 않음. 거리·시간은 지금까지 올라온 시계열로 셈. 없으면 run=null",
    response_model=Envelope[ActiveRun],
    responses=AUTH,
)
def active_run(request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    run = db.scalar(select(Run).where(Run.user_id == user.id).order_by(Run.started_at.desc(), Run.id.desc()).limit(1))
    if run is None or run.status != ACTIVE or _expired(run):
        return ok(request, d.ACTIVE_RUN_NONE)
    distance_m, duration_s = _samples_so_far(db, run.id)
    return ok(request, {"run": _summary(run, _course_name(db, run.course_id), distance_m, duration_s, PENDING),
                        "recoverableUntil": run.updated_at + RECOVERABLE})


@router.get(
    "/runs",
    summary="내 기록 목록",
    description="화면 F-01, F-02, D-03 · BL-20, BL-24. 월별, 커서 방식. 고스트 고르기에도 씀. 기록이 없으면 items=[]",
    response_model=Envelope[CursorPage[RunSummary]],
    responses={**AUTH, **error_responses(E.VALIDATION_ERROR)},
)
def list_runs(
    request: Request,
    month: Optional[str] = Query(default=None, pattern=r"^\d{4}-\d{2}$", description="YYYY-MM. 없으면 최근부터"),
    cursor: Optional[str] = None,
    _: Principal = Depends(stub_user),
):
    return ok(request, {"items": d.RUNS, "nextCursor": None})


@router.get("/runs/{runId}", summary="기록 상세",
            description="화면 F-03, F-04, E-12 · BL-19, BL-20. 공식 값과 출처(계기판/센서), 그래프용 시계열, 함께 달린 러너" + STUB_NOTE,
            response_model=Envelope[RunDetail], responses={**AUTH, **RUN_ERRORS})
def get_run(runId: str, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, {**d.RUN_DETAIL, "runId": runId})


@router.patch("/runs/{runId}", summary="피드 공유 바꾸기",
              description="화면 E-12 · BL-19. 공유만. 기록 값 수정·삭제는 없음(PRD)" + STUB_NOTE,
              response_model=Envelope[RunSummary], responses={**AUTH, **RUN_ERRORS, **error_responses(E.VALIDATION_ERROR)})
def patch_run(runId: str, body: RunUpdate, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, {**d.RUNS[0], "runId": runId, "shared": body.shared})


@router.put(
    "/runs/{runId}/samples",
    status_code=204,
    summary="속도 시계열 올리기",
    description="화면 D-10 · BL-12 ⑥, BL-19 ④. 고스트 재생·인증 대조 근거. 앱은 60초마다 묶음으로 올림. "
    "offsetS는 달린 시간 기준(일시정지 구간은 빠짐). 같은 offsetS 묶음이 다시 오면 덮어씀(다시 보내도 하나). "
    "달리는 중(ACTIVE)인 기록만. 끝났거나 버렸거나 복구 기한(24시간)이 지났으면 RUN_ALREADY_FINISHED",
    responses={**AUTH, **RUN_ERRORS, **error_responses(E.VALIDATION_ERROR, E.RUN_ALREADY_FINISHED)},
)
def put_samples(runId: str, body: SamplesUpload, user: User = Depends(current_user), db: Session = Depends(get_db)):
    run = _my_run(db, runId, user, lock=True)
    if run.status != ACTIVE or _expired(run):
        raise ApiError(E.RUN_ALREADY_FINISHED)
    db.execute(insert(RunSample).values(run_id=run.id, offset_s=body.offset_s, interval_s=body.interval_s,
                                        speeds_kmh=body.speeds_kmh)
               .on_conflict_do_update(index_elements=[RunSample.run_id, RunSample.offset_s],
                                      set_={"interval_s": body.interval_s, "speeds_kmh": body.speeds_kmh}))
    db.execute(update(Run).where(Run.id == run.id).values(updated_at=func.now()))  # 복구 24시간을 마지막 올리기부터
    db.commit()
    return Response(status_code=204)


@router.post(
    "/runs/{runId}/finish",
    summary="러닝 종료",
    description="화면 D-18, D-19 · BL-14 ②. 센서로 잰 거리·시간(일시정지 뺀 달린 시간)·종료 시각 → 인증 대기(PENDING). "
    "공식 값은 인증 전까지 센서 값. 응답이 안 와서 같은 endedAt으로 다시 보내면 처음 결과를 돌려줌. "
    "다른 값으로 다시 끝내거나 버렸거나 복구 기한(24시간)이 지났으면 RUN_ALREADY_FINISHED. 평균 속도가 30 km/h를 넘으면 400",
    response_model=Envelope[RunSummary],
    responses={**AUTH, **RUN_ERRORS, **error_responses(E.VALIDATION_ERROR, E.RUN_ALREADY_FINISHED)},
)
def finish_run(runId: str, body: RunFinishRequest, request: Request, user: User = Depends(current_user),
               db: Session = Depends(get_db)):
    run = _my_run(db, runId, user, lock=True)
    if run.status != ACTIVE:
        if run.status != DISCARDED and run.ended_at == body.ended_at and run.sensor_distance_m == body.sensor.distance_m \
                and run.sensor_duration_s == body.sensor.duration_s:
            return ok(request, _finished_summary(db, run))  # 응답을 못 받은 앱이 다시 보냄
        raise ApiError(E.RUN_ALREADY_FINISHED)
    if _expired(run):
        raise ApiError(E.RUN_ALREADY_FINISHED)
    if body.ended_at.tzinfo is None:
        raise _invalid("endedAt", "시간대(Z 또는 +09:00)가 있어야 합니다")
    if body.ended_at < run.started_at:
        raise _invalid("endedAt", "시작 시각보다 앞입니다")
    if body.ended_at > datetime.now(timezone.utc) + CLOCK_SKEW:
        raise _invalid("endedAt", "미래 시각입니다")
    if body.sensor.duration_s > (body.ended_at - run.started_at).total_seconds() + 1:
        raise _invalid("sensor.durationS", "시작부터 종료까지보다 깁니다")
    if body.sensor.distance_m > MAX_SPEED_KMH / 3.6 * body.sensor.duration_s + DISTANCE_SLACK_M:
        raise _invalid("sensor.distanceM", f"평균 {MAX_SPEED_KMH:.0f} km/h를 넘습니다")
    run.status = PENDING
    run.ended_at = body.ended_at
    run.sensor_distance_m = body.sensor.distance_m
    run.sensor_duration_s = body.sensor.duration_s
    run.official_source = "SENSOR"
    run.official_distance_m = body.sensor.distance_m
    run.official_duration_s = body.sensor.duration_s
    db.commit()
    return ok(request, _finished_summary(db, run))


@router.post(
    "/runs/{runId}/discard",
    status_code=204,
    summary="복구한 러닝 버리기",
    description="화면 I-04 · BL-37 ②. 끝나지 않은 러닝만. 이미 버렸으면 그대로 204. 끝난 기록은 지울 수 없음(PRD) → RUN_ALREADY_FINISHED",
    responses={**AUTH, **RUN_ERRORS, **error_responses(E.RUN_ALREADY_FINISHED)},
)
def discard_run(runId: str, user: User = Depends(current_user), db: Session = Depends(get_db)):
    run = _my_run(db, runId, user, lock=True)
    if run.status == ACTIVE:
        run.status = DISCARDED
        db.commit()
    elif run.status != DISCARDED:
        raise ApiError(E.RUN_ALREADY_FINISHED)
    return Response(status_code=204)


@router.get("/runs/{runId}/ghost", summary="고스트 재생용 시계열",
            description="화면 D-13 · BL-24. 본인 기록만, 코스와 무관하게 씀(PRD F04)" + STUB_NOTE,
            response_model=Envelope[GhostTrack], responses={**AUTH, **RUN_ERRORS})
def ghost(runId: str, request: Request, _: Principal = Depends(stub_user)):
    _check_run(runId, KNOWN)
    return ok(request, {**d.GHOST, "runId": runId})
