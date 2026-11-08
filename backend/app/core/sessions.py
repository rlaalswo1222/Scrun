"""로그인 세션(refresh 토큰) 수명. 계획 0016 "리뷰 후 수정" 4·5·8번.

- 로그인 한 번 = family 하나. 재발급하면 같은 family에 새 줄, 예전 줄은 ROTATED로 폐기(한 번만 씀)
- 재발급은 "아직 살아 있으면 폐기" 조건부 UPDATE 한 번 → 동시에 와도 교체는 딱 한 번
- 이미 교체된 토큰이 다시 오면:
  - 교체된 지 refresh_reuse_grace_s(30초) 안이고, 그때 대신 나간 새 토큰을 아직 안 썼으면 → 응답 유실 재시도로 보고 새 토큰.
    이때 안 쓴 그 토큰은 폐기하고 새 토큰으로 넘김 → family에 살아 있는 토큰은 늘 하나(재시도를 몇 번 해도 갈래가 안 늘어남)
  - 그 밖 → 탈취 의심: 그 family만 REUSED로 폐기(다른 기기 로그인은 유지) + 401
- 로그아웃·같은 기기 재로그인·상한으로 폐기된 토큰이 오면 그냥 401
- 새 로그인: 그 사용자의 만료 줄 삭제 → 같은 기기(deviceId)의 예전 로그인 REPLACED → 살아 있는 로그인이 상한(5)을 넘으면 오래된 것부터 LIMIT.
  사용자 행을 잠가(SELECT ... FOR UPDATE) 같은 사용자의 로그인은 한 번에 하나씩 → 동시에 와도 상한·기기 규칙이 지켜짐
"""
import logging
from datetime import datetime, timedelta, timezone
from typing import Optional

from sqlalchemy import delete, func, select, update
from sqlalchemy.orm import Session

from ..db.ids import new_id
from ..db.models import RefreshToken, User
from . import tokens
from .config import settings
from .errors import ApiError, ErrorCode

ROTATED, LOGOUT, REUSED, REPLACED, LIMIT = "ROTATED", "LOGOUT", "REUSED", "REPLACED", "LIMIT"
log = logging.getLogger("scrun.sessions")


def _now() -> datetime:
    return datetime.now(timezone.utc)


def _active(now: datetime):
    return RefreshToken.revoked_at.is_(None), RefreshToken.expires_at > now


def _issue(db: Session, user_id: str, family_id: Optional[str], device_id: Optional[str], now: datetime) -> tuple[str, dict]:
    """새 refresh 줄 + access 토큰. (새 줄 id, 응답). 호출한 쪽이 commit."""
    raw, token_hash = tokens.new_refresh()
    row_id = new_id()
    db.add(RefreshToken(id=row_id, user_id=user_id, family_id=family_id or row_id, device_id=device_id,
                        token_hash=token_hash, expires_at=now + timedelta(seconds=settings.refresh_token_ttl_s)))
    db.flush()
    return row_id, {"accessToken": tokens.issue_access(user_id), "refreshToken": raw,
                    "expiresInS": settings.access_token_ttl_s}


def _revoke(db: Session, reason: str, now: datetime, *where) -> None:
    db.execute(update(RefreshToken).where(*where, RefreshToken.revoked_at.is_(None))
               .values(revoked_at=now, revoked_reason=reason))


def start(db: Session, user_id: str, device_id: Optional[str]) -> dict:
    """새 로그인(카카오 로그인·가입). 호출한 쪽이 commit."""
    db.execute(select(User.id).where(User.id == user_id).with_for_update())  # 같은 사용자 로그인 직렬화(commit까지)
    now = _now()
    db.execute(delete(RefreshToken).where(RefreshToken.user_id == user_id, RefreshToken.expires_at <= now))
    if device_id is not None:
        _revoke(db, REPLACED, now, RefreshToken.user_id == user_id, RefreshToken.device_id == device_id)
    families = db.execute(
        select(RefreshToken.family_id)
        .where(RefreshToken.user_id == user_id, *_active(now))
        .group_by(RefreshToken.family_id)
        .order_by(func.max(RefreshToken.created_at).desc(), func.max(RefreshToken.id).desc())
    ).scalars().all()
    over = families[settings.max_sessions_per_user - 1:]  # 새 로그인 자리 하나 남김
    if over:
        _revoke(db, LIMIT, now, RefreshToken.family_id.in_(over))
    return _issue(db, user_id, None, device_id, now)[1]


