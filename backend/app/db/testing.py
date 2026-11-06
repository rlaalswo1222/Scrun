"""테스트·예시 내보내기용: 앱이 테스트 DB(scrun_test)와 가짜 카카오를 쓰게 바꾼다. 개발 DB는 건드리지 않는다.

TEST_DATABASE_URL(없으면 backend/.env, 없으면 로컬 기본값). DB가 안 떠 있으면 `docker compose up -d` 안내와 함께 실패.
"""
import os
from pathlib import Path

from sqlalchemy import create_engine, text
from sqlalchemy.exc import OperationalError
from sqlalchemy.orm import sessionmaker

from ..core.config import settings  # noqa: F401  (.env 읽기)
from ..core.kakao import FakeKakao, get_kakao
from .session import get_db

BACKEND = Path(__file__).resolve().parents[2]
TEST_DATABASE_URL = os.environ.get(
    "TEST_DATABASE_URL", "postgresql+psycopg://scrun:scrun-local-only@localhost:5432/scrun_test")


class TestDatabase:
    __test__ = False  # pytest가 테스트로 모으지 않게

    def __init__(self, app):
        self.app = app
        self.engine = create_engine(TEST_DATABASE_URL, pool_pre_ping=True)
        self.Session = sessionmaker(bind=self.engine, autoflush=False, expire_on_commit=False)

    def start(self) -> "TestDatabase":
        try:
            with self.engine.connect() as conn:
                conn.execute(text("SELECT 1"))
        except OperationalError as e:
            raise RuntimeError("테스트 DB에 연결하지 못했습니다. backend 폴더에서 `docker compose up -d`를 먼저 실행하세요.") from e
        self._migrate()

        def _get_db():
            db = self.Session()
            try:
                yield db
            finally:
                db.close()

        self.app.dependency_overrides[get_db] = _get_db
        self.app.dependency_overrides[get_kakao] = FakeKakao
        return self

    def _migrate(self) -> None:
        from alembic import command
        from alembic.config import Config

        os.environ["ALEMBIC_DATABASE_URL"] = TEST_DATABASE_URL
        try:
            cfg = Config(str(BACKEND / "alembic.ini"))
            cfg.set_main_option("script_location", str(BACKEND / "migrations"))
            command.upgrade(cfg, "head")
        finally:
            os.environ.pop("ALEMBIC_DATABASE_URL", None)

    def reset(self) -> None:
        """사용자를 지우면 딸린 행이 전부 같이 지워진다(CASCADE). 운영 데이터(약관 등)는 남김."""
        with self.engine.begin() as conn:
            conn.execute(text("TRUNCATE users CASCADE"))

    def stop(self) -> None:
        self.app.dependency_overrides.pop(get_db, None)
        self.app.dependency_overrides.pop(get_kakao, None)
        self.engine.dispose()
