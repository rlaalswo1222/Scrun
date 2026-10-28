"""3D 복원 진행 상황 한 줄: 완료 구간 수 / 전체, 작업별 현재 구간의 단계.

  python tools/sfm_progress.py
"""
import re
import sys
import time
from pathlib import Path

SFM = Path(__file__).resolve().parent.parent / "run3d" / "data" / "raw" / "sfm"
TOTAL = 44
FRAMES = 240


def stage(log):
    text = log.read_text(encoding="utf-8", errors="ignore")
    # 마지막으로 끝난 구간 이후의 기록만 본다
    last_done = text.rfind("\n구간 ")
    if last_done >= 0:
        text = text[last_done + 1:]
        text = text[text.find("\n") + 1:]
    # 새로 켜진 뒤의 기록만 본다 (재시작 전에 난 오류는 지난 일)
    restart = text.rfind("\n--- ")
    if restart >= 0:
        text = text[restart + 1:]
    if "Traceback" in text or "MemoryError" in text:
        return "오류"
    reg = re.findall(r"num_reg_frames=(\d+)", text)
    if reg:
        return f"3D 조립 {reg[-1]}/{FRAMES}"
    ext = text.count("Processed file")
    if ext >= FRAMES:
        return "짝짓기 중"
    if ext:
        return f"특징점 {ext}/{FRAMES}"
    return "준비 중"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    done = len(list(SFM.glob("chunk*.npz")))
    parts = [f"{time.strftime('%H:%M')} 완료 {done}/{TOTAL}"]
    for w in range(2):
        log = SFM / "logs" / f"worker{w}.log"
        if log.exists():
            parts.append(f"작업{w + 1}: {stage(log)}")
    print(" | ".join(parts), flush=True)


if __name__ == "__main__":
    main()
