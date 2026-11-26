"""글자 찾기(PP-OCRv5 검출) 입력 크기별 시간·결과 비교(계획 0081).

    python bench_det.py data/console_synth_b --threads 1 --long 2048 --configs default,1280,960,736

사진을 앱이 보내는 크기(긴 변 --long, 11/19 앱 2048×1342)로 키워 읽는다. 설정마다 같은 사진을 읽어
한 장 시간(중앙·90%), 검출·인식·7세그먼트 시간, 글자 영역 수, 사진 단위 맞음·틀린 READ·UNREADABLE을 낸다.
합성 점수는 정확도가 아니다(바꾸기 전후 비교용). 서버 읽기 코드(`ConsoleReader`)를 그대로 쓰고 검출 크기만 바꾼다.
설정: default = RapidOCR 기본(짧은 변 736 이상, 0081 전 서버), 숫자 = 검출 입력 긴 변 상한(서버 지금 값 `DET_SIDE`).
"""
import argparse
import json
import statistics
import time
from collections import Counter
from pathlib import Path

import cv2

from field import ConsoleReader


def make_reader(onnx: str, threads: int, cfg: str) -> ConsoleReader:
    return ConsoleReader(onnx, threads, det_side=None if cfg == "default" else int(cfg))


def upscale(img, long_side: int):
    h, w = img.shape[:2]
    s = long_side / max(h, w)
    return cv2.resize(img, (round(w * s), round(h * s)), interpolation=cv2.INTER_CUBIC) if s != 1 else img


def pct(xs, q):
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(len(xs) * q))]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--onnx", default="runs/yolox_nano/model.onnx")
    ap.add_argument("--threads", type=int, default=1, help="오라클 일꾼은 1스레드(계획 0046)")
    ap.add_argument("--long", type=int, default=2048, help="앱이 보내는 사진 긴 변(0이면 원본 크기)")
    ap.add_argument("--configs", default="default,1280,960,736")
    ap.add_argument("--n", type=int, default=0, help="앞에서 n장만(0이면 전부)")
    ap.add_argument("--out", default=None, help="사진별 결과 JSON")
    a = ap.parse_args()

    folder = Path(a.folder)
    truth = json.loads((folder / "truth.json").read_text())
    names = list(truth)[: a.n or None]
    imgs = {n: cv2.imread(str(folder / n)) for n in names}
    if a.long:
        imgs = {n: upscale(im, a.long) for n, im in imgs.items()}
    shape = Counter(f"{im.shape[1]}x{im.shape[0]}" for im in imgs.values()).most_common(1)[0][0]
    print(f"{folder.name} {len(names)}장 · 입력 크기 예 {shape} · onnxruntime {a.threads}스레드")

    per_photo = {}
    for cfg in a.configs.split(","):
        reader = make_reader(a.onnx, a.threads, cfg)
        reader.read(imgs[names[0]])   # 처음 불러오기 시간은 빼고 잼
        count, tot, det, rec, seg, boxes = Counter(), [], [], [], [], []
        for n in names:
            t0 = time.perf_counter()
            res = reader.read(imgs[n])
            tot.append(time.perf_counter() - t0)
            det.append(reader.stats["det_s"])
            rec.append(reader.stats["rec_s"])
            seg.append(reader.stats["seg_s"])
            boxes.append(reader.stats["boxes"])
            t = truth[n]
            ok = res.status == "READ" and res.distance_m == t["distance_m"] and res.duration_s == t["duration_s"]
            kind = "맞음" if ok else ("틀린 READ" if res.status == "READ" else "UNREADABLE")
            count[kind] += 1
            per_photo.setdefault(n, {})[cfg] = {"kind": kind, "s": round(tot[-1], 3), "boxes": boxes[-1],
                                                "d": res.distance_m, "t": res.duration_s, "why": res.why}
        k = len(names)
        print(f"[{cfg:>7}] 한 장 {statistics.median(tot):.2f}초(중앙) {pct(tot, .9):.2f}초(90%) · "
              f"검출 {statistics.median(det):.2f} · 인식 {statistics.median(rec):.2f} · 7세그 {statistics.median(seg):.2f} · "
              f"영역 {statistics.median(boxes):.0f}개 · 맞음 {count['맞음']}/{k} ({count['맞음'] / k:.1%}) · "
              f"틀린 READ {count['틀린 READ']} · UNREADABLE {count['UNREADABLE']}", flush=True)

    if a.out:
        Path(a.out).write_text(json.dumps(per_photo, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
