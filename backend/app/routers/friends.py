"""친구(계획 0044, BL-27·28, S7 민재). 코드 추가·목록·달리는 친구·프로필·삭제·피드는 DB와 실시간 허브로.
같이 달리기(run-together, BL-29 S8, 계획 0047)는 친구가 달리는 맵에 일반 입장과 같은 판정으로 들어가 친구 뒤에 선다.

- 친구 맺기는 코드를 넣으면 요청·수락 없이 서로 바로(정할 것 2). friendships는 (작은 ID, 큰 ID) 한 줄
- 친구가 아닌 사람의 ID로 부르면 USER_NOT_FOUND(남의 계정이 있는지 알려 주지 않음)
- 친구에게 보이는 기록 = 친구가 공유(shared)한 끝난 기록(PENDING·VERIFIED·UNVERIFIED). 마감 지난 PENDING은 UNVERIFIED로 보임
- 레벨은 BL-32(S8 지환) 전까지 null
"""
import random
from datetime import datetime, timezone
from typing import Optional

from fastapi import APIRouter, Depends, Query, Request, Response
from sqlalchemy import and_, delete, func, or_, select, tuple_
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.orm import Session

from ..core import cursor as cursors
from ..core.attempts import friend_code_failures
from ..core.config import settings
from ..core.envelope import Envelope, error_responses, ok
from ..core.errors import ApiError, ErrorCode
from ..core.friend_code import normalize
from ..core.run_access import PENDING, UNVERIFIED, VERIFIED, is_expired
from ..core.security import current_user
from ..db.models import Course, Friendship, Run, User, UserProgress
from ..db.session import get_db
from ..realtime.hub import RunningNow, hub
from ..schemas.common import CursorPage
from ..schemas.course import MapEntry
from ..schemas.friend import FeedItem, Friend, FriendAddRequest, FriendProfile, RunningFriend, RunTogetherRequest
from .courses import FRIEND_BEHIND_M, enter_course, get_rng

router = APIRouter(prefix="/v1", tags=["Friend"])
E = ErrorCode
AUTH = (E.UNAUTHORIZED, E.TOKEN_EXPIRED, E.SIGNUP_REQUIRED)   # 코드 묶음, 문서는 error_responses 한 번에(dict 합치기는 같은 상태 코드를 덮어씀)
FINISHED = (PENDING, VERIFIED, UNVERIFIED)
FEED_PAGE = 20      # 피드 한 쪽
RECENT_RUNS = 10    # 프로필 최근 러닝


def _pair(a: str, b: str) -> tuple[str, str]:
    return (a, b) if a < b else (b, a)


def _friend_ids(db: Session, me: str) -> list[str]:
    rows = db.execute(select(Friendship.user_a_id, Friendship.user_b_id)
                      .where(or_(Friendship.user_a_id == me, Friendship.user_b_id == me))).all()
    return [b if a == me else a for a, b in rows]


def _count_friends(db: Session, user_id: str) -> int:
    return db.scalar(select(func.count()).select_from(Friendship)
                     .where(or_(Friendship.user_a_id == user_id, Friendship.user_b_id == user_id))) or 0


def _is_friend(db: Session, me: str, other: str) -> bool:
    a, b = _pair(me, other)
    return db.scalar(select(Friendship.user_a_id).where(Friendship.user_a_id == a, Friendship.user_b_id == b)) is not None


def _require_friend(db: Session, me: User, other_id: str) -> None:
    if other_id == me.id or not _is_friend(db, me.id, other_id):
        raise ApiError(E.USER_NOT_FOUND)


def _visible_runs():
    """친구에게 보이는 기록의 조건."""
    return and_(Run.status.in_(FINISHED), Run.shared.is_(True))


