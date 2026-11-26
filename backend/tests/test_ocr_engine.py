"""계기판 OCR 진짜 엔진(계획 0046): 브래킷 합치기, 자식 프로세스 시간 초과·죽음, 실제 모델로 합성 계기판 한 장."""
import importlib.util
import threading
import time
from pathlib import Path

import pytest

from app.console_ocr.pool import EngineDown, PoolBusy, ReaderPool, ReadTimeout, WorkerFailed, read_job
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


def test_request_that_waited_does_not_kill_a_healthy_process():
    # 일꾼 1개가 0.6초 읽기 중 → 다음 요청은 기다리느라 시간을 거의 다 씀 → 이 요청만 끝나고 일꾼은 살아서 돌아옴(#68 리뷰)
    pool = ReaderPool(workers=1, target=slow, args=(0.6,))
    try:
        wait_idle(pool)
        first = next(iter(pool.all))
        busy = threading.Thread(target=pool.read, args=([b"x"], 5.0))
        busy.start()
        time.sleep(0.1)
        with pytest.raises(PoolBusy):              # 사진 탓이 아니라 밀린 것(앱은 "바빠요", 계획 0055 리뷰)
            pool.read([b"y"], timeout_s=1.0)       # 약 0.5초 기다리고 0.5초 남음 → 0.6초 읽기를 못 기다림
        busy.join()
        wait_idle(pool, seconds=5)
        assert first.proc.is_alive() and pool.all == {first}
    finally:
        pool.close()


def test_engine_down_fails_fast(monkeypatch):
    monkeypatch.setattr(ReaderPool, "RESTART_DELAY_S", 60.0)
    monkeypatch.setattr(ReaderPool, "DOWN_AFTER", 1)
    pool = ReaderPool(workers=1, target=crashes_on_start, args=())
    try:
        end = time.monotonic() + 20
        while pool.start_failures < 1 and time.monotonic() < end:
            time.sleep(0.05)
        t0 = time.monotonic()
        with pytest.raises(EngineDown):
            pool.read([b"x"], timeout_s=10)
        assert time.monotonic() - t0 < 1.0         # 10초를 기다리지 않음
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


class TimeoutPool:
    def read(self, photos, timeout_s):
        raise ReadTimeout()


def test_read_timeout_is_503_with_reason_and_not_counted(db_client, me, fresh_ocr):  # noqa: F811
    """시간 초과는 503 + details.reason OCR_TIMEOUT(앱이 "바빠요" 대신 다시 찍기 안내, 계획 0055). 일꾼이 다 찬 503엔 reason 없음."""
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = lambda: OnnxOcr(TimeoutPool())
    r = photo(db_client, me, run_id, "a.jpg")
    fail(r, 503, "SERVICE_UNAVAILABLE")
    assert r.json()["error"]["details"] == {"reason": "OCR_TIMEOUT"}
    app.dependency_overrides[get_ocr] = lambda: OnnxOcr(BusyPool())
    r = photo(db_client, me, run_id, "a.jpg")
    fail(r, 503, "SERVICE_UNAVAILABLE")
    assert not r.json()["error"].get("details")
    app.dependency_overrides[get_ocr] = lambda: OnnxOcr(AgreeingPool())
    assert data(photo(db_client, me, run_id, "a.jpg"))["status"] == "CONFIRM_REQUIRED"   # 횟수 안 깎임


# ---------- 묶음 읽기: 같은 값 두 장이면 멈춤(계획 0055) ----------
class _Img:
    shape = (20, 10, 3)


class _ScriptReader:
    """사진 바이트(b"A" 등)마다 정해 둔 결과를 내고, 읽은 순서를 남김."""
    def __init__(self, script):
        self.script, self.seen = script, []

    def read(self, img):
        self.seen.append(img.key)
        status, d, t = self.script[img.key]
        return type("R", (), {"status": status, "distance_m": d, "duration_s": t})()


def _decode(data):
    if data == b"broken":
        return None
    img = _Img()
    img.key = data
    return img


SCRIPT = {b"A": ("READ", 3000, 1040), b"A2": ("READ", 3000, 1040), b"B": ("READ", 3100, 1040), b"U": ("UNREADABLE", None, None)}


@pytest.mark.parametrize("job, read, result", [
    ([b"A", b"A2", b"U"], [b"A", b"A2"], "READ"),                    # 두 장이 같으면 셋째는 안 읽음
    ([b"A", b"U", b"A2"], [b"A", b"U", b"A2"], "READ"),              # 못 읽은 게 끼면 끝까지
    ([b"A", b"B", b"A2"], [b"A", b"B"], "UNREADABLE"),               # 값이 갈리면 이미 다시 찍기 → 셋째는 안 읽음
    ([b"A", b"U", b"B", b"A2"], [b"A", b"U", b"B"], "UNREADABLE"),
    ([b"U", b"A", b"A2", b"B"], [b"U", b"A", b"A2"], "READ"),        # 넷째가 달랐어도 두 장 일치에서 멈춤(값 확인 E-05b가 받침)
    ([b"U", b"U", b"U"], [b"U", b"U", b"U"], "UNREADABLE"),
    ([b"broken", b"A", b"A2"], [b"A", b"A2"], "READ"),
])
def test_read_job_stops_when_two_photos_agree(job, read, result):
    reader = _ScriptReader(SCRIPT)
    lines = []
    out = read_job(job, _decode, reader, lines.append)
    assert reader.seen == read
    assert combine(out).status.value == result
    assert all("3000" not in ln and "1040" not in ln for ln in lines)   # 로그에 읽은 값 안 남김(BL-17 ③)


