"""DB 테이블 21개. 설계 문서 docs/api/data_model.md(데이터 모델 v1)와 같게 유지한다.

- 사용자에 딸린 행은 모두 users 삭제 때 같이 지워진다(ON DELETE CASCADE)
- 시각은 timestamptz(UTC), ID는 ULID 문자열(app/db/ids.py)
- 테이블을 바꾸면 `alembic revision --autogenerate`로 변경 기록을 만들고 설계 문서도 같이 고친다
"""
from datetime import date, datetime
from typing import Any, Optional

from sqlalchemy import (
    ARRAY,
    Boolean,
    CheckConstraint,
    Date,
    DateTime,
    ForeignKey,
    Index,
    Integer,
    REAL,
    SmallInteger,
    Text,
    UniqueConstraint,
    func,
    text,
)
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

CASCADE = "CASCADE"


class Base(DeclarativeBase):
    pass


class _Created:
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())


def user_fk() -> Mapped[str]:
    return mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), nullable=False, index=True)


# ---------- 계정 ----------
class User(_Created, Base):
    __tablename__ = "users"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    nickname: Mapped[str] = mapped_column(Text)
    height_cm: Mapped[int] = mapped_column(SmallInteger)
    weight_kg: Mapped[int] = mapped_column(SmallInteger)
    timezone: Mapped[str] = mapped_column(Text, server_default="Asia/Seoul")
    friend_code: Mapped[str] = mapped_column(Text, unique=True)
    feed_share_default: Mapped[bool] = mapped_column(Boolean, server_default=text("true"))
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now())

    __table_args__ = (Index("uq_users_nickname_lower", text("lower(nickname)"), unique=True),)


class AuthIdentity(_Created, Base):
    __tablename__ = "auth_identities"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    user_id: Mapped[str] = user_fk()
    provider: Mapped[str] = mapped_column(Text)  # KAKAO, APPLE, GOOGLE
    provider_user_id: Mapped[str] = mapped_column(Text)

    __table_args__ = (UniqueConstraint("provider", "provider_user_id"),)


class RefreshToken(_Created, Base):
    """로그인 한 번 = family 하나. 재발급하면 같은 family에 새 줄이 생기고 예전 줄은 ROTATED로 폐기."""
    __tablename__ = "refresh_tokens"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    user_id: Mapped[str] = user_fk()
    family_id: Mapped[str] = mapped_column(Text, index=True)  # 처음 로그인 때 만든 줄의 id
    device_id: Mapped[Optional[str]] = mapped_column(Text)  # 앱이 보낸 기기 ID(없을 수 있음)
    token_hash: Mapped[str] = mapped_column(Text, unique=True)
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    revoked_at: Mapped[Optional[datetime]] = mapped_column(DateTime(timezone=True))
    # ROTATED(재발급) · LOGOUT · REUSED(탈취 의심) · REPLACED(같은 기기 재로그인) · LIMIT(로그인 수 상한)
    revoked_reason: Mapped[Optional[str]] = mapped_column(Text)
    replaced_by_id: Mapped[Optional[str]] = mapped_column(Text)  # ROTATED일 때 대신 나간 줄

    __table_args__ = (
        CheckConstraint("revoked_reason IN ('ROTATED', 'LOGOUT', 'REUSED', 'REPLACED', 'LIMIT')",
                        name="ck_refresh_tokens_revoked_reason"),
        CheckConstraint("(revoked_at IS NULL) = (revoked_reason IS NULL)", name="ck_refresh_tokens_revoked_pair"),
    )


class Agreement(_Created, Base):
    __tablename__ = "agreements"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    title: Mapped[str] = mapped_column(Text)
    required: Mapped[bool] = mapped_column(Boolean)
    version: Mapped[str] = mapped_column(Text)
    url: Mapped[str] = mapped_column(Text)
    sort: Mapped[int] = mapped_column(SmallInteger, server_default="0")


class UserAgreement(Base):
    __tablename__ = "user_agreements"
    user_id: Mapped[str] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), primary_key=True)
    agreement_id: Mapped[str] = mapped_column(Text, ForeignKey("agreements.id"), primary_key=True)
    version: Mapped[str] = mapped_column(Text, primary_key=True)
    agreed_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())


# ---------- 보정·목표 ----------
class Calibration(_Created, Base):
    __tablename__ = "calibrations"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    user_id: Mapped[str] = user_fk()
    points: Mapped[Any] = mapped_column(JSONB)
    model: Mapped[Any] = mapped_column(JSONB)  # 폰 SpeedEstimator 계수. OCR 값 칸 없음(독립성 원칙)
    device: Mapped[str] = mapped_column(Text)


