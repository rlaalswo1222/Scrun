"""러닝 기록.

달리는 동안·끝낼 때 쓰는 API(시작·속도 시계열·종료·끝나지 않은 러닝·버리기)는 계획 0037,
읽는 API(목록·상세·공유·고스트)는 계획 0043(BL-19·20·24)에서 DB와 연결(민재).
읽는 API는 끝난 기록(PENDING·VERIFIED·UNVERIFIED)만 보이고, 읽기 전에 마감 지난 인증 대기를 닫는다(close_expired).

상태: ACTIVE(달리는 중) → PENDING(끝, 인증 대기) → VERIFIED·UNVERIFIED(인증 쪽에서). 복구 안 하고 버리면 DISCARDED.
끝나지 않은 러닝은 마지막 갱신(시작·시계열 올리기)부터 24시간 안이면 복구할 수 있다(BL-37 ③). 그 뒤엔 시계열·종료도 받지 않는다.
복구 화면에 보이는 건 가장 최근에 시작한 러닝이 끝나지 않았을 때 그것 하나(옛 러닝은 남기되 다시 묻지 않음).
"""
import math
from datetime import datetime, timedelta, timezone
from typing import Optional
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from fastapi import APIRouter, Depends, Query, Request, Response
from sqlalchemy import func, or_, select, tuple_, update
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.orm import Session

from .. import stub_data as d
from ..core import cursor as cursors
from ..core.config import settings
from ..core.errors import invalid as _invalid
from ..core.run_access import ACTIVE, DISCARDED, PENDING, UNVERIFIED, VERIFIED, close_expired
from ..core.run_access import my_run as _my_run
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..core.security import current_user
from ..db.ids import new_id
from ..db.models import Course, CourseEntry, Friendship, Run, RunCompanion, RunGhost, RunSample, User
from ..db.session import get_db
from ..realtime.hub import RUNNER
from ..realtime.link import hub_link
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
# 오류 코드 묶음. 문서는 error_responses 한 번에 모아 넘긴다: dict를 {**a, **b}로 합치면 같은 상태 코드(403 SIGNUP_REQUIRED·FORBIDDEN)가 덮어써짐
AUTH = (E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)
RUN_ERRORS = (E.RUN_NOT_FOUND, E.FORBIDDEN)
FINISHED = (PENDING, VERIFIED, UNVERIFIED)  # 읽는 API에 보이는 기록
READ_NOTE = "\n\n남의 기록이면 FORBIDDEN, 없거나 아직 달리는 중·버린 기록이면 RUN_NOT_FOUND"

RECOVERABLE = timedelta(hours=24)       # 앱 종료 후 복구 가능 시간(BL-37 ③, 11/06 확정)
CLOCK_SKEW = timedelta(minutes=5)       # 기기 시계가 이만큼 어긋나도 받아 줌
DISTANCE_SLACK_M = 50                   # 평균 속도 상한 검사에서 반올림·짧은 러닝 여유
PAGE_SIZE = 20                          # 기록 목록 한 쪽
GRAPH_POINTS = 120                      # 상세 그래프 시계열 최대 칸 수


def deadline_on_finish(ended_at: datetime, received_at: datetime, sent_at: Optional[datetime]) -> datetime:
    """인증 마감(계획 0052 정할 것 2 가). 창 = VERIFY_DEADLINE_S(10분).
    멈춘 뒤 지난 시간 = sentAt − endedAt(둘 다 앱 시계라 폰 시계가 틀려도 맞음, 서버 시각과 섞으면 시계가 늦은 폰은 정상 종료도 닫힘).
    창 안이면 받은 때부터 창(응답이 늦어도 줄지 않음), 넘으면 받은 때 바로 닫힘: 앱이 꺼졌다 오래 뒤 복구했거나 폰에 남겼다 나중에 보낸
    기록이라 그때 찍은 계기판 숫자는 그 러닝 값이 아님(러닝머신을 끄거나 다음 사람이 씀). sentAt이 없으면(옛 앱) 방금 끝난 것으로."""
    window = timedelta(seconds=settings.verification.deadline_s)
    stopped = sent_at - ended_at if sent_at is not None and sent_at.tzinfo is not None else timedelta(0)
    return received_at + window if stopped <= window else received_at


