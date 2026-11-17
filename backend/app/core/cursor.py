"""목록 커서(기록 목록·친구 피드). (시각, ID)를 base64로 싸서 앱에 주고, 다음 쪽은 그보다 작은 줄부터.
같은 시각인 줄이 여러 개여도 ID로 이어져 겹치거나 빠지지 않는다."""
import base64
import binascii
from datetime import datetime

from .errors import invalid


def encode(at: datetime, row_id: str) -> str:
    raw = f"{at.isoformat()}|{row_id}".encode()
    return base64.urlsafe_b64encode(raw).decode().rstrip("=")


def decode(cursor: str) -> tuple[datetime, str]:
    try:
        raw = base64.urlsafe_b64decode(cursor + "=" * (-len(cursor) % 4)).decode()
        at, row_id = raw.split("|", 1)
        when = datetime.fromisoformat(at)
    except (binascii.Error, UnicodeDecodeError, ValueError):
        raise invalid("cursor", "잘못된 커서입니다")
    if when.tzinfo is None or not row_id:
        raise invalid("cursor", "잘못된 커서입니다")
    return when, row_id
