"""환경 변수 설정. 값은 backend/.env(git 제외) 또는 실제 환경 변수에서. 예시는 .env.example."""
import os
from dataclasses import dataclass
from pathlib import Path

_ENV_FILE = Path(__file__).resolve().parents[2] / ".env"


def _load_env_file() -> None:
    """간단한 .env 읽기(이미 설정된 환경 변수가 우선)."""
    if not _ENV_FILE.exists():
        return
    for line in _ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, value = line.split("=", 1)
            os.environ.setdefault(key.strip(), value.strip())


_load_env_file()


@dataclass(frozen=True)
class Settings:
    database_url: str = os.environ.get(
        "DATABASE_URL", "postgresql+psycopg://scrun:scrun-local-only@localhost:5432/scrun")
    jwt_secret: str = os.environ.get("JWT_SECRET", "local-dev-secret-change-me-0123456789abcdef")
    kakao_mode: str = os.environ.get("KAKAO_MODE", "fake")
    access_token_ttl_s: int = 3600
    signup_token_ttl_s: int = 1800
    refresh_token_ttl_s: int = 30 * 24 * 3600


settings = Settings()
