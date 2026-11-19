"""계기판 읽기를 자식 프로세스에서(계획 0046). 모델을 미리 올린 프로세스 몇 개가 번갈아 읽는다.

- 스레드는 멈춰도 죽일 수 없지만 프로세스는 죽일 수 있다: 시간 제한을 넘기면 그 프로세스를 죽이고 새로 띄운다
- 프로세스가 죽거나(메모리 등) 엔진이 예외를 내면 호출한 쪽에 예외(라우터가 503, 다시 찍기 횟수 안 셈)
- 사진은 바이트로만 넘기고 디스크에 쓰지 않는다(BL-17 ③)
- spawn으로 띄움(uvicorn 스레드가 있는 프로세스를 fork하지 않게)
"""
import logging
import multiprocessing as mp
import queue
import sys
import threading
import time
from pathlib import Path
from typing import Callable, Optional

log = logging.getLogger(__name__)

MODEL = Path(__file__).parent / "models" / "yolox_nano_7seg.onnx"

# 한 사진의 결과: None(사진을 못 엶) 또는 (상태 READ·UNREADABLE, 거리 m, 시간 초)
PhotoResult = Optional[tuple]


class PoolBusy(Exception):
    """쉬는 프로세스가 시간 안에 안 생김(모두 일하는 중이거나 아직 뜨는 중)."""


class EngineDown(PoolBusy):
    """프로세스가 연달아 뜨지 못함(모델·의존성 없음 등). 기다리지 않고 바로 실패."""


class ReadTimeout(Exception):
    """읽기 자체가 시간 제한을 넘김 → 그 프로세스를 죽이고 새로 띄움(기다리느라 늦은 요청은 PoolBusy)."""


class WorkerFailed(Exception):
    """프로세스가 죽었거나 엔진이 예외를 냄."""


MAX_PIXELS = 50_000_000   # 사진 한 장 픽셀 상한(앱은 긴 변 2048로 줄여 보냄, 원본 12 MP·48 MP도 들어감)


def serve(conn, model_path: str, threads: int) -> None:
    """자식 프로세스 본체: 모델을 올리고 "ready"를 보낸 뒤, 사진 묶음(bytes 목록)을 받아 사진마다 결과를 돌려준다."""
    import os
    # 압축 폭탄 막기: 4 MB 안에 10억 픽셀이라고 적은 JPEG(검은 2000×2000 = 63 KB)를 풀면 몇 GB를 잡음(#68 리뷰).
    # OpenCV가 첫 디코드 때 읽는 값이라 cv2보다 먼저. 넘으면 cv2.error → 못 엶
    os.environ["OPENCV_IO_MAX_IMAGE_PIXELS"] = str(MAX_PIXELS)
    import cv2
    import numpy as np

    from .field import ConsoleReader

    cv2.setNumThreads(1)   # 프로세스 2개 × OpenCV 스레드가 2코어를 넘겨 쓰지 않게(onnxruntime도 threads개)
    reader = ConsoleReader(model_path, threads)
    reader.warm_up()   # 인식 모델까지 지금 올림: 없으면 ready 전에 죽어 시작 실패로 잡히고, 첫 요청이 올리는 시간을 안 냄

    def decode(data: bytes):
        try:
            return cv2.imdecode(np.frombuffer(data, np.uint8), cv2.IMREAD_COLOR)
        except cv2.error:
            return None   # 너무 큰 사진(MAX_PIXELS) 등
    conn.send(("ready", None))
    while True:
        try:
            job = conn.recv()
        except (EOFError, OSError):
            return
        if job is None:
            return
        try:
            conn.send(("ok", read_job(job, decode, reader)))
        except Exception as e:  # 엔진 고장: 이 묶음만 실패로 알리고 계속
            conn.send(("error", f"{type(e).__name__}: {e}"))


AGREE_STOP = 2   # 같은 값으로 읽힌 사진이 이만큼이면 나머지는 안 읽음(계획 0055)


