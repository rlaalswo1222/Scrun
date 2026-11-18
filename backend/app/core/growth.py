"""성장(계획 0048, BL-32·34·30): 기록이 정해질 때 경험치·레벨·러닝복 단계·인증 횟수·주간 목표를 한 번만 반영한다.

- 경험치 = 인증 완료 기록의 공식 거리(계기판 값) 누적, 줄지 않음. 미인증은 경험치·횟수에 안 씀(BL-32 ④)
- 레벨 = 넘은 임계값 수 + 1(config.GrowthPolicy.level_m), 러닝복 단계 = min(레벨, 6)(BL-34)
- 정해지는 모든 곳(사진·확인·건너뛰기·마감으로 닫힘)이 같은 트랜잭션에서 settle()을 부른다. run_rewards 한 줄이
  "이미 반영함" 표시라 같은 기록은 두 번 안 더함. user_progress는 사용자마다 잠가서 동시 판정에도 전후 값이 맞음
- 주간 목표: 목표를 설정한 날(anchor_date)부터 7일 주기, 사용자 시간대 날짜 기준, 인증 완료 + 5분 이상만 1회(BL-30)
- 임팩트(BL-33·35)는 11/24 이후: 해금 목록이 아직 없어 해금 계산만 자리를 둠(impacts 표가 비면 늘 빈 목록)
"""
from dataclasses import dataclass
from datetime import date, datetime, timedelta, timezone
from typing import Optional
from zoneinfo import ZoneInfo

from sqlalchemy import and_, func, select
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.orm import Session

from ..db.models import Impact, Run, RunReward, User, UserImpact, UserProgress, WeeklyGoal, WeeklyGoalCycle
from .config import settings
from .run_access import UNVERIFIED, VERIFIED

POLICY = settings.growth
DECIDED = (VERIFIED, UNVERIFIED)


# ---------- 레벨 ----------
def level_of(xp_m: int) -> int:
    return 1 + sum(1 for t in POLICY.level_m if xp_m >= t)


def outfit_of(level: int) -> int:
    return min(level, POLICY.outfit_stages)


def next_level_at(xp_m: int) -> Optional[int]:
    """다음 레벨 경험치. 마지막 임계값을 넘었으면 None(정할 것 1 가: Lv.6 뒤는 아직 없음)."""
    return next((t for t in POLICY.level_m if xp_m < t), None)


def remaining_m(xp_m: int) -> Optional[int]:
    at = next_level_at(xp_m)
    return None if at is None else at - xp_m


# ---------- 반영 ----------
def counts_for_week(run: Run) -> bool:
    return run.status == VERIFIED and (run.official_duration_s or 0) >= POLICY.weekly_min_s


def locked_progress(db: Session, user_id: str) -> UserProgress:
    db.execute(insert(UserProgress).values(user_id=user_id).on_conflict_do_nothing(index_elements=[UserProgress.user_id]))
    return db.scalar(select(UserProgress).where(UserProgress.user_id == user_id).with_for_update().execution_options(
        populate_existing=True))


def _unlock_impacts(db: Session, user_id: str, verified_count: int) -> list[str]:
    """인증 횟수가 해금 횟수에 닿은 임팩트(BL-32 ③). 목록이 정해지기 전(11/24 이후)엔 impacts 표가 비어 늘 빈 목록."""
    have = set(db.scalars(select(UserImpact.impact_id).where(UserImpact.user_id == user_id)))
    new = [i for i in db.scalars(select(Impact.id).where(Impact.unlock_at_count <= verified_count).order_by(Impact.sort, Impact.id))
           if i not in have]
    for impact_id in new:
        db.add(UserImpact(user_id=user_id, impact_id=impact_id))
    return new


def settle(db: Session, run: Run) -> Optional[RunReward]:
    """정해진 기록(VERIFIED·UNVERIFIED)을 반영한다. 커밋은 부른 쪽(판정과 같은 트랜잭션). 이미 반영했으면 그 줄.
    마감으로 닫혀 미인증으로 반영됐다가(아무것도 안 더함) 유예 안의 사진·확인으로 인증 완료가 되면 그 줄을 지우고 다시.
    잠금은 인증 완료일 때만(경험치를 더할 때). 미인증은 지금 값을 읽어 전후 같은 줄만 씀."""
    if run.status not in DECIDED:
        return None
    db.flush()  # 방금 정한 판정·공식 값을 아래 집계(이번 주기 횟수)가 보게(세션이 자동으로 안 내보냄)
    done = db.get(RunReward, run.id)
    if done is not None:
        if not (done.status == UNVERIFIED and run.status == VERIFIED):
            return done
        db.delete(done)
        db.flush()
    unlocked: list[str] = []
    if run.status == VERIFIED:
        p = locked_progress(db, run.user_id)
        xp_before = p.xp_m
        p.xp_m += run.official_distance_m or 0
        p.verified_count += 1
        p.level = level_of(p.xp_m)
        p.outfit_stage = outfit_of(p.level)
        unlocked = _unlock_impacts(db, run.user_id, p.verified_count)
        xp_after = p.xp_m
    else:
        p = db.get(UserProgress, run.user_id)
        xp_before = xp_after = p.xp_m if p is not None else 0
    counted = counts_for_week(run)
    user = db.get(User, run.user_id)
    cycle = cycle_at(db, user, local_date(run.ended_at, user)) if run.ended_at is not None else None
    reward = RunReward(run_id=run.id, status=run.status, xp_before_m=xp_before, xp_after_m=xp_after,
                       level_before=level_of(xp_before), level_after=level_of(xp_after),
                       outfit_stage_after=outfit_of(level_of(xp_after)), impacts_unlocked=unlocked, weekly_counted=counted,
                       weekly_done_count=counted_in(db, user, cycle, until=run.ended_at) if cycle else 0,
                       weekly_target_count=cycle.target if cycle else None)
    db.add(reward)
    db.flush()
    return reward