class WeeklyGoal(_Created, Base):
    __tablename__ = "weekly_goals"
    user_id: Mapped[str] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), primary_key=True)
    target_count: Mapped[int] = mapped_column(SmallInteger)
    anchor_date: Mapped[date] = mapped_column(Date)
    next_target_count: Mapped[Optional[int]] = mapped_column(SmallInteger)

    __table_args__ = (CheckConstraint("target_count BETWEEN 1 AND 7"),)


class WeeklyGoalCycle(_Created, Base):
    __tablename__ = "weekly_goal_cycles"
    user_id: Mapped[str] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), primary_key=True)
    cycle_start: Mapped[date] = mapped_column(Date, primary_key=True)
    target_count: Mapped[int] = mapped_column(SmallInteger)


# ---------- 코스 ----------
class Course(_Created, Base):
    __tablename__ = "courses"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    name: Mapped[str] = mapped_column(Text)
    description: Mapped[str] = mapped_column(Text, server_default="")
    mood: Mapped[str] = mapped_column(Text, server_default="")
    thumbnail_url: Mapped[Optional[str]] = mapped_column(Text)  # 없으면 null → 앱 기본 그림
    type: Mapped[str] = mapped_column(Text)  # LOOP_3D, VIDEO
    length_m: Mapped[int] = mapped_column(Integer)
    max_runners_per_map: Mapped[int] = mapped_column(SmallInteger, server_default="20")
    active: Mapped[bool] = mapped_column(Boolean, server_default=text("true"))


class CourseEntry(Base):
    """맵 입장 = 자리 잡기(사람당 한 줄, 다른 코스에 들어가면 옮겨짐). 실시간 서버 전까지 인원은 이것 + 달리는 중 기록으로 센다."""
    __tablename__ = "course_entries"
    user_id: Mapped[str] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), primary_key=True)
    course_id: Mapped[str] = mapped_column(Text, ForeignKey("courses.id"))
    entered_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())

    # 인원 세기(core/occupancy): 코스별 최근 입장
    __table_args__ = (Index("ix_course_entries_course_id_entered_at", "course_id", "entered_at"),)


# ---------- 러닝·인증 ----------
class Run(_Created, Base):
    __tablename__ = "runs"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    user_id: Mapped[str] = user_fk()
    client_run_id: Mapped[str] = mapped_column(Text)
    course_id: Mapped[str] = mapped_column(Text, ForeignKey("courses.id"))
    map_instance_id: Mapped[Optional[str]] = mapped_column(Text)
    goal_type: Mapped[str] = mapped_column(Text)  # DISTANCE, TIME, NONE
    goal_value: Mapped[Optional[int]] = mapped_column(Integer)
    status: Mapped[str] = mapped_column(Text, index=True)  # ACTIVE, PENDING, VERIFIED, UNVERIFIED, DISCARDED
    started_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    ended_at: Mapped[Optional[datetime]] = mapped_column(DateTime(timezone=True))
    sensor_distance_m: Mapped[Optional[int]] = mapped_column(Integer)
    sensor_duration_s: Mapped[Optional[int]] = mapped_column(Integer)
    console_distance_m: Mapped[Optional[int]] = mapped_column(Integer)
    console_duration_s: Mapped[Optional[int]] = mapped_column(Integer)
    official_source: Mapped[Optional[str]] = mapped_column(Text)  # CONSOLE, SENSOR
    official_distance_m: Mapped[Optional[int]] = mapped_column(Integer)
    official_duration_s: Mapped[Optional[int]] = mapped_column(Integer)
    unverified_reason: Mapped[Optional[str]] = mapped_column(Text)
    verification_deadline: Mapped[Optional[datetime]] = mapped_column(DateTime(timezone=True))
    shared: Mapped[bool] = mapped_column(Boolean, server_default=text("true"))
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now())

    __table_args__ = (
        UniqueConstraint("user_id", "client_run_id"),
        # 인원 세기(core/occupancy): 코스별 달리는 중·최근 갱신
        Index("ix_runs_course_id_status_updated_at", "course_id", "status", "updated_at"),
    )


class RunSample(Base):
    __tablename__ = "run_samples"
    run_id: Mapped[str] = mapped_column(Text, ForeignKey("runs.id", ondelete=CASCADE), primary_key=True)
    offset_s: Mapped[float] = mapped_column(REAL, primary_key=True)
    interval_s: Mapped[float] = mapped_column(REAL)
    speeds_kmh: Mapped[list[float]] = mapped_column(ARRAY(REAL))


