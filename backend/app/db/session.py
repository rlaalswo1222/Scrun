"""DB 연결. 라우터는 `db: Session = Depends(get_db)`로 받는다(요청 하나 = 세션 하나)."""
import time
from collections.abc import Iterator

from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from ..core.config import settings
from ..core.request_log import add_pool_wait

engine = create_engine(settings.database_url, pool_pre_ping=True)
SessionLocal = sessionmaker(bind=engine, autoflush=False, expire_on_commit=False)


def get_sessionmaker() -> sessionmaker:
    """오래 열린 연결(WebSocket)용: 세션을 연결 내내 붙잡지 않고 필요할 때만 짧게 연다."""
    return SessionLocal


def get_db() -> Iterator[Session]:
    db = SessionLocal()
    try:
        # 연결을 여기서 미리 받아 풀에서 기다린 시간을 요청 로그(pool_ms)에 남김(계획 0070). 어차피 첫 쿼리 때 받을 연결
        t0 = time.perf_counter()
        db.connection()
        add_pool_wait(time.perf_counter() - t0)
        yield db
    finally:
        db.close()
