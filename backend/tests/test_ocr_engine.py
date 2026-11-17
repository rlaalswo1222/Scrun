"""계기판 OCR 진짜 엔진(계획 0043): 브래킷 합치기, 자식 프로세스 시간 초과·죽음, 실제 모델로 합성 계기판 한 장."""
import importlib.util
import time
from pathlib import Path

import pytest

from app.console_ocr.pool import PoolBusy, ReaderPool, ReadTimeout, WorkerFailed
from app.core.ocr import UNREADABLE, BadPhoto, ConsoleRead, OnnxOcr, Photo, combine, get_ocr
from app.main import app
from tests.helpers import data, fail
from tests.ocr_workers import crashes_on_start, dies, slow
from tests.test_runs import db, me  # noqa: F401  (fixture)
from tests.test_verification import fake_ocr, finished, fresh_ocr, photo  # noqa: F401  (fixture)

R = ("READ", 3120, 1720)


# ---------- 브래킷 합치기 ----------
@pytest.mark.parametrize("results,expected", [
    ([R, R, R], ConsoleRead.read(3120, 1720)),
    ([R, ("UNREADABLE", None, None), None], ConsoleRead.read(3120, 1720)),   # 읽힌 것만 같으면
    ([R, ("READ", 3120, 1721), R], UNREADABLE),                              # 하나라도 다르면 다시 찍기
    ([("UNREADABLE", None, 1720)] * 3, UNREADABLE),
])
def test_combine_reads_only_when_all_read_photos_agree(results, expected):
    assert combine(results) == expected


def test_combine_all_unopenable_is_bad_photo():
    with pytest.raises(BadPhoto):
        combine([None, None, None])


# ---------- 자식 프로세스 ----------
def wait_idle(pool, n=1, seconds=60):
    end = time.monotonic() + seconds
    while pool.idle.qsize() < n:
        assert time.monotonic() < end, "일꾼이 안 뜸"
        time.sleep(0.05)


def test_timeout_kills_the_process_and_starts_a_new_one():
    pool = ReaderPool(workers=1, target=slow, args=(5.0,))
    try:
        wait_idle(pool)
        first = next(iter(pool.all))
        with pytest.raises(ReadTimeout):
            pool.read([b"x"], timeout_s=0.3)
        assert not first.proc.is_alive()          # 멈춘 작업을 죽임(스레드는 못 죽임)
        wait_idle(pool)                            # 새로 뜸
        assert first not in pool.all
    finally:
        pool.close()


def test_dead_process_is_replaced():
    pool = ReaderPool(workers=1, target=dies, args=())
    try:
        wait_idle(pool)
        with pytest.raises(WorkerFailed):
            pool.read([b"x"], timeout_s=5)
        wait_idle(pool)
        assert len(pool.all) == 1
    finally:
        pool.close()


def test_worker_that_dies_while_starting_is_noticed_at_once(monkeypatch):
    monkeypatch.setattr(ReaderPool, "RESTART_DELAY_S", 60.0)   # 다시 띄우기는 이 테스트 밖
    pool = ReaderPool(workers=1, target=crashes_on_start, args=())
    try:
        end = time.monotonic() + 20                # START_TIMEOUT_S(180초)까지 기다리지 않음
        while pool.all and time.monotonic() < end:
            time.sleep(0.05)
        assert not pool.all and pool.idle.qsize() == 0
    finally:
        pool.close()


def test_busy_when_no_worker_is_free():
    pool = ReaderPool(workers=0, target=slow, args=(0.0,))
    try:
        with pytest.raises(PoolBusy):
            pool.read([b"x"], timeout_s=0.1)
    finally:
        pool.close()


# ---------- 라우터: 엔진이 못 읽으면 503, 다시 찍기 횟수는 그대로 ----------
class BusyPool:
    def read(self, photos, timeout_s):
        raise PoolBusy()


class AgreeingPool:
    def read(self, photos, timeout_s):
        return [("READ", 3000, 1040)] * len(photos)


def test_engine_unavailable_is_503_and_not_counted(db_client, me, fresh_ocr):  # noqa: F811
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = lambda: OnnxOcr(BusyPool())
    fail(photo(db_client, me, run_id, "a.jpg", "b.jpg"), 503, "SERVICE_UNAVAILABLE")
    app.dependency_overrides[get_ocr] = lambda: OnnxOcr(AgreeingPool())
    r = data(photo(db_client, me, run_id, "a.jpg", "b.jpg", "c.jpg"))
    assert r["status"] == "VERIFIED" and r["console"] == {"distanceM": 3000, "durationS": 1040}


# ---------- 실제 모델 ----------
needs_models = pytest.mark.skipif(importlib.util.find_spec("rapidocr") is None, reason="rapidocr 없음(requirements.txt)")
SAMPLE = Path(__file__).parent / "data" / "console_synth_sample.jpg"   # 합성: 시간 34.49(점), 페이스 11:06, 라벨 한·영


@needs_models
def test_real_engine_reads_a_synthetic_console_in_a_child_process():
    engine = OnnxOcr(ReaderPool(workers=1))
    try:
        wait_idle(engine.pool, seconds=180)
        data_ = SAMPLE.read_bytes()
        assert engine.read([Photo(data_), Photo(data_)]) == ConsoleRead.read(6470, 34 * 60 + 49)
        with pytest.raises(BadPhoto):
            engine.read([Photo(b"\xff\xd8\xff not a jpeg")])
    finally:
        engine.close()