def _friends(db: Session, ids: list[str], running: Optional[dict[str, RunningNow]] = None) -> list[dict]:
    """친구 줄(Friend 모양) 여럿. 순서는 부른 쪽이 정함."""
    if not ids:
        return []
    running = hub.running_now(ids) if running is None else running
    names = dict(db.execute(select(User.id, User.nickname).where(User.id.in_(ids))).all())
    # 인증 마크도 공유한 기록만 센다(공유를 끈 기록의 수가 친구에게 새지 않게)
    verified = dict(db.execute(select(Run.user_id, func.count()).where(Run.user_id.in_(ids), Run.status == VERIFIED,
                                                                       Run.shared.is_(True))
                               .group_by(Run.user_id)).all())
    last = dict(db.execute(select(Run.user_id, func.max(Run.ended_at)).where(Run.user_id.in_(ids), _visible_runs())
                           .group_by(Run.user_id)).all())
    out = []
    for uid in ids:
        if uid not in names:  # 탈퇴하면 친구 줄도 같이 지워지지만, 읽는 사이에 지워질 수 있음
            continue
        now = running.get(uid)
        out.append({"userId": uid, "nickname": names[uid], "level": None, "verifiedCount": verified.get(uid, 0),
                    "runningNow": now is not None,
                    "runningDistanceM": round(now.run_m) if now is not None and now.run_m is not None else None,
                    "lastRunAt": last.get(uid)})
    return out


def _run_row(run: Run, course_name: str, at: datetime) -> dict:
    distance, duration = run.official_distance_m or 0, run.official_duration_s or 0
    verification = UNVERIFIED if is_expired(run, at) else run.status  # 마감 지난 대기는 미인증으로(DB는 주인이 읽을 때 닫힘)
    return {"runId": run.id, "startedAt": run.started_at, "endedAt": run.ended_at, "courseId": run.course_id,
            "courseName": course_name, "distanceM": distance, "durationS": duration,
            "avgPaceSPerKm": round(duration * 1000 / distance) if distance > 0 else None,
            "verification": verification}


@router.post(
    "/friends",
    status_code=201,
    summary="친구 코드로 추가",
    description="화면 G-05에서 G-08 · BL-27. 받은 코드(8글자, 대소문자·공백·하이픈 무시)를 넣으면 요청·수락 없이 서로 바로 친구. "
    f"한쪽이라도 {settings.friends.max_friends}명이면 FRIEND_LIMIT_EXCEEDED(details.who: ME 또는 FRIEND). "
    f"없는 코드를 {settings.friends.add_fail_window_s // 60}분 안에 {settings.friends.add_fail_limit}번 넣으면 "
    "그 뒤로는 맞는 코드도 RATE_LIMITED(맞혀 보기 막기). 응답은 새 친구",
    response_model=Envelope[Friend],
    responses=error_responses(*AUTH, E.VALIDATION_ERROR, E.SELF_FRIEND_CODE, E.FRIEND_CODE_NOT_FOUND,
                                         E.ALREADY_FRIEND, E.FRIEND_LIMIT_EXCEEDED, E.RATE_LIMITED),
)
def add_friend(body: FriendAddRequest, request: Request, user: User = Depends(current_user),
               db: Session = Depends(get_db)):
    policy = settings.friends
    # 없는 코드일 것으로 미리 세고(동시에 여러 개가 와도 한도를 못 넘게), 코드를 찾으면 돌려준다
    ticket = friend_code_failures.acquire(user.id, policy.add_fail_limit, policy.add_fail_window_s)
    if ticket is None:
        raise ApiError(E.RATE_LIMITED)
    code = normalize(body.friend_code)
    target = db.scalar(select(User).where(User.friend_code == code)) if code else None
    if target is None:
        raise ApiError(E.FRIEND_CODE_NOT_FOUND)
    friend_code_failures.release(user.id, ticket)
    if target.id == user.id:
        raise ApiError(E.SELF_FRIEND_CODE)
    # 두 사람을 ID 순서로 잠그고 센다: 동시에 여러 명이 추가해도 50명을 넘지 않게(순서가 같아 서로 기다리다 멈추지 않음).
    # FOR NO KEY UPDATE: 친구 추가끼리만 줄 서고, 그 사람을 가리키는 다른 줄 넣기(러닝 시작·토큰 등 외래 키)는 막지 않음
    db.execute(select(User.id).where(User.id.in_(_pair(user.id, target.id))).order_by(User.id)
               .with_for_update(key_share=True))
    if _is_friend(db, user.id, target.id):
        raise ApiError(E.ALREADY_FRIEND)
    for who, uid in (("ME", user.id), ("FRIEND", target.id)):
        if _count_friends(db, uid) >= policy.max_friends:
            raise ApiError(E.FRIEND_LIMIT_EXCEEDED, details={"who": who})
    a, b = _pair(user.id, target.id)
    db.execute(insert(Friendship).values(user_a_id=a, user_b_id=b).on_conflict_do_nothing())
    db.commit()
    return ok(request, _friends(db, [target.id])[0])


