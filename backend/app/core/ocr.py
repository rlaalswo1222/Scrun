"""계기판 사진 → 거리·시간 읽기(BL-17, 계획 0038). 판정(core/verification)과 떼어 둔 엔진 자리.

- 사진은 바이트로만 받는다. 엔진은 디스크에 쓰지 않고, 읽은 뒤 라우터가 버린다(BL-17 ③)
- 브래킷 여러 장(노출만 다름, ADR-0011)을 한 번에 받아 엔진이 가장 잘 읽힌 값을 고른다
- 진짜 엔진은 ADR-0012(엔진 고르기) 뒤. 그 전엔 OCR_ENGINE=none → 라우터가 503
"""
import re
from dataclasses import dataclass
from enum import Enum
from typing import Optional, Protocol

from .config import settings


class ReadStatus(str, Enum):
    READ = "READ"                  # 거리·시간을 읽음
    UNREADABLE = "UNREADABLE"      # 이번 사진으로는 못 읽음(흐림·반사·잘림) → 다시 찍기
    UNSUPPORTED = "UNSUPPORTED"    # 이 계기판은 읽을 수 없음(거리 칸이 없음 등) → 다시 찍어도 소용없음


@dataclass(frozen=True)
class Photo:
    data: bytes
    filename: str = ""


@dataclass(frozen=True)
class ConsoleRead:
    status: ReadStatus
    distance_m: Optional[int] = None
    duration_s: Optional[int] = None

    @staticmethod
    def read(distance_m: int, duration_s: int) -> "ConsoleRead":
        return ConsoleRead(ReadStatus.READ, distance_m, duration_s)


UNREADABLE = ConsoleRead(ReadStatus.UNREADABLE)
UNSUPPORTED = ConsoleRead(ReadStatus.UNSUPPORTED)


class BadPhoto(Exception):
    """사진 자체를 열 수 없음(깨진 JPEG 등). 엔진이 이걸 던지면 라우터는 못 읽은 것(다시 찍기, 횟수 셈)으로 본다.
    그 밖의 예외는 엔진 고장으로 보고 503(횟수 안 셈): 둘을 섞으면 깨진 사진은 503만 받아 마감까지 돌거나(#55 리뷰),
    엔진이 고장 났을 때 모두 다시 찍기를 다 써서 미인증으로 굳는다."""


class OcrEngine(Protocol):
    def read(self, photos: list[Photo]) -> ConsoleRead: ...  # 사진을 못 열면 BadPhoto


class FakeOcr:
    """로컬·테스트용. 사진 내용은 보지 않고 파일 이름으로 값을 정한다(앱 에디터의 가짜 카메라가 이름을 붙임).
    `d3120_t1080` → 3120 m·1080초, `unsupported` → 읽을 수 없는 계기판, `corrupt` → 사진을 못 엶(BadPhoto),
    그 밖(`blur` 등) → 다시 찍기. 여러 장이면 처음 읽힌 것."""

    NAME = re.compile(r"d(\d{1,6})_t(\d{1,6})")

    def read(self, photos: list[Photo]) -> ConsoleRead:
        if photos and all("corrupt" in p.filename for p in photos):
            raise BadPhoto("사진을 열 수 없음")
        for p in photos:
            m = self.NAME.search(p.filename)
            if m:
                return ConsoleRead.read(int(m.group(1)), int(m.group(2)))
        if any("unsupported" in p.filename for p in photos):
            return UNSUPPORTED
        return UNREADABLE


def get_ocr() -> Optional[OcrEngine]:
    """라우터는 Depends(get_ocr). None이면 엔진이 없음(503). 테스트는 이 함수를 바꿔 끼운다."""
    if settings.ocr_engine == "fake":
        return FakeOcr()
    return None
