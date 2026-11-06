import pytest
from fastapi.testclient import TestClient

from app.db.testing import TestDatabase
from app.main import app


@pytest.fixture(scope="session")
def test_db():
    tdb = TestDatabase(app).start()
    yield tdb
    tdb.stop()


@pytest.fixture
def db_client(test_db):
    """진짜 API 테스트용: 테스트 DB를 비우고 시작."""
    test_db.reset()
    return TestClient(app)