def _expired(run: Run) -> bool:
    """끝나지 않았지만 복구 기한(마지막 갱신 + 24시간)이 지난 러닝."""
    return run.status == ACTIVE and run.updated_at < datetime.now(timezone.utc) - RECOVERABLE


def _pace(distance_m: int, duration_s: int) -> Optional[int]:
    return round(duration_s * 1000 / distance_m) if distance_m > 0 else None


def _summary(run: Run, course_name: str, distance_m: int, duration_s: int, verification: str) -> dict:
    return {"runId": run.id, "startedAt": run.started_at, "courseId": run.course_id, "courseName": course_name,
            "distanceM": distance_m, "durationS": duration_s, "avgPaceSPerKm": _pace(distance_m, duration_s),
            "verification": verification, "shared": run.shared}


def _finished_query(user: User):
    """내 끝낸 러닝(목록·홈이 같이 씀). 순서는 부른 쪽이 _NEWEST로."""
    return (select(Run, Course.name).join(Course, Course.id == Run.course_id)
            .where(Run.user_id == user.id, Run.status.in_(FINISHED)))


_NEWEST = (Run.started_at.desc(), Run.id.desc())


def _list_row(run: Run, course_name: str) -> dict:
    return _summary(run, course_name, run.official_distance_m or 0, run.official_duration_s or 0, run.status)


def recent_rows(db: Session, user: User, limit: int) -> list[dict]:
    """끝낸 러닝 최근 것부터 limit개(목록과 같은 모양). 마감 닫기(close_expired)는 부른 쪽이 먼저. 홈(GET /home)이 쓴다."""
    return [_list_row(run, name) for run, name in db.execute(_finished_query(user).order_by(*_NEWEST).limit(limit)).all()]


def _course_name(db: Session, course_id: str) -> str:
    return db.scalar(select(Course.name).where(Course.id == course_id)) or ""


def _finished_summary(db: Session, run: Run) -> dict:
    verification = run.status if run.status in (VERIFIED, UNVERIFIED) else PENDING
    return _summary(run, _course_name(db, run.course_id), run.official_distance_m or 0,
                    run.official_duration_s or 0, verification)


Segment = tuple[float, float, float]  # (달린 시간 기준 시작 초, 길이 초, km/h)


def _segments(db: Session, run_id: str) -> list[Segment]:
    """올라온 시계열을 시간순 칸으로. 묶음이 겹치면(앱이 다시 나눠 보냄) 앞 묶음이 덮은 칸은 한 번만 쓴다."""
    out: list[Segment] = []
    covered = 0.0
    rows = db.execute(select(RunSample.offset_s, RunSample.interval_s, RunSample.speeds_kmh)
                      .where(RunSample.run_id == run_id).order_by(RunSample.offset_s)).all()
    for offset_s, interval_s, speeds in rows:
        skip = max(0, math.ceil((covered - offset_s) / interval_s - 1e-6))
        if skip >= len(speeds):
            continue
        out.extend((offset_s + i * interval_s, interval_s, speeds[i]) for i in range(skip, len(speeds)))
        covered = offset_s + len(speeds) * interval_s
    return out


def _samples_so_far(db: Session, run_id: str) -> tuple[int, int]:
    """올라온 시계열로 센 (거리 m, 달린 시간 초). 끝나지 않은 러닝의 복구 화면용."""
    segs = _segments(db, run_id)
    distance_m = sum(v / 3.6 * dur for _, dur, v in segs)
    return round(distance_m), round(segs[-1][0] + segs[-1][1]) if segs else 0