def settle_ids(db: Session, run_ids) -> None:
    """마감으로 한꺼번에 닫은 기록들(run_access.close_expired)."""
    for run in db.scalars(select(Run).where(Run.id.in_(list(run_ids))).order_by(Run.ended_at, Run.id)):
        settle(db, run)


# ---------- 주간 목표 ----------
@dataclass(frozen=True)
class Cycle:
    start: date
    target: int

    @property
    def end(self) -> date:
        return self.start + timedelta(days=POLICY.cycle_days)


def _tz(user: User) -> ZoneInfo:
    return ZoneInfo(user.timezone or "Asia/Seoul")


def local_date(at: datetime, user: User) -> date:
    return at.astimezone(_tz(user)).date()


def cycle_at(db: Session, user: User, day: date) -> Optional[Cycle]:
    """그날이 든 주간 목표 주기(설정한 날부터 7일 단위). 목표가 없거나 설정 전이면 None.
    그 주기의 목표는 주기별 기록(weekly_goal_cycles)이 있으면 그것(목표를 바꾸면 다음 주기부터, BL-30 ⑥), 없으면 지금 목표."""
    goal = db.get(WeeklyGoal, user.id)
    if goal is None or day < goal.anchor_date:
        return None
    start = goal.anchor_date + timedelta(days=(day - goal.anchor_date).days // POLICY.cycle_days * POLICY.cycle_days)
    row = db.get(WeeklyGoalCycle, (user.id, start))
    return Cycle(start=start, target=row.target_count if row is not None else goal.target_count)


def counted_in(db: Session, user: User, cycle: Cycle, until: Optional[datetime] = None) -> int:
    """그 주기에 주간 목표 1회로 인정된 기록 수(끝난 날짜가 사용자 시간대로 주기 안). until이 있으면 그때까지 끝난 것만."""
    tz = _tz(user)
    lo = datetime.combine(cycle.start, datetime.min.time(), tz).astimezone(timezone.utc)
    hi = datetime.combine(cycle.end, datetime.min.time(), tz).astimezone(timezone.utc)
    cond = [Run.user_id == user.id, Run.status == VERIFIED, Run.official_duration_s >= POLICY.weekly_min_s,
            Run.ended_at >= lo, Run.ended_at < hi]
    if until is not None:
        cond.append(Run.ended_at <= until)
    return db.scalar(select(func.count()).select_from(Run).where(and_(*cond))) or 0


# ---------- 응답 ----------
def _impact_rows(db: Session, user_id: str, only: Optional[list[str]] = None) -> list[dict]:
    q = select(Impact).order_by(Impact.sort, Impact.id)
    if only is not None:
        if not only:
            return []
        q = q.where(Impact.id.in_(only))
    have = set(db.scalars(select(UserImpact.impact_id).where(UserImpact.user_id == user_id)))
    return [{"impactId": i.id, "name": i.name, "unlockAtCount": i.unlock_at_count, "unlocked": i.id in have}
            for i in db.scalars(q)]


def rewards_payload(db: Session, run: Run, reward: RunReward) -> dict:
    """결과 화면(E-11) 연출용 반영 결과. 모두 반영할 때 저장한 값(다시 열어도 같음)."""
    return {"xpBeforeM": reward.xp_before_m, "xpAfterM": reward.xp_after_m,
            "levelBefore": reward.level_before, "levelAfter": reward.level_after,
            "leveledUp": reward.level_after > reward.level_before, "outfitStageAfter": reward.outfit_stage_after,
            "nextLevelRemainingM": remaining_m(reward.xp_after_m),
            "impactsUnlocked": _impact_rows(db, run.user_id, list(reward.impacts_unlocked or [])),
            "weeklyGoal": {"counted": reward.weekly_counted, "doneCount": reward.weekly_done_count,
                           "targetCount": reward.weekly_target_count}}


def rewards_for(db: Session, run: Run) -> Optional[dict]:
    """정해진 기록의 반영 결과(인증 응답·GET rewards). 반영 줄이 없으면(이 기능 전에 정해진 기록) 지금 반영하고 커밋."""
    if run.status not in DECIDED:
        return None
    reward = db.get(RunReward, run.id)
    if reward is None:
        reward = settle(db, run)
        db.commit()
    return rewards_payload(db, run, reward)


def progress_payload(db: Session, user_id: str) -> dict:
    """레벨·러닝복은 저장한 값이 아니라 경험치로 매번 계산(임계값 설정을 바꿔도 다음 인증을 기다리지 않음)."""
    p = db.get(UserProgress, user_id)
    xp = p.xp_m if p else 0
    level = level_of(xp)
    return {"level": level, "xpM": xp, "nextLevelAtM": next_level_at(xp), "nextLevelRemainingM": remaining_m(xp),
            "outfitStage": outfit_of(level), "verifiedCount": p.verified_count if p else 0,
            "impacts": _impact_rows(db, user_id), "selectedImpactId": p.selected_impact_id if p else None,
            "levelStartsM": [0, *POLICY.level_m]}
