"""설정 검사: 위험한 설정이면 서버가 시작하지 않는다(계획 0016 리뷰 후 수정 1)."""
import pytest

from app.core.config import ConfigError, PUBLIC_JWT_SECRETS, load_settings

GOOD_SECRET = "x" * 48


def test_jwt_secret_required():
    with pytest.raises(ConfigError, match="JWT_SECRET이 없습니다"):
        load_settings({"KAKAO_MODE": "fake"})
    with pytest.raises(ConfigError, match="JWT_SECRET이 없습니다"):
        load_settings({"JWT_SECRET": "   ", "KAKAO_MODE": "fake"})


def test_jwt_secret_public_default_rejected():
    for secret in PUBLIC_JWT_SECRETS:
        with pytest.raises(ConfigError, match="공개된"):
            load_settings({"JWT_SECRET": secret, "KAKAO_MODE": "fake"})


def test_jwt_secret_too_short():
    with pytest.raises(ConfigError, match="32자 이상"):
        load_settings({"JWT_SECRET": "x" * 31, "KAKAO_MODE": "fake"})


def test_kakao_mode_defaults_to_real_and_needs_app_id():
    with pytest.raises(ConfigError, match="KAKAO_APP_ID가 필요"):
        load_settings({"JWT_SECRET": GOOD_SECRET})
    s = load_settings({"JWT_SECRET": GOOD_SECRET, "KAKAO_APP_ID": "1641065"})
    assert s.kakao_mode == "real" and s.kakao_app_id == 1641065


def test_kakao_mode_unknown_rejected():
    for mode in ("Fake ", "fake"):
        assert load_settings({"JWT_SECRET": GOOD_SECRET, "KAKAO_MODE": mode}).kakao_mode == "fake"
    for mode in ("test", "off", "fale"):
        with pytest.raises(ConfigError, match="KAKAO_MODE"):
            load_settings({"JWT_SECRET": GOOD_SECRET, "KAKAO_MODE": mode})


@pytest.mark.parametrize("app_id", ["abc", "12²3", "-1", "1 2"])
def test_kakao_app_id_must_be_number(app_id):
    with pytest.raises(ConfigError, match="숫자"):
        load_settings({"JWT_SECRET": GOOD_SECRET, "KAKAO_MODE": "real", "KAKAO_APP_ID": app_id})


def test_migrations_need_only_database_url():
    """마이그레이션은 DB 주소만 있으면 돈다(JWT_SECRET·카카오 설정 없이)."""
    import os
    import subprocess
    import sys
    from pathlib import Path

    from app.db.testing import TEST_DATABASE_URL

    env = {k: v for k, v in os.environ.items() if k not in ("JWT_SECRET", "KAKAO_MODE", "KAKAO_APP_ID")}
    env["ALEMBIC_DATABASE_URL"] = TEST_DATABASE_URL
    backend = Path(__file__).resolve().parents[1]
    r = subprocess.run([sys.executable, "-m", "alembic", "current"], cwd=backend, env=env,
                       capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60)
    assert r.returncode == 0, r.stderr
    assert "ConfigError" not in r.stderr


def test_bot_policy_defaults_and_overrides():
    from app.core.config import BotPolicy
    s = load_settings({"JWT_SECRET": GOOD_SECRET, "KAKAO_MODE": "fake"})
    assert s.bots == BotPolicy() and s.bots.fill_to == 6 and len(s.bots.paces_kmh) == 5
    s = load_settings({"JWT_SECRET": GOOD_SECRET, "KAKAO_MODE": "fake", "BOT_FILL_TO": "0", "BOT_PACES_KMH": " 8, 9.5 "})
    assert s.bots.fill_to == 0 and s.bots.paces_kmh == (8.0, 9.5)


