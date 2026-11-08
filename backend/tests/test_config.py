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
