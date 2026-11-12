from app.testenv import use_test_settings

use_test_settings()  # app보다 먼저(임시 서명 키 + 가짜 카카오)

import pytest
from fastapi.testclient import TestClient

from app.db.testing import TestDatabase
from app.main import app
from app.realtime.hub import hub


@pytest.fixture(scope="session")
def test_db():
    tdb = TestDatabase(app).start()
    yield tdb
    tdb.stop()


@pytest.fixture
def db_client(test_db):
    """진짜 API 테스트용: 테스트 DB를 비우고 시작."""
    test_db.reset()
    hub.reset()  # 실시간 방(메모리)도 비움
    return TestClient(app)