def test_engine_unavailable_is_503_and_not_counted(db_client, me, fresh_ocr):  # noqa: F811
    run_id = finished(db_client, me)
    app.dependency_overrides[get_ocr] = lambda: OnnxOcr(BusyPool())
    fail(photo(db_client, me, run_id, "a.jpg", "b.jpg"), 503, "SERVICE_UNAVAILABLE")
    app.dependency_overrides[get_ocr] = lambda: OnnxOcr(AgreeingPool())
    r = data(photo(db_client, me, run_id, "a.jpg", "b.jpg", "c.jpg"))
    assert r["status"] == "CONFIRM_REQUIRED" and r["console"] == {"distanceM": 3000, "durationS": 1040}   # 계획 0045: 판정은 확인 뒤


# ---------- 실제 모델 ----------
needs_models = pytest.mark.skipif(importlib.util.find_spec("rapidocr") is None, reason="rapidocr 없음(requirements.txt)")
SAMPLE = Path(__file__).parent / "data" / "console_synth_sample.jpg"   # 합성: 시간 34.49(점), 페이스 11:06, 라벨 한·영


@needs_models
def test_warm_up_loads_the_recognition_model_too():
    """RapidOCR은 인식 모델을 글자를 처음 찾았을 때 올림(없으면 그때 받음). 빈 그림으로는 안 올라와 11/18 오라클 빌드가
    한국어 인식 모델 없이 끝났음 → 예열은 글자를 그린 그림으로, 빌드(fetch_models)·프로세스 시작(serve)이 이걸 부름."""
    import numpy as np
    from app.console_ocr.field import ConsoleReader
    from app.console_ocr.pool import MODEL
    blank = ConsoleReader(str(MODEL), threads=1)
    blank.read(np.zeros((200, 400, 3), np.uint8))
    assert blank.ocr.text_rec is None   # 옛 fetch_models가 한 것: 인식 모델을 안 건드림
    reader = ConsoleReader(str(MODEL), threads=1)
    reader.warm_up()
    assert reader.ocr.text_rec is not None


@needs_models
def test_detection_runs_on_a_shrunk_image_and_times_are_split():
    """계획 0081: 앱 사진(긴 변 2048)을 원본 크기로 글자 찾기 하던 것(RapidOCR 기본 = 짧은 변 736 이상) → 긴 변 DET_SIDE로
    줄여 찾고, 읽기는 원본에서. 서버 로그가 찾기·인식 시간을 따로 남기게 stats에 det_s·rec_s."""
    import cv2
    from app.console_ocr.field import DET_SIDE, ConsoleReader
    from app.console_ocr.pool import MODEL
    img = cv2.imread(str(SAMPLE))
    s = 2048 / max(img.shape[:2])
    img = cv2.resize(img, None, fx=s, fy=s, interpolation=cv2.INTER_CUBIC)   # 앱이 보내는 크기
    reader = ConsoleReader(str(MODEL), threads=1)
    res = reader.read(img)
    assert (reader.ocr.text_det.limit_type, reader.ocr.text_det.limit_side_len) == ("max", DET_SIDE)
    assert (res.status, res.distance_m, res.duration_s) == ("READ", 6470, 34 * 60 + 49)
    assert reader.stats["det_s"] > 0 and reader.stats["rec_s"] > 0


@needs_models
def test_real_engine_reads_a_synthetic_console_in_a_child_process():
    engine = OnnxOcr(ReaderPool(workers=1))
    try:
        wait_idle(engine.pool, seconds=180)
        data_ = SAMPLE.read_bytes()
        assert engine.read([Photo(data_), Photo(data_)]) == ConsoleRead.read(6470, 34 * 60 + 49)
        with pytest.raises(BadPhoto):
            engine.read([Photo(b"\xff\xd8\xff not a jpeg")])
        import cv2
        import numpy as np
        ok, bomb = cv2.imencode(".jpg", np.zeros((8000, 8000, 3), np.uint8))   # 64 MP(상한 50 MP), 파일은 작음
        assert ok and len(bomb) < 4 * 1024 * 1024
        with pytest.raises(BadPhoto):                                          # 풀지 않고 못 엶
            engine.read([Photo(bomb.tobytes())])
        assert engine.read([Photo(data_)]) == ConsoleRead.read(6470, 34 * 60 + 49)   # 프로세스는 멀쩡
    finally:
        engine.close()
