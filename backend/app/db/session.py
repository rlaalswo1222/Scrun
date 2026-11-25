"""DB 연결. 라우터는 `db: Session = Depends(get_db)`로 받는다(요청 하나 = 세션 하나)."""
import time
from collections.abc import Iterator

from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker
from sqlalchemy.pool import QueuePool

from ..core.config import settings
from ..core.request_log import add_pool_wait


class _TimedQueuePool(QueuePool):
    """풀에서 연결을 받기까지 기다린 시간을 요청 로그(pool_ms)에 더한다(계획 0070).

    연결은 처음 쿼리할 때 받는다. get_db에서 미리 받으면 모든 요청이 의존성 단계부터 연결을 쥐고 스레드를 기다려서,
    몰릴 때 "연결을 쥔 요청은 스레드를 못 받고, 스레드는 연결을 기다리는" 멈춤이 훨씬 잘 생겼다
    (11/25 부하 시험 S3 100명: 로그인 p50 30초 → 미리 받지 않으면 0.4–0.6초, docs/perf/README.md).
    """

    def _do_get(self):
        t0 = time.perf_counter()
        try:
            return super()._do_get()
        finally:
            add_pool_wait(time.perf_counter() - t0)


# 연결 기다림 상한은 기본 30초 → 몇 초(계획 0071). 번호표(admission)가 연결 수 안으로 막아 평소엔 거의 안 기다림
engine = create_engine(settings.database_url, pool_pre_ping=True, poolclass=_TimedQueuePool,
                       pool_timeout=settings.admission.db_pool_timeout_s)
SessionLocal = sessionmaker(bind=engine, autoflush=False, expire_on_commit=False)


def get_sessionmaker() -> sessionmaker:
    """오래 열린 연결(WebSocket)용: 세션을 연결 내내 붙잡지 않고 필요할 때만 짧게 연다."""
    return SessionLocal


def get_db() -> Iterator[Session]:
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
