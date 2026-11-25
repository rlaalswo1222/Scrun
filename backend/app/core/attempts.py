"""실패 횟수 제한(친구 코드 맞혀 보기 막기, 계획 0044). 사람마다 최근 window_s 안의 틀린 시도를 DB(friend_code_failures)에 센다.

계획 0073: 일반 API를 프로세스 여러 개로 띄우므로 메모리로 세면 프로세스마다 따로 세어 한도가 N배 느슨해진다 → DB로.
- 확인과 기록을 한 번에 한다(acquire): 같은 사람의 시도는 사람별 잠금(트랜잭션 advisory lock)으로 줄 세워, 동시에 여러 요청이 와도
  limit을 넘지 않는다. 미리 센 줄은 바로 커밋한다(그 뒤 "없는 코드" 오류로 요청 트랜잭션이 되돌려져도 남게)
- 실패가 아니었으면(코드를 찾음) release로 그 줄을 지운다
- 오래된 줄은 그 사람이 다시 시도할 때 지운다. 계정을 지우면 같이 지워짐(외래 키)
"""
import time
from datetime import datetime, timezone
from typing import Callable, Optional

from sqlalchemy import delete, func, select
from sqlalchemy.orm import Session

from ..db.ids import new_id
from ..db.models import FriendCodeFailure

LOCK_NAMESPACE = 7344  # pg_advisory_xact_lock(이 값, hashtext(사용자)) — 다른 잠금과 안 겹치게


class FailureWindow:
    def __init__(self, clock: Callable[[], float] = time.time):
        self._clock = clock   # 초(테스트가 바꿔 끼움)

    def _at(self, seconds: float) -> datetime:
        return datetime.fromtimestamp(seconds, timezone.utc)

    def acquire(self, db: Session, key: str, limit: int, window_s: float) -> Optional[str]:
        """시도 하나를 실패로 미리 센다(커밋함). 이미 limit번이면 None(막음), 아니면 돌려줄 때 쓸 표."""
        now = self._clock()
        db.execute(select(func.pg_advisory_xact_lock(LOCK_NAMESPACE, func.hashtext(key))))
        db.execute(delete(FriendCodeFailure).where(FriendCodeFailure.user_id == key,
                                                   FriendCodeFailure.at <= self._at(now - window_s)))
        n = db.scalar(select(func.count()).select_from(FriendCodeFailure).where(FriendCodeFailure.user_id == key))
        if n >= limit:
            db.commit()   # 지운 옛 줄은 남기고 잠금을 놓음
            return None
        ticket = new_id()
        db.add(FriendCodeFailure(id=ticket, user_id=key, at=self._at(now)))
        db.commit()
        return ticket

    def release(self, db: Session, ticket: str) -> None:
        """실패가 아니었음(코드를 찾음): 미리 센 줄을 지운다."""
        db.execute(delete(FriendCodeFailure).where(FriendCodeFailure.id == ticket))
        db.commit()


friend_code_failures = FailureWindow()
