"""DB 연결. 라우터는 `db: Session = Depends(get_db)`로 받는다(요청 하나 = 세션 하나)."""
from collections.abc import Iterator

from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from ..core.config import settings

engine = create_engine(settings.database_url, pool_pre_ping=True)
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
