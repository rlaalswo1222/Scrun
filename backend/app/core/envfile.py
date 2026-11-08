"""backend/.env 읽기와 DB 주소. 검사 없이 값만 읽는다.

마이그레이션(migrations/env.py)은 DB 주소만 필요하므로 이 파일만 쓴다(인증 비밀값 검사를 하는 config.py를 부르지 않음).
"""
import os
from pathlib import Path

ENV_FILE = Path(__file__).resolve().parents[2] / ".env"
DEFAULT_DATABASE_URL = "postgresql+psycopg://scrun:scrun-local-only@localhost:5432/scrun"


def load_env_file() -> None:
    """간단한 .env 읽기(이미 설정된 환경 변수가 우선). 여러 번 불러도 됨."""
    if not ENV_FILE.exists():
        return
    for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, value = line.split("=", 1)
            os.environ.setdefault(key.strip(), value.strip())


def database_url() -> str:
    load_env_file()
    return os.environ.get("DATABASE_URL", DEFAULT_DATABASE_URL)