def rotate(db: Session, raw: str) -> dict:
    """토큰 재발급. commit까지 한다(탈취 의심 폐기도 저장해야 하므로)."""
    token_hash = tokens.hash_refresh(raw)
    now = _now()
    claimed = db.execute(
        update(RefreshToken)
        .where(RefreshToken.token_hash == token_hash, *_active(now))
        .values(revoked_at=now, revoked_reason=ROTATED)
        .returning(RefreshToken.id, RefreshToken.user_id, RefreshToken.family_id, RefreshToken.device_id)
    ).one_or_none()
    if claimed is not None:
        new_row_id, issued = _issue(db, claimed.user_id, claimed.family_id, claimed.device_id, now)
        db.execute(update(RefreshToken).where(RefreshToken.id == claimed.id).values(replaced_by_id=new_row_id))
        db.commit()
        return issued

    row = db.scalar(select(RefreshToken).where(RefreshToken.token_hash == token_hash).with_for_update())
    if row is None:
        raise ApiError(ErrorCode.UNAUTHORIZED)
    if row.revoked_at is None:  # 살아 있는데 위에서 못 잡음 = 만료
        raise ApiError(ErrorCode.TOKEN_EXPIRED, "refresh 토큰이 만료되었습니다. 다시 로그인하세요.")
    if row.revoked_reason != ROTATED:
        raise ApiError(ErrorCode.UNAUTHORIZED, "다시 로그인하세요.")

    in_grace = now - row.revoked_at <= timedelta(seconds=settings.refresh_reuse_grace_s)
    if in_grace and row.replaced_by_id is not None:
        # 대신 나간 토큰이 아직 안 쓰였으면(응답 유실) 그걸 폐기하고 새 토큰으로 넘김. 조건부 UPDATE라 동시에 와도 한 번만
        handed_over = db.execute(
            update(RefreshToken)
            .where(RefreshToken.id == row.replaced_by_id, *_active(now))
            .values(revoked_at=now, revoked_reason=ROTATED)
            .returning(RefreshToken.id)
        ).one_or_none()
        if handed_over is not None:
            new_row_id, issued = _issue(db, row.user_id, row.family_id, row.device_id, now)
            db.execute(update(RefreshToken).where(RefreshToken.id.in_((row.id, handed_over.id)))
                       .values(replaced_by_id=new_row_id))
            db.commit()
            return issued

    _revoke(db, REUSED, now, RefreshToken.family_id == row.family_id)
    db.commit()
    log.warning("교체된 refresh 토큰 재사용 → 그 로그인 폐기: user=%s family=%s", row.user_id, row.family_id)
    raise ApiError(ErrorCode.UNAUTHORIZED, "다시 로그인하세요.")


def end(db: Session, user_id: str, raw: str) -> None:
    """로그아웃: 그 토큰의 로그인(family) 전체 폐기. 남의 토큰·이미 폐기된 토큰이면 아무 일 없음. commit까지."""
    family_id = db.scalar(select(RefreshToken.family_id).where(
        RefreshToken.token_hash == tokens.hash_refresh(raw), RefreshToken.user_id == user_id))
    if family_id is not None:
        _revoke(db, LOGOUT, _now(), RefreshToken.family_id == family_id)
    db.commit()


def purge_expired(db: Session) -> int:
    """만료된 줄 전부 삭제(운영 서버에서 하루 한 번, BL-51). 지운 줄 수."""
    n = db.execute(delete(RefreshToken).where(RefreshToken.expires_at <= _now())).rowcount
    db.commit()
    return n