def read_job(job: list, decode: Callable, reader, log_line: Callable = None) -> list:
    """사진 묶음(앱이 0 EV를 먼저 보냄)을 차례로 읽어 사진마다 결과. 실사진은 한 장 4–6초라(11/19 오라클) 같은 값으로 읽힌
    사진이 AGREE_STOP장이 되면 멈춘다: 합치기 규칙(읽힌 사진 값이 모두 같아야 READ, 11/17 지환)의 대조는 두 장으로 남기고
    나머지 장 시간을 아낌(그 장이 다른 값이었으면 다시 찍기였을 것을 놓침 — 두 장 일치 + 사용자 값 확인 E-05b로 받아들임).
    읽힌 값이 갈리면 결과가 이미 다시 찍기라 거기서 멈춤. 그 밖엔(못 읽은 사진뿐 등) 끝까지.
    읽지 않은 사진은 결과 목록에서 빠짐(합치기는 받은 것만 봄)."""
    if log_line is None:
        def log_line(msg: str) -> None:
            print(msg, file=sys.stderr, flush=True)   # 자식 프로세스 → docker 로그
    out, agree = [], {}
    started = time.perf_counter()
    for i, data in enumerate(job):
        img = decode(data)
        if img is None:
            out.append(None)
            continue
        t0 = time.perf_counter()
        r = reader.read(img)
        out.append((r.status, r.distance_m, r.duration_s))
        st = getattr(reader, "stats", {}) or {}
        # 어디서 걸리는지 사진마다 남김(11/19 14초 초과). 사진·숫자 값은 안 남김(BL-17 ③)
        log_line(f"[계기판 OCR] 사진 {i + 1}/{len(job)} {img.shape[1]}x{img.shape[0]} {len(data) // 1024} KB: "
                 f"{time.perf_counter() - t0:.2f}초(글자 영역 {st.get('boxes', 0)}개 찾기·인식 {st.get('ocr_s', 0):.2f}초, "
                 f"7세그먼트 {st.get('segs', 0)}개 {st.get('seg_s', 0):.2f}초) → {r.status}")
        if r.status == "READ" and i + 1 < len(job):
            key = (r.distance_m, r.duration_s)
            agree[key] = agree.get(key, 0) + 1
            if len(agree) > 1:   # 값이 갈림 → 나머지가 뭐든 합치기는 다시 찍기(리뷰: 남은 장에 4–6초를 쓰지 않게)
                log_line(f"[계기판 OCR] 읽은 값이 갈림 → 남은 {len(job) - i - 1}장 건너뜀")
                break
            if agree[key] >= AGREE_STOP:
                log_line(f"[계기판 OCR] 같은 값 {AGREE_STOP}장 → 남은 {len(job) - i - 1}장 건너뜀")
                break
    log_line(f"[계기판 OCR] 묶음 {len(out)}/{len(job)}장 {time.perf_counter() - started:.2f}초")
    return out


class _Worker:
    def __init__(self, ctx, target: Callable, args: tuple):
        self.conn, child = ctx.Pipe()
        self.proc = ctx.Process(target=target, args=(child, *args), daemon=True, name="console-ocr")
        self.proc.start()
        child.close()

    def wait_ready(self, timeout_s: float) -> bool:
        """모델을 올리고 "ready"를 보낼 때까지. 뜨다가 죽으면(가져오기 실패 등) 제한 시간을 다 기다리지 않고 바로 False."""
        end = time.monotonic() + timeout_s
        try:
            while time.monotonic() < end:
                if self.conn.poll(0.2):
                    return self.conn.recv()[0] == "ready"
                if not self.proc.is_alive():
                    return False
            return False
        except (EOFError, OSError):
            return False

    def kill(self) -> None:
        if self.proc.is_alive():
            self.proc.kill()
        self.proc.join(2)
        self.conn.close()


