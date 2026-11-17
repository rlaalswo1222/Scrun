"""계기판 사진 전체 → 거리·시간(계획 0042). 원본은 서버 `backend/app/console_ocr/field.py`(계획 0046)."""
import _backend  # noqa: F401
from app.console_ocr.field import (ConsoleReader, PhotoRead, Region, decide, draw, label_kind, merge_split,  # noqa: F401
                                   pair, parse_km, parse_time)
