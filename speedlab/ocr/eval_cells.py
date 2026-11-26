"""실사진 칸 단위 채점(계획 0083): 헬스장 사진에서 자른 거리·시간 칸을 7세그먼트 모델이 글자 그대로 맞히는 비율.

    python eval_cells.py data/gym_cells --onnx ../../backend/app/console_ocr/models/yolox_nano_7seg.onnx [--onnx 다른.onnx]

truth.json: {"파일": "0.15", ...}, "_partial": [LED 깜박임 중간에 찍혀 숫자 일부만 보이는 칸 — 채점에서 뺌].
학습에 넣지 않는 채점 전용 세트. 한 헬스장·적은 기종이라 숫자는 참고(정확도 아님).
"""
import argparse
import json
from pathlib import Path

import cv2

import _backend  # noqa: F401
from app.console_ocr.seg import OnnxReader


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--onnx", action="append", required=True, help="여러 번 주면 모델끼리 나란히")
    a = ap.parse_args()
    folder = Path(a.folder)
    truth = json.loads((folder / "truth.json").read_text())
    partial = set(truth.pop("_partial", []))
    readers = {Path(p).name: OnnxReader(p, 1) for p in a.onnx}
    score = {k: 0 for k in readers}
    clean = [f for f in truth if f not in partial]
    for f in sorted(truth):
        img = cv2.imread(str(folder / f))
        got = {k: r.read(img) for k, r in readers.items()}
        for k in readers:
            score[k] += f not in partial and got[k] == truth[f]
        mark = " (일부만 보임, 채점 안 함)" if f in partial else ""
        print(f"{f:22} 정답 {truth[f]:6} " + " · ".join(f"{k}: {g!r}" for k, g in got.items()) + mark)
    for k in readers:
        print(f"{k}: 온전한 칸 {len(clean)}개 중 {score[k]}개 맞음 ({score[k] / max(1, len(clean)):.0%})")


if __name__ == "__main__":
    main()