@router.get("/friends", summary="친구 목록",
            description="화면 G-04, G-10 · BL-27. 달리는 친구 먼저, 그다음 최근에 달린 순, 닉네임 순. 없으면 빈 배열(G-04 빈 상태). "
            "'친구 N/50명'의 50은 정해진 값(BL-27 ⑤)",
            response_model=Envelope[list[Friend]], responses=error_responses(*AUTH))
def list_friends(request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    rows = _friends(db, _friend_ids(db, user.id))
    oldest = datetime.min.replace(tzinfo=timezone.utc)
    rows.sort(key=lambda f: f["nickname"])
    rows.sort(key=lambda f: (f["runningNow"], f["lastRunAt"] or oldest), reverse=True)
    return ok(request, rows)


@router.get("/friends/running", summary="지금 달리는 친구",
            description="화면 G-01, G-02, B-01 · BL-28, BL-31. 지금 섬에서 달리는 친구(실시간 방 기준, 와이파이가 잠깐 끊긴 사람 30초 포함). "
            "이번 러닝 거리가 긴 순. 없으면 빈 배열(G-02)",
            response_model=Envelope[list[RunningFriend]], responses=error_responses(*AUTH))
def running_friends(request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    return ok(request, running_rows(db, user))


def running_rows(db: Session, user: User) -> list[dict]:
    """지금 달리는 친구(G-01·B-01). 홈(GET /home)도 같이 쓴다."""
    running = hub.running_now(_friend_ids(db, user.id))
    if not running:
        return []
    names = dict(db.execute(select(User.id, User.nickname).where(User.id.in_(running))).all())
    courses = dict(db.execute(select(Course.id, Course.name)
                              .where(Course.id.in_({r.course_id for r in running.values()}))).all())
    out = [{"userId": uid, "nickname": names[uid], "courseId": r.course_id, "courseName": courses.get(r.course_id, ""),
            "distanceM": round(r.run_m or 0), "paceSPerKm": round(3600 / r.kmh) if r.kmh >= 1 else None}
           for uid, r in running.items() if uid in names]
    out.sort(key=lambda f: (-f["distanceM"], f["nickname"]))
    return out


@router.get("/friends/{userId}", summary="친구 프로필",
            description=f"화면 G-09 · BL-27. 친구 줄 + 옷 단계 + 최근 공유 러닝 {RECENT_RUNS}개. 친구가 아니면 USER_NOT_FOUND",
            response_model=Envelope[FriendProfile], responses=error_responses(*AUTH, E.USER_NOT_FOUND))
def friend_profile(userId: str, request: Request, user: User = Depends(current_user), db: Session = Depends(get_db)):
    _require_friend(db, user, userId)
    rows = _friends(db, [userId])
    if not rows:
        raise ApiError(E.USER_NOT_FOUND)
    stage = db.scalar(select(UserProgress.outfit_stage).where(UserProgress.user_id == userId)) or 1
    at = datetime.now(timezone.utc)
    runs = db.execute(select(Run, Course.name).join(Course, Course.id == Run.course_id)
                      .where(Run.user_id == userId, _visible_runs())
                      .order_by(Run.ended_at.desc(), Run.id.desc()).limit(RECENT_RUNS)).all()
    return ok(request, {**rows[0], "outfitStage": stage, "recentRuns": [_run_row(r, name, at) for r, name in runs]})


@router.delete("/friends/{userId}", status_code=204, summary="친구 삭제",
               description="화면 G-09, G-10, G-11 · BL-27. 양쪽 모두에서 지워짐(상대에게 알리지 않음). 다시 맺으려면 코드가 필요. "
               "친구가 아니면 USER_NOT_FOUND",
               responses=error_responses(*AUTH, E.USER_NOT_FOUND))
def delete_friend(userId: str, user: User = Depends(current_user), db: Session = Depends(get_db)):
    if userId == user.id:
        raise ApiError(E.USER_NOT_FOUND)
    a, b = _pair(user.id, userId)
    gone = db.execute(delete(Friendship).where(Friendship.user_a_id == a, Friendship.user_b_id == b)).rowcount
    db.commit()
    if not gone:
        raise ApiError(E.USER_NOT_FOUND)
    return Response(status_code=204)


@router.post(
    "/friends/{userId}/run-together",
    status_code=201,
    summary="같이 달리기",
    description="화면 G-03, D-20, D-21 · BL-29, 계획 0047. 친구가 courseId에서 달리는 중이면 그 맵에 입장하고 친구 뒤 "
    f"{FRIEND_BEHIND_M[0]:.0f}–{FRIEND_BEHIND_M[1]:.0f}m에 선다(entryPositionM, nearFriend). 응답은 맵 입장과 같은 모양이고 판정도 같다"
    "(보정 없음 CALIBRATION_REQUIRED, 가득 참 COURSE_FULL, 다른 코스에 있던 자리는 옮겨짐). 친구가 아니면 USER_NOT_FOUND, "
    "섬을 나갔거나 그 코스를 떠났으면 FRIEND_NOT_RUNNING(앱은 D-21 안내 뒤 그 코스로 일반 입장, 잠깐 멈춘 친구는 같이). "
    "코스가 숨겨졌으면 COURSE_NOT_FOUND. "
    "섬을 불러오고 3·2·1을 하는 동안 친구가 앞서가므로 앱은 달리기를 시작할 때 실시간 방의 친구 위치로 다시 맞춘다",
    response_model=Envelope[MapEntry],
    responses=error_responses(*AUTH, E.VALIDATION_ERROR, E.USER_NOT_FOUND, E.COURSE_NOT_FOUND, E.FRIEND_NOT_RUNNING,
                              E.COURSE_FULL, E.CALIBRATION_REQUIRED),
)
def run_together(userId: str, body: RunTogetherRequest, request: Request, user: User = Depends(current_user),
                 db: Session = Depends(get_db), rng: random.Random = Depends(get_rng)):
    _require_friend(db, user, userId)
    where = hub.in_map(userId)   # 잠깐 멈춘 친구도(물 마시기), 맵에 있으면 같이
    if where is None or where[0] != body.course_id:
        raise ApiError(E.FRIEND_NOT_RUNNING)
    return ok(request, enter_course(db, request, user, body.course_id, rng, near_friend=(userId, where[1])))


@router.get("/feed", summary="친구 러닝 피드",
            description=f"화면 G-01 · BL-28. 친구들이 공유한 끝난 러닝, 끝난 시각 최신순, 커서 방식(한 쪽 {FEED_PAGE}개). "
            "미인증 기록도 보임(앱이 '미인증' 마크), 인증 대기는 마크 없음. 친구에게만 공개(내 기록은 안 나옴). 친구가 없으면 items=[]",
            response_model=Envelope[CursorPage[FeedItem]], responses=error_responses(*AUTH, E.VALIDATION_ERROR))
def feed(request: Request, cursor: Optional[str] = Query(default=None, description="앞 쪽 응답의 nextCursor"),
         user: User = Depends(current_user), db: Session = Depends(get_db)):
    ids = _friend_ids(db, user.id)
    after = cursors.decode(cursor) if cursor is not None else None
    if not ids:
        return ok(request, {"items": [], "nextCursor": None})
    q = (select(Run, Course.name, User.nickname).join(Course, Course.id == Run.course_id).join(User, User.id == Run.user_id)
         .where(Run.user_id.in_(ids), _visible_runs()))
    if after is not None:
        q = q.where(tuple_(Run.ended_at, Run.id) < tuple_(*after))
    rows = db.execute(q.order_by(Run.ended_at.desc(), Run.id.desc()).limit(FEED_PAGE + 1)).all()
    page = rows[:FEED_PAGE]
    at = datetime.now(timezone.utc)
    items = [{**_run_row(run, course, at), "feedId": run.id, "userId": run.user_id, "nickname": nickname}
             for run, course, nickname in page]
    next_cursor = cursors.encode(page[-1][0].ended_at, page[-1][0].id) if len(rows) > FEED_PAGE else None
    return ok(request, {"items": items, "nextCursor": next_cursor})
