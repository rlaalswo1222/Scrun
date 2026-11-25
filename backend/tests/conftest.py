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


@pytest.fixture
def remote_link(monkeypatch):
    """일반 API가 허브를 같은 프로세스가 아니라 내부 API(HTTP)로 부르게 바꿔 끼움(계획 0073, app/realtime/link.py).
    내부 API는 같은 앱(테스트 클라이언트)에 붙어 있어, 실시간 프로세스를 따로 띄운 것과 같은 길을 지난다."""
    import importlib

    from app.core.config import settings
    from app.realtime.link import RemoteHub, internal_token
    remote = RemoteHub("http://testserver", internal_token(settings.jwt_secret), client=TestClient(app))
    for name in ("app.routers.courses", "app.routers.friends", "app.routers.runs", "app.core.accounts", "app.core.occupancy"):
        monkeypatch.setattr(importlib.import_module(name), "hub_link", remote)
    return remote