def _resample(segs: list[Segment], end: float, step: float) -> list[float]:
    """step초 칸마다 시간 가중 평균 속도. 시계열이 빠진 칸(못 올라온 묶음)은 앞 칸 값을 이어 쓴다(처음이면 0)."""
    n = max(1, math.ceil(end / step - 1e-9))
    acc, weight = [0.0] * n, [0.0] * n
    for t, dur, v in segs:
        stop = t + dur
        while t < stop - 1e-9:
            i = min(int(t // step), n - 1)
            part = min(stop, (i + 1) * step) - t
            if part <= 0:  # 부동소수 끝자락: 남은 건 마지막 칸에
                part = stop - t
            acc[i] += v * part
            weight[i] += part
            t += part
    out, last = [], 0.0
    for a, w in zip(acc, weight):
        last = a / w if w > 0 else last
        out.append(round(last, 2))
    return out


def _series(db: Session, run_id: str, max_points: Optional[int] = None) -> dict:
    """속도 시계열(SpeedSeries 모양). 칸 간격은 정수 초: 올라온 가장 짧은 간격을 올림(앱은 1초, 1초보다 짧으면 1초로 평균).
    max_points가 있으면 간격을 늘려 그 수 안으로 줄인다. 칸 수는 끝 시각(최대 하루 + 묶음 길이) / 1초로 묶여 있음."""
    segs = _segments(db, run_id)
    if not segs:
        return {"intervalS": 1.0, "speedsKmh": []}
    end = segs[-1][0] + segs[-1][1]
    step = max(1, math.ceil(min(dur for _, dur, _ in segs) - 1e-6))
    if max_points is not None:
        step = max(step, math.ceil(end / max_points))
    return {"intervalS": float(step), "speedsKmh": _resample(segs, end, float(step))}


@router.post(
    "/runs",
    status_code=201,
    summary="러닝 시작",
    description="화면 D-09 · BL-11, BL-24. 카운트다운이 끝나면. 같은 clientRunId가 다시 오면 처음 결과를 돌려줌. "
    "mapInstanceId는 맵 입장(POST /courses/{courseId}/entries)에서 받은 값이어야 함(마지막으로 들어간 맵), startedAt은 그 입장 뒤. "
    "ghostRunId는 내 끝난 기록만. 공유 여부는 내 설정 feedShareDefault로 시작. "
    "끝나지 않은 이전 러닝이 있어도 그대로 두고 새로 만듦(서버가 몰래 버리지 않음, 계획 0037)",
    response_model=Envelope[RunStarted],
    responses=error_responses(*AUTH, E.VALIDATION_ERROR),
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
    if inserted is not None:
        hub_link.start_tracking(user.id, run_id)  # 이번 러닝에서 같은 방에서 같이 달린 사람을 모음(종료 때 저장)
    run = db.scalar(select(Run).where(Run.user_id == user.id, Run.client_run_id == body.client_run_id))
    return ok(request, _started(run))


def _started(run: Run) -> dict:
    return {"runId": run.id, "clientRunId": run.client_run_id, "startedAt": run.started_at}


@router.get(
    "/runs/active",
    summary="끝나지 않은 러닝 찾기",
    description="화면 I-04 · BL-37. 앱 재실행 때. 가장 최근에 시작한 러닝이 끝나지 않았고 마지막 갱신(시작·시계열 올리기)부터 "
    "24시간 안이면 그것. 그보다 옛 러닝은 다시 묻지 않음. 거리·시간은 지금까지 올라온 시계열로 셈, lastActiveAt은 마지막 갱신 시각"
    "(폰에 진행 기록이 없을 때 종료 시각, 계획 0052). 없으면 run=null",
    response_model=Envelope[ActiveRun],
    responses=error_responses(*AUTH),
)
def active_run(request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    run = db.scalar(select(Run).where(Run.user_id == user.id).order_by(Run.started_at.desc(), Run.id.desc()).limit(1))
    if run is None or run.status != ACTIVE or _expired(run):
        return ok(request, d.ACTIVE_RUN_NONE)
    distance_m, duration_s = _samples_so_far(db, run.id)
    return ok(request, {"run": _summary(run, _course_name(db, run.course_id), distance_m, duration_s, PENDING),
                        "recoverableUntil": run.updated_at + RECOVERABLE, "lastActiveAt": run.updated_at})


def _month_range(month: str, tz_name: str) -> tuple[datetime, datetime]:
    """YYYY-MM을 내 시간대의 그 달 [처음, 다음 달 처음)으로."""
    year, mon = int(month[:4]), int(month[5:])
    if not 1 <= mon <= 12:
        raise _invalid("month", "월은 01에서 12입니다")
    if not 2000 <= year <= 9998:  # datetime 범위 밖(0000·9999-12의 다음 달)이 500이 되지 않게
        raise _invalid("month", "연도는 2000에서 9998입니다")
    try:
        tz = ZoneInfo(tz_name)
    except (ZoneInfoNotFoundError, ValueError):
        tz = ZoneInfo("Asia/Seoul")  # 가입 때 IANA 이름만 받지만(계획 0028) 옛 값이면 기본 시간대
    start = datetime(year, mon, 1, tzinfo=tz)
    end = datetime(year + mon // 12, mon % 12 + 1, 1, tzinfo=tz)
    return start, end


def _read_run(db: Session, run_id: str, user: User, lock: bool = False) -> Run:
    """읽는 API가 보는 내 끝난 기록. 마감 지난 인증 대기면 먼저 닫는다."""
    run = _my_run(db, run_id, user, lock=lock)
    if run.status not in FINISHED:
        raise ApiError(E.RUN_NOT_FOUND)
    if close_expired(db, run_id=run.id):
        db.commit()
        run = _my_run(db, run_id, user, lock=lock)
    return run


@router.get(
    "/runs",
    summary="내 기록 목록",
    description="화면 F-01, F-02, D-03 · BL-20, BL-24. 끝난 기록(인증 대기·완료·미인증)만 최신순, 커서 방식(한 쪽 20개). "
    "month(YYYY-MM)는 내 시간대 기준 달. 고스트 고르기에도 씀. 기록이 없으면 items=[]",
    response_model=Envelope[CursorPage[RunSummary]],
    responses=error_responses(*AUTH, E.VALIDATION_ERROR),
)
def list_runs(
    request: Request,
    month: Optional[str] = Query(default=None, pattern=r"^\d{4}-\d{2}$", description="YYYY-MM. 없으면 최근부터"),
    cursor: Optional[str] = Query(default=None, description="앞 쪽 응답의 nextCursor"),
    user: User = Depends(current_user),
    db: Session = Depends(get_db),
):
    if close_expired(db, user_id=user.id):
        db.commit()
    q = _finished_query(user)
    if month is not None:
        start, end = _month_range(month, user.timezone)
        q = q.where(Run.started_at >= start, Run.started_at < end)
    if cursor is not None:
        at, run_id = cursors.decode(cursor)
        q = q.where(tuple_(Run.started_at, Run.id) < tuple_(at, run_id))
    rows = db.execute(q.order_by(*_NEWEST).limit(PAGE_SIZE + 1)).all()
    page = rows[:PAGE_SIZE]
    items = [_list_row(run, name) for run, name in page]
    next_cursor = cursors.encode(page[-1][0].started_at, page[-1][0].id) if len(rows) > PAGE_SIZE else None
    return ok(request, {"items": items, "nextCursor": next_cursor})


def _companions(db: Session, run: Run) -> list[dict]:
    """함께 달린 사람·봇. 친구인지는 지금 친구 표로(나중에 맺거나 끊을 수 있어서 저장하지 않음), 이름은 지금 닉네임."""
    rows = db.execute(select(RunCompanion.kind, RunCompanion.companion_user_id, RunCompanion.bot_label, User.nickname)
                      .outerjoin(User, User.id == RunCompanion.companion_user_id)
                      .where(RunCompanion.run_id == run.id).order_by(RunCompanion.seq)).all()
    ids = [uid for _, uid, _, _ in rows if uid]
    friends: set[str] = set()
    if ids:
        me = run.user_id
        for a, b in db.execute(select(Friendship.user_a_id, Friendship.user_b_id).where(or_(
                (Friendship.user_a_id == me) & Friendship.user_b_id.in_(ids),
                (Friendship.user_b_id == me) & Friendship.user_a_id.in_(ids)))).all():
            friends.add(b if a == me else a)
    out = []
    for kind, uid, bot_label, nickname in rows:
        if kind == RUNNER:
            out.append({"nickname": nickname or "", "kind": "FRIEND" if uid in friends else "RUNNER"})
        else:
            out.append({"nickname": bot_label or "", "kind": kind})
    return out


@router.get("/runs/{runId}", summary="기록 상세",
            description="화면 F-03, F-04, E-12 · BL-19, BL-20. 시작할 때 고른 목표, 공식 값과 출처(계기판/센서), 센서 값, 계기판에서 읽은 값(사진을 읽었을 때), "
            f"그래프용 시계열(최대 {GRAPH_POINTS}칸, 칸 간격은 정수 초), 함께 달린 러너(같은 맵에서 같이 달린 사람·봇, 친구 표시)"
            + READ_NOTE,
            response_model=Envelope[RunDetail], responses=error_responses(*AUTH, *RUN_ERRORS))
def get_run(runId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    run = _read_run(db, runId, user)
    ghost_id = db.scalar(select(RunGhost.ghost_run_id).where(RunGhost.run_id == run.id))
    console = None
    if run.console_distance_m is not None:  # 인증 결과 응답과 같게: 읽었으면 미인증(값 어긋남)이어도 보임
        console = {"distanceM": run.console_distance_m, "durationS": run.console_duration_s or 0}
    return ok(request, {
        **_finished_summary(db, run),
        "goal": {"type": run.goal_type, "value": run.goal_value},
        "officialSource": run.official_source or "SENSOR",
        "sensor": {"distanceM": run.sensor_distance_m or 0, "durationS": run.sensor_duration_s or 0},
        "console": console,
        "speedSeries": _series(db, run.id, max_points=GRAPH_POINTS),
        "companions": _companions(db, run),
        "ghostRunId": ghost_id,
    })


@router.patch("/runs/{runId}", summary="피드 공유 바꾸기",
              description="화면 E-12 · BL-19. 공유만. 기록 값 수정·삭제는 없음(PRD). 끄면 친구 피드에서 빠짐" + READ_NOTE,
              response_model=Envelope[RunSummary], responses=error_responses(*AUTH, *RUN_ERRORS, E.VALIDATION_ERROR))
def patch_run(runId: str, body: RunUpdate, request: Request, user: User = Depends(current_user),
              db: Session = Depends(get_db)):
    run = _read_run(db, runId, user, lock=True)
    run.shared = body.shared
    db.commit()
    db.refresh(run)
    return ok(request, _finished_summary(db, run))


@router.put(
    "/runs/{runId}/samples",
    status_code=204,
    summary="속도 시계열 올리기",
    description="화면 D-10 · BL-12 ⑥, BL-19 ④. 고스트 재생·인증 대조 근거. 앱은 60초마다 묶음으로 올림. "
    "offsetS는 달린 시간 기준(일시정지 구간은 빠짐). 같은 offsetS 묶음이 다시 오면 덮어씀(다시 보내도 하나). "
    "달리는 중(ACTIVE)인 기록만. 끝났거나 버렸거나 복구 기한(24시간)이 지났으면 RUN_ALREADY_FINISHED",
    responses=error_responses(*AUTH, *RUN_ERRORS, E.VALIDATION_ERROR, E.RUN_ALREADY_FINISHED),
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
    "다른 값으로 다시 끝내거나 버렸거나 복구 기한(24시간)이 지났으면 RUN_ALREADY_FINISHED. 평균 속도가 30 km/h를 넘으면 400. "
    "인증 마감은 받은 때부터 10분. 단 sentAt − endedAt(앱 시계로 멈춘 뒤 지난 시간)이 10분을 넘으면 바로 미인증(앱이 꺼졌다 늦게 복구, 계획 0052)",
    response_model=Envelope[RunSummary],
    responses=error_responses(*AUTH, *RUN_ERRORS, E.VALIDATION_ERROR, E.RUN_ALREADY_FINISHED),
)
def finish_run(runId: str, body: RunFinishRequest, request: Request, user: User = Depends(current_user),
               db: Session = Depends(get_db)):
    run = _my_run(db, runId, user, lock=True)
    if run.status != ACTIVE:
        if run.status != DISCARDED and run.ended_at == body.ended_at and run.sensor_distance_m == body.sensor.distance_m \
                and run.sensor_duration_s == body.sensor.duration_s:
            # 응답을 못 받은 앱이 다시 보냄. 그 사이 인증 마감이 지났으면 닫힌 상태로 답함(#55 리뷰: PENDING으로 보이면 앱이 촬영으로 감)
            if close_expired(db, run_id=run.id):
                db.commit()
                db.refresh(run)
            return ok(request, _finished_summary(db, run))
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
    run.verification_deadline = deadline_on_finish(body.ended_at, datetime.now(timezone.utc), body.sent_at)
    _save_companions(db, run)
    db.commit()
    hub_link.stop_tracking(user.id, run.id)  # 커밋 뒤에 지움(커밋이 실패해 앱이 다시 보내면 다시 넣게)
    # 오래전에 꺼진 러닝을 복구해 끝냄 → 마감이 이미 지남: 바로 미인증으로 닫아 응답이 PENDING으로 보이지 않게(앱이 촬영으로 가지 않음)
    if close_expired(db, run_id=run.id):
        db.commit()
        db.refresh(run)
    return ok(request, _finished_summary(db, run))


def _save_companions(db: Session, run: Run) -> None:
    """실시간 방에서 모은 함께 달린 사람·봇을 남긴다. 그 사이 탈퇴한 사람은 뺀다(외래 키)."""
    found = hub_link.companions(run.user_id, run.id)
    ids = {c.user_id for c in found if c.user_id}
    alive = set(db.scalars(select(User.id).where(User.id.in_(ids)))) if ids else set()
    seq = 0
    for c in found:
        if c.user_id is not None and c.user_id not in alive:
            continue
        db.add(RunCompanion(run_id=run.id, seq=seq, kind=c.kind, companion_user_id=c.user_id,
                            bot_label=None if c.user_id else c.label))
        seq += 1


@router.post(
    "/runs/{runId}/discard",
    status_code=204,
    summary="복구한 러닝 버리기",
    description="화면 I-04 · BL-37 ②. 끝나지 않은 러닝만. 이미 버렸으면 그대로 204. 끝난 기록은 지울 수 없음(PRD) → RUN_ALREADY_FINISHED",
    responses=error_responses(*AUTH, *RUN_ERRORS, E.RUN_ALREADY_FINISHED),
)
def discard_run(runId: str, user: User = Depends(current_user), db: Session = Depends(get_db)):
    run = _my_run(db, runId, user, lock=True)
    if run.status == ACTIVE:
        run.status = DISCARDED
        db.commit()
        hub_link.stop_tracking(user.id, run.id)
    elif run.status != DISCARDED:
        raise ApiError(E.RUN_ALREADY_FINISHED)
    return Response(status_code=204)


@router.get("/runs/{runId}/ghost", summary="고스트 재생용 시계열",
            description="화면 D-13 · BL-24. 본인 끝난 기록만, 코스와 무관하게 씀(PRD F04). 시계열은 정수 초 칸(앱은 1초 그대로), "
            "빠진 칸은 앞 칸 값" + READ_NOTE,
            response_model=Envelope[GhostTrack], responses=error_responses(*AUTH, *RUN_ERRORS))
def ghost(runId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    run = _read_run(db, runId, user)
    return ok(request, {"runId": run.id, "distanceM": run.official_distance_m or 0,
                        "durationS": run.official_duration_s or 0, "speedSeries": _series(db, run.id)})
