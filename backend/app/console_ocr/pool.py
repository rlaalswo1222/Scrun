"""계기판 읽기를 자식 프로세스에서(계획 0043). 모델을 미리 올린 프로세스 몇 개가 번갈아 읽는다.

- 스레드는 멈춰도 죽일 수 없지만 프로세스는 죽일 수 있다: 시간 제한을 넘기면 그 프로세스를 죽이고 새로 띄운다
- 프로세스가 죽거나(메모리 등) 엔진이 예외를 내면 호출한 쪽에 예외(라우터가 503, 다시 찍기 횟수 안 셈)
- 사진은 바이트로만 넘기고 디스크에 쓰지 않는다(BL-17 ③)
- spawn으로 띄움(uvicorn 스레드가 있는 프로세스를 fork하지 않게)
"""
import logging
import multiprocessing as mp
import queue
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


class ReadTimeout(Exception):
    """읽기가 시간 제한을 넘김 → 그 프로세스를 죽이고 새로 띄움."""


class WorkerFailed(Exception):
    """프로세스가 죽었거나 엔진이 예외를 냄."""


def serve(conn, model_path: str, threads: int) -> None:
    """자식 프로세스 본체: 모델을 올리고 "ready"를 보낸 뒤, 사진 묶음(bytes 목록)을 받아 사진마다 결과를 돌려준다."""
    import cv2
    import numpy as np

    from .field import ConsoleReader

    reader = ConsoleReader(model_path, threads)
    conn.send(("ready", None))
    while True:
        try:
            job = conn.recv()
        except (EOFError, OSError):
            return
        if job is None:
            return
        try:
            out = []
            for data in job:
                img = cv2.imdecode(np.frombuffer(data, np.uint8), cv2.IMREAD_COLOR)
                if img is None:
                    out.append(None)
                    continue
                r = reader.read(img)
                out.append((r.status, r.distance_m, r.duration_s))
            conn.send(("ok", out))
        except Exception as e:  # 엔진 고장: 이 묶음만 실패로 알리고 계속
            conn.send(("error", f"{type(e).__name__}: {e}"))


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

    def __init__(self, workers: int = 2, threads: int = 1, model: Path = MODEL,
                 target: Callable = serve, args: Optional[tuple] = None):
        self.ctx = mp.get_context("spawn")
        self.target = target
        self.args = args if args is not None else (str(model), threads)
        self.idle: queue.Queue = queue.Queue()
        self.closed = False
        self.all: set = set()
        self.lock = threading.Lock()
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
        with self.lock:
            self.all.add(w)
        if w.wait_ready(self.START_TIMEOUT_S) and not self.closed:
            self.idle.put(w)
            return
        log.error("계기판 OCR 프로세스를 띄우지 못함 — %.0f초 뒤 다시", self.RESTART_DELAY_S)
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
        deadline = time.monotonic() + timeout_s
        try:
            w = self.idle.get(timeout=max(0.0, deadline - time.monotonic()))
        except queue.Empty:
            raise PoolBusy() from None
        try:
            w.conn.send(photos)
            if not w.conn.poll(max(0.0, deadline - time.monotonic())):
                log.error("계기판 OCR이 %.1f초를 넘김 — 프로세스를 죽이고 새로 띄움", timeout_s)
                self._replace(w)
                raise ReadTimeout()
            kind, out = w.conn.recv()
        except (EOFError, OSError, BrokenPipeError):
            log.error("계기판 OCR 프로세스가 죽음 — 새로 띄움")
            self._replace(w)
            raise WorkerFailed("프로세스가 죽음") from None
        self.idle.put(w)
        if kind != "ok":
            raise WorkerFailed(out)
        return out

    def close(self) -> None:
        self.closed = True
        with self.lock:
            workers = list(self.all)
            self.all.clear()
        for w in workers:
            try:
                w.conn.send(None)
            except (OSError, BrokenPipeError):
                pass
            w.kill()