class ReaderPool:
    """workers개 프로세스(각 onnxruntime threads스레드). 처음 띄우기는 뒤에서(서버 시작을 막지 않음)."""

    START_TIMEOUT_S = 180.0   # 모델 올리기(처음엔 PP-OCRv5를 받을 수도 있음)
    RESTART_DELAY_S = 5.0     # 띄우기에 실패하면 이만큼 쉬고 다시
    DOWN_AFTER = 3            # 연달아 이만큼 못 뜨면 고장으로 보고 요청을 기다리게 하지 않음
    LATE_MIN_S = 0.2          # 요청이 끝날 때 읽기에 남은 시간이 이보다 적으면 읽기가 넘긴 것으로 보고 바로 죽임

    def __init__(self, workers: int = 2, threads: int = 1, model: Path = MODEL,
                 target: Callable = serve, args: Optional[tuple] = None):
        self.ctx = mp.get_context("spawn")
        self.target = target
        self.args = args if args is not None else (str(model), threads)
        self.idle: queue.Queue = queue.Queue()
        self.closed = False
        self.all: set = set()
        self.lock = threading.Lock()
        self.start_failures = 0   # 연달아 못 뜬 횟수(뜨면 0)
        for _ in range(workers):
            self._start_later()

    def _start_later(self, delay_s: float = 0.0) -> None:
        threading.Thread(target=self._start, args=(delay_s,), daemon=True, name="console-ocr-start").start()

    def _start(self, delay_s: float) -> None:
        if delay_s:
            time.sleep(delay_s)
        if self.closed:
            return
        w = _Worker(self.ctx, self.target, self.args)
        with self.lock:   # close()가 all을 비운 뒤 새 프로세스가 들어가 안 죽는 일이 없게(#68 리뷰)
            closed = self.closed
            if not closed:
                self.all.add(w)
        if closed:
            w.kill()
            return
        ready = w.wait_ready(self.START_TIMEOUT_S)
        if self.closed:
            self._drop(w)
            return
        if ready:
            self.start_failures = 0
            self.idle.put(w)
            return
        self.start_failures += 1
        level = logging.CRITICAL if self.start_failures >= self.DOWN_AFTER else logging.ERROR
        log.log(level, "계기판 OCR 프로세스를 띄우지 못함(연달아 %d번) — %.0f초 뒤 다시",
                self.start_failures, self.RESTART_DELAY_S)
        self._drop(w)
        self._start_later(self.RESTART_DELAY_S)

    def _drop(self, w: _Worker) -> None:
        with self.lock:
            self.all.discard(w)
        w.kill()

    def _replace(self, w: _Worker) -> None:
        self._drop(w)
        self._start_later()

    def read(self, photos: list[bytes], timeout_s: float) -> list[PhotoResult]:
        """timeout_s 안에 결과를 돌려주거나 예외. 프로세스는 **읽기 자체가** timeout_s를 넘길 때만 죽인다:
        빈 프로세스를 기다리느라 시간을 다 쓴 요청이 방금 받은 멀쩡한 프로세스를 죽이지 않게(#68 리뷰)."""
        deadline = time.monotonic() + timeout_s
        if self.start_failures >= self.DOWN_AFTER and self.idle.empty():
            raise EngineDown()
        try:
            w = self.idle.get(timeout=max(0.0, deadline - time.monotonic()))
        except queue.Empty:
            raise PoolBusy() from None
        try:
            sent = time.monotonic()
            w.conn.send(photos)
            if not w.conn.poll(max(0.0, deadline - time.monotonic())):
                left = sent + timeout_s - time.monotonic()
                if left <= self.LATE_MIN_S:   # 기다림 없이 받은 요청(남은 시간이 거의 없음) → 읽기가 넘긴 것
                    log.error("계기판 OCR이 %.1f초를 넘김 — 프로세스를 죽이고 새로 띄움", timeout_s)
                    self._replace(w)
                    raise ReadTimeout()
                # 기다리느라 늦은 요청: 이 요청은 끝내고, 읽기는 남은 시간만큼 더 지켜봄. 사진 탓이 아니라 밀린 것이라
                # 시간 초과(앱: 다시 찍기 안내)가 아니라 바쁨(앱: 잠시 뒤 다시 보내기)으로(계획 0055 리뷰)
                threading.Thread(target=self._finish_late, args=(w, left, timeout_s), daemon=True,
                                 name="console-ocr-late").start()
                raise PoolBusy()
            kind, out = w.conn.recv()
        except (EOFError, OSError, BrokenPipeError):
            log.error("계기판 OCR 프로세스가 죽음 — 새로 띄움")
            self._replace(w)
            raise WorkerFailed("프로세스가 죽음") from None
        self.idle.put(w)
        if kind != "ok":
            raise WorkerFailed(out)
        return out

    def _finish_late(self, w: _Worker, left_s: float, timeout_s: float) -> None:
        """요청은 이미 끝난 읽기: 결과는 버리고, 읽기가 timeout_s 안에 끝나면 프로세스를 돌려놓고 아니면 죽임."""
        try:
            if w.conn.poll(left_s):
                w.conn.recv()
                if self.closed:
                    self._drop(w)
                else:
                    self.idle.put(w)
                return
            log.error("계기판 OCR이 %.1f초를 넘김 — 프로세스를 죽이고 새로 띄움", timeout_s)
        except (EOFError, OSError):
            log.error("계기판 OCR 프로세스가 죽음 — 새로 띄움")
        self._replace(w)

    def close(self) -> None:
        with self.lock:
            self.closed = True
            workers = list(self.all)
            self.all.clear()
        for w in workers:
            try:
                w.conn.send(None)
            except (OSError, BrokenPipeError):
                pass
            w.kill()
