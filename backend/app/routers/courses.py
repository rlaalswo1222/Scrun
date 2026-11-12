"""코스 목록·상세·맵 입장(BL-7). 계획 0021.

코스 하나 = 맵 하나(docs/api/README.md "코스와 맵 인원"). 인원은 core/occupancy.count_present 한 곳에서 센다.
입장은 코스 줄을 잠그고(SELECT FOR NO KEY UPDATE) 세서, 동시에 들어와도 최대 인원을 넘지 않는다.
입장 응답에 실시간 방(계획 0029, ADR-0010) 접속 주소·토큰을 준다. 입장 위치는 방에 달리는 사람이 있으면
그중 한 명의 20–40m 뒤(앞사람이 보이게, BL-25 ②), 없으면 코스 위 랜덤.
"""
import random

from fastapi import APIRouter, Depends, Request
from sqlalchemy import exists, func, select
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.orm import Session

from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..core.occupancy import count_present
from ..core import tokens
from ..core.security import current_user
from ..db.models import Calibration, CourseEntry, User
from ..db.models import Course as CourseRow
from ..db.session import get_db
from ..realtime.hub import hub
from ..realtime.router import map_id_of
from ..schemas.course import Course, CourseDetail, MapEntry

router = APIRouter(prefix="/v1", tags=["Course"])
E = ErrorCode
AUTH = error_responses(E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)

_rng = random.Random()


BEHIND_M = (20.0, 40.0)  # 달리는 사람 뒤 이만큼에 입장


def get_rng() -> random.Random:
    """입장 위치용 난수. 예시 만들기(scripts/real_examples.py)는 시드를 고정한 것으로 바꿔 끼운다."""
    return _rng


def _summary(course: CourseRow, present: int) -> dict:
    return {"courseId": course.id, "name": course.name, "type": course.type, "lengthM": course.length_m,
            "runnersNow": present, "thumbnailUrl": course.thumbnail_url}


def _active_course(db: Session, course_id: str, lock: bool = False) -> CourseRow:
    q = select(CourseRow).where(CourseRow.id == course_id, CourseRow.active)
    # FOR NO KEY UPDATE: 입장끼리만 줄 세움(이 코스를 가리키는 runs·course_entries 삽입의 외래 키 확인은 막지 않음)
    course = db.scalar(q.with_for_update(key_share=True) if lock else q)
    if course is None:
        raise ApiError(E.COURSE_NOT_FOUND)
    return course


@router.get("/courses", summary="코스 목록",
            description="화면 C-01 · BL-7, BL-43. 짧은 코스부터. runnersNow는 그 맵에 지금 있는 사람 수(나 빼고)"
            "(실시간 방에 붙어 있는 사람 + 입장 2분 안에 아직 안 붙은 사람). 실사 영상 코스는 최하위라 type만 열어 둠",
            response_model=Envelope[list[Course]], responses=AUTH)
def list_courses(request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    courses = db.scalars(select(CourseRow).where(CourseRow.active).order_by(CourseRow.length_m, CourseRow.id)).all()
    present = count_present(db, [c.id for c in courses], exclude_user_id=user.id)  # 보는 사람은 빼고(get_course 참고)
    return ok(request, [_summary(c, present[c.id]) for c in courses])


@router.get("/courses/{courseId}", summary="코스 상세", description="화면 C-02 · BL-7. 숨긴 코스도 404",
            response_model=Envelope[CourseDetail], responses={**AUTH, **error_responses(E.COURSE_NOT_FOUND)})
def get_course(courseId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    course = _active_course(db, courseId)
    # 보는 사람은 빼고 센다: 목록·상세를 보는 동안엔 맵 밖이라 "같이 달릴 사람"이 아님. 맵에서 막 나왔을 때
    # 앱의 leave보다 이 요청이 먼저 닿아도 나를 1명으로 세지 않는다(입장 검사도 나를 빼고 셈)
    return ok(request, {**_summary(course, count_present(db, [course.id], exclude_user_id=user.id)[course.id]),
                        "description": course.description, "mood": course.mood,
                        "maxRunnersPerMap": course.max_runners_per_map})


@router.post(
    "/courses/{courseId}/entries",
    status_code=201,
    summary="맵 입장",
    description="화면 C-02, D-01, I-10, I-11 · BL-7, BL-21. 판정 순서: 코스 없음 → 보정 없음(CALIBRATION_REQUIRED) → "
    "나 말고 그 맵에 있는 사람이 maxRunnersPerMap 이상(COURSE_FULL). 통과하면 자리를 잡는다(다른 코스에 있던 자리는 옮겨짐). "
    "코스를 잠그고 세므로 동시에 들어와도 최대 인원을 넘지 않는다. 코스 하나 = 맵 하나(mapInstanceId = map_{courseId}). "
    "realtime은 그 맵의 실시간 방 접속 주소·토큰(6시간, docs/api/realtime.md). "
    "입장 위치는 그 맵에 달리는 사람이 있으면 한 명의 20–40m 뒤, 없으면 코스 위 랜덤",
    response_model=Envelope[MapEntry],
    responses={**AUTH, **error_responses(E.COURSE_NOT_FOUND, E.COURSE_FULL, E.CALIBRATION_REQUIRED)},
)
def enter_map(courseId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db),
              rng: random.Random = Depends(get_rng)):
    course = _active_course(db, courseId, lock=True)  # 같은 코스 입장은 여기서 한 줄로 선다(커밋까지)
    if not db.scalar(select(exists().where(Calibration.user_id == user.id))):
        raise ApiError(E.CALIBRATION_REQUIRED)
    if count_present(db, [course.id], exclude_user_id=user.id)[course.id] >= course.max_runners_per_map:
        raise ApiError(E.COURSE_FULL)
    db.execute(insert(CourseEntry).values(user_id=user.id, course_id=course.id)
               .on_conflict_do_update(index_elements=[CourseEntry.user_id],
                                      set_={"course_id": course.id, "entered_at": func.now()}))
    db.commit()
    map_id = map_id_of(course.id)
    return ok(request, {"mapInstanceId": map_id, "courseId": course.id,
                        "entryPositionM": _entry_position(course, user.id, rng),
                        "realtime": {"url": _realtime_url(request), "token": tokens.issue_realtime(user.id, map_id)}})


def _entry_position(course: CourseRow, user_id: str, rng: random.Random) -> float:
    runners = hub.running_positions(course.id, except_user=user_id)
    if not runners:
        return round(rng.uniform(0, course.length_m), 1)
    ahead = rng.choice(sorted(runners))  # 정렬: 같은 시드면 같은 결과(예시 만들기)
    return round((ahead - rng.uniform(*BEHIND_M)) % course.length_m, 1)


def _realtime_url(request: Request) -> str:
    """이 API를 부른 주소 그대로(Caddy 뒤에선 프록시 헤더로 https → wss)."""
    base = str(request.base_url).rstrip("/")
    scheme, rest = base.split("://", 1)
    return f"{'wss' if scheme == 'https' else 'ws'}://{rest}/v1/realtime"