class RunCompanion(Base):
    __tablename__ = "run_companions"
    run_id: Mapped[str] = mapped_column(Text, ForeignKey("runs.id", ondelete=CASCADE), primary_key=True)
    seq: Mapped[int] = mapped_column(SmallInteger, primary_key=True)
    kind: Mapped[str] = mapped_column(Text)  # RUNNER, FRIEND, BOT
    companion_user_id: Mapped[Optional[str]] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), index=True)
    bot_label: Mapped[Optional[str]] = mapped_column(Text)


class Verification(_Created, Base):
    __tablename__ = "verifications"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    run_id: Mapped[str] = mapped_column(Text, ForeignKey("runs.id", ondelete=CASCADE), index=True)
    method: Mapped[str] = mapped_column(Text)  # PHOTO, READING, SKIP
    read_distance_m: Mapped[Optional[int]] = mapped_column(Integer)
    read_duration_s: Mapped[Optional[int]] = mapped_column(Integer)
    status: Mapped[str] = mapped_column(Text)  # VERIFIED, UNVERIFIED, RETAKE_REQUIRED
    reason: Mapped[Optional[str]] = mapped_column(Text)
    comparison: Mapped[Any] = mapped_column(JSONB, server_default=text("'[]'::jsonb"))


class RunGhost(Base):
    __tablename__ = "run_ghosts"
    run_id: Mapped[str] = mapped_column(Text, ForeignKey("runs.id", ondelete=CASCADE), primary_key=True)
    ghost_run_id: Mapped[Optional[str]] = mapped_column(Text, ForeignKey("runs.id", ondelete="SET NULL"))


class RunReward(_Created, Base):
    __tablename__ = "run_rewards"
    run_id: Mapped[str] = mapped_column(Text, ForeignKey("runs.id", ondelete=CASCADE), primary_key=True)
    xp_before_m: Mapped[int] = mapped_column(Integer)
    xp_after_m: Mapped[int] = mapped_column(Integer)
    level_before: Mapped[int] = mapped_column(SmallInteger)
    level_after: Mapped[int] = mapped_column(SmallInteger)
    outfit_stage_after: Mapped[int] = mapped_column(SmallInteger)
    impacts_unlocked: Mapped[list[str]] = mapped_column(ARRAY(Text), server_default=text("'{}'"))
    weekly_counted: Mapped[bool] = mapped_column(Boolean)


# ---------- 보상 ----------
class Impact(_Created, Base):
    __tablename__ = "impacts"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    name: Mapped[str] = mapped_column(Text)
    unlock_at_count: Mapped[int] = mapped_column(SmallInteger)
    sort: Mapped[int] = mapped_column(SmallInteger, server_default="0")


class UserProgress(Base):
    __tablename__ = "user_progress"
    user_id: Mapped[str] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), primary_key=True)
    xp_m: Mapped[int] = mapped_column(Integer, server_default="0")
    level: Mapped[int] = mapped_column(SmallInteger, server_default="1")
    outfit_stage: Mapped[int] = mapped_column(SmallInteger, server_default="1")
    verified_count: Mapped[int] = mapped_column(Integer, server_default="0")
    selected_impact_id: Mapped[Optional[str]] = mapped_column(Text, ForeignKey("impacts.id"))
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now())


class UserImpact(Base):
    __tablename__ = "user_impacts"
    user_id: Mapped[str] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), primary_key=True)
    impact_id: Mapped[str] = mapped_column(Text, ForeignKey("impacts.id"), primary_key=True)
    unlocked_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())


# ---------- 친구·지표 ----------
class Friendship(_Created, Base):
    __tablename__ = "friendships"
    user_a_id: Mapped[str] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), primary_key=True)
    user_b_id: Mapped[str] = mapped_column(Text, ForeignKey("users.id", ondelete=CASCADE), primary_key=True, index=True)

    __table_args__ = (CheckConstraint("user_a_id < user_b_id", name="ck_friendships_order"),)


class Event(Base):
    __tablename__ = "events"
    id: Mapped[str] = mapped_column(Text, primary_key=True)
    user_id: Mapped[str] = user_fk()
    name: Mapped[str] = mapped_column(Text)
    occurred_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    props: Mapped[Any] = mapped_column(JSONB, server_default=text("'{}'::jsonb"))
    received_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())
