"""계기판 사진 → 거리·시간 읽기(BL-17, 계획 0038). 판정(core/verification)과 떼어 둔 엔진 자리.

- 사진은 바이트로만 받는다. 엔진은 디스크에 쓰지 않고, 읽은 뒤 라우터가 버린다(BL-17 ③)
- 브래킷 여러 장(노출만 다름, ADR-0011)을 한 번에 받아 엔진이 가장 잘 읽힌 값을 고른다
- OCR_ENGINE: none → 라우터가 503, fake → 파일 이름으로(로컬·테스트), onnx → 진짜 엔진(계획 0043: `app/console_ocr`를
  자식 프로세스 2개에서, 시간을 넘기면 죽이고 새로 띄움)
- 브래킷 합치기: 읽힌 사진의 거리·시간이 모두 같을 때만 READ, 다르면 다시 찍기(11/17 지환). 센서 값은 쓰지 않음
"""
import logging
import re
import threading
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


class EngineUnavailable(Exception):
    """엔진이 지금 못 읽음(일꾼이 다 바쁨·아직 뜨는 중·시간 초과로 죽임). 라우터는 503(다시 찍기 횟수 안 셈)."""


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


def combine(results: list) -> ConsoleRead:
    """사진마다의 결과(None = 못 엶, (상태, 거리, 시간)) → 한 결과. 브래킷은 노출만 다른 같은 화면이라
    읽힌 사진들의 값이 모두 같을 때만 READ(하나라도 다르면 잘못 읽은 것이 섞임 → 다시 찍기). 모두 못 열면 BadPhoto."""
    opened = [r for r in results if r is not None]
    if not opened:
        raise BadPhoto("사진을 열 수 없음")
    values = {(d, t) for status, d, t in opened if status == ReadStatus.READ.value}
    if len(values) == 1:
        d, t = values.pop()
        return ConsoleRead.read(d, t)
    return UNREADABLE


class OnnxOcr:
    """진짜 엔진(계획 0043). 읽기는 `app.console_ocr.pool.ReaderPool`의 자식 프로세스에서."""

    def __init__(self, pool):
        self.pool = pool

    def read(self, photos: list[Photo]) -> ConsoleRead:
        from ..console_ocr.pool import PoolBusy, ReadTimeout

        # 라우터의 시간 제한보다 조금 먼저 끊어 프로세스를 죽인다(라우터 쪽 스레드가 버려지지 않게)
        timeout_s = max(1.0, settings.verification.ocr_timeout_s - 1.0)
        try:
            return combine(self.pool.read([p.data for p in photos], timeout_s))
        except PoolBusy:
            raise EngineUnavailable("계기판 읽기가 밀려 있습니다. 잠시 뒤 다시 보내세요.") from None
        except ReadTimeout:
            raise EngineUnavailable("계기판 읽기가 시간 안에 끝나지 않았습니다.") from None

    def close(self) -> None:
        self.pool.close()


_engine: Optional[OnnxOcr] = None
_engine_lock = threading.Lock()


def start_ocr() -> None:
    """서버가 뜰 때 진짜 엔진의 프로세스를 미리 띄운다(첫 사진이 모델 올리기를 기다리지 않게)."""
    if settings.ocr_engine == "onnx":
        get_ocr()


def stop_ocr() -> None:
    global _engine
    with _engine_lock:
        if _engine is not None:
            _engine.close()
            _engine = None


def get_ocr() -> Optional[OcrEngine]:
    """라우터는 Depends(get_ocr). None이면 엔진이 없음(503). 테스트는 이 함수를 바꿔 끼운다."""
    global _engine
    if settings.ocr_engine == "fake":
        return FakeOcr()
    if settings.ocr_engine == "onnx":
        with _engine_lock:
            if _engine is None:
                from ..console_ocr.pool import ReaderPool  # onnxruntime·RapidOCR은 이 엔진을 쓸 때만 부름
                _engine = OnnxOcr(ReaderPool(workers=2, threads=1))   # 오라클 2코어(ADR-0008)
                logging.getLogger("scrun.ocr").info("계기판 OCR 엔진(onnx) 프로세스 2개 띄우는 중")
            return _engine
    return None
