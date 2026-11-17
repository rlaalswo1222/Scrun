"""계기판 전체 사진 채점(계획 0042, ADR-0012 채점 2): 사진 단위로 거리·시간이 둘 다 맞은 비율.

    python eval_console.py <폴더(truth.json 포함)> --onnx runs/yolox_nano/model.onnx [--threads 2] [--fails out]

truth.json: {"파일": {"distance_m": 3120, "duration_s": 1720}, ...}. 합성(console_synth.py) 점수는 정확도가 아니다.
틀린 것과 UNREADABLE은 따로 센다: 틀린 READ(잘못 인증될 수 있음)가 UNREADABLE(다시 찍기)보다 훨씬 나쁘다.
"""
import argparse
import json
import time
from collections import Counter
from pathlib import Path

import cv2

from field import ConsoleReader, draw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--onnx", default="runs/yolox_nano/model.onnx")
    ap.add_argument("--threads", type=int, default=2)
    ap.add_argument("--prefer", default="seg", choices=["seg", "rec"])
    ap.add_argument("--fails", default=None, help="틀린·못 읽은 사진을 그려 둘 폴더")
    a = ap.parse_args()

    folder = Path(a.folder)
    truth = json.loads((folder / "truth.json").read_text())
    reader = ConsoleReader(a.onnx, a.threads, a.prefer)
    reader.read(cv2.imread(str(folder / next(iter(truth)))))   # 처음 불러오기 시간은 빼고 잼
    if a.fails:
        Path(a.fails).mkdir(parents=True, exist_ok=True)

    count, ms, kinds = Counter(), [], Counter()
    for name, t in truth.items():
        img = cv2.imread(str(folder / name))
        t0 = time.perf_counter()
        res = reader.read(img)
        ms.append((time.perf_counter() - t0) * 1000)
        ok_d = res.distance_m == t["distance_m"]
        ok_t = res.duration_s == t["duration_s"]
        if res.status == "READ":
            count["맞음" if ok_d and ok_t else "틀린 READ"] += 1
        else:
            count["UNREADABLE"] += 1
            kinds[res.why.split("(")[0]] += 1
        if a.fails and not (res.status == "READ" and ok_d and ok_t):
            cv2.imwrite(str(Path(a.fails) / name), draw(img, res))
    n = len(truth)
    print(f"사진 {n}장 · 맞음 {count['맞음']} ({count['맞음'] / n:.1%}) · 틀린 READ {count['틀린 READ']} · "
          f"UNREADABLE {count['UNREADABLE']}")
    print("UNREADABLE 이유:", dict(kinds.most_common(6)))
    ms.sort()
    print(f"한 장 처리 {ms[len(ms) // 2]:.0f} ms(중앙), {ms[int(len(ms) * 0.9)]:.0f} ms(90%), 스레드 {a.threads}")


if __name__ == "__main__":
    main()
