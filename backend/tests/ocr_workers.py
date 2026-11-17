"""계기판 OCR 자식 프로세스 시험용 일꾼(test_ocr_engine). spawn으로 뜨므로 모듈 수준 함수여야 한다."""
import os
import time


def slow(conn, seconds: float) -> None:
    """준비는 바로, 읽기는 seconds초."""
    conn.send(("ready", None))
    while True:
        job = conn.recv()
        if job is None:
            return
        time.sleep(seconds)
        conn.send(("ok", [("READ", 1000, 300)] * len(job)))


def dies(conn) -> None:
    """읽기를 받으면 죽음(메모리 부족 등)."""
    conn.send(("ready", None))
    conn.recv()
    os._exit(1)


def crashes_on_start(conn) -> None:
    """모델을 올리다 죽음(가져오기 실패 등)."""
    os._exit(3)