@pytest.mark.parametrize("env, msg", [
    ({"BOT_FILL_TO": "-1"}, "BOT_FILL_TO"), ({"BOT_FILL_TO": "21"}, "BOT_FILL_TO"), ({"BOT_FILL_TO": "six"}, "BOT_FILL_TO"),
    ({"BOT_PACES_KMH": "9,,10"}, "BOT_PACES_KMH"), ({"BOT_PACES_KMH": "nan"}, "BOT_PACES_KMH"),
    ({"BOT_PACES_KMH": "2.5"}, "BOT_PACES_KMH"), ({"BOT_PACES_KMH": "9,inf"}, "BOT_PACES_KMH"),
])
def test_bot_policy_rejects_bad_values(env, msg):
    with pytest.raises(ConfigError, match=msg):
        load_settings({"JWT_SECRET": GOOD_SECRET, "KAKAO_MODE": "fake", **env})


def test_request_log_switch():
    base = {"JWT_SECRET": GOOD_SECRET, "KAKAO_MODE": "fake"}
    assert load_settings(base).request_log is True
    assert load_settings({**base, "REQUEST_LOG": "0"}).request_log is False
    assert load_settings({**base, "REQUEST_LOG": " 1 "}).request_log is True
    for typo in ("fasle", "O", "off", "true"):   # 오타가 조용히 켜짐·꺼짐이 되지 않게
        with pytest.raises(ConfigError):
            load_settings({**base, "REQUEST_LOG": typo})


def test_admission_settings():
    base = {"JWT_SECRET": GOOD_SECRET, "KAKAO_MODE": "fake"}
    a = load_settings(base).admission
    assert (a.limit, a.wait_s, a.db_pool_timeout_s) == (12, 2.0, 5.0)
    a = load_settings({**base, "ADMISSION_LIMIT": "0", "ADMISSION_WAIT_S": "0.5", "DB_POOL_TIMEOUT_S": "10"}).admission
    assert (a.limit, a.wait_s, a.db_pool_timeout_s) == (0, 0.5, 10.0)
    for name, bad in (("ADMISSION_LIMIT", "-1"), ("ADMISSION_LIMIT", "12.5"), ("ADMISSION_WAIT_S", "0"),
                      ("ADMISSION_WAIT_S", "abc"), ("DB_POOL_TIMEOUT_S", "120"),
                      ("ADMISSION_LIMIT", "16")):   # 연결 합계 15보다 많으면 안 됨
        with pytest.raises(ConfigError):
            load_settings({**base, name: bad})


@pytest.mark.parametrize("compose", ["deploy/docker-compose.server.yml", "perf/docker-compose.perf.yml"])
def test_compose_tickets_and_db_connections_add_up(compose):
    """프로세스를 여럿 띄우면(계획 0073) 번호표·연결 풀이 프로세스마다라 합계가 늘어난다. api의 번호표 합계는 12(0073 측정:
    24면 DB에 너무 몰려 200명 p95가 0.7초), DB 연결 합계는 Postgres 기본 한도 100에서 관리용 여유 10을 뺀 90 안"""
    import re
    from pathlib import Path

    import yaml

    from app.core.config import AdmissionPolicy

    def value(v):   # "${NAME:-6}" → 6
        m = re.fullmatch(r"\$\{\w+:-(\d+)\}", str(v))
        return int(m.group(1) if m else v)

    services = yaml.safe_load((Path(__file__).resolve().parents[1] / compose).read_text(encoding="utf-8"))["services"]
    pool = AdmissionPolicy().db_pool_total
    processes, api_tickets = 0, 0
    for name in ("api", "realtime", "ocr"):
        svc = services[name]
        cmd = svc.get("command") or []
        workers = value(cmd[cmd.index("--workers") + 1]) if "--workers" in cmd else 1
        limit = value(svc["environment"].get("ADMISSION_LIMIT", AdmissionPolicy().limit))
        assert limit <= pool, f"{name}: 번호표 {limit} > 연결 풀 {pool}"
        processes += workers
        if name == "api":
            api_tickets = workers * limit
    assert api_tickets == 12, f"api 번호표 합계 {api_tickets}(프로세스 수 × ADMISSION_LIMIT)는 12로"
    assert processes * pool <= 90, f"DB 연결 합계 {processes * pool} > 90"
