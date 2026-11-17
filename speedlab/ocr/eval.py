"""학습한 모델 채점(ADR-0012 채점 (1)(3)): ONNX로 내보내 onnxruntime으로 글자 정확도·처리 시간을 잰다.

사용: python eval.py --size nano --run runs/yolox_nano [--photos <우리 사진 폴더(COCO)>]
- public_test: 공개 데이터 test(학습에 안 씀). 정답은 상자에서 만든 글자
- synth_val: 합성(다른 시드)
- photos: 새 기종 실사진(학습에 안 쓴 것) — 이게 진짜 기준. 사진은 저장소에 안 올림
결과는 runs/.../eval.json(틀린 것 목록 포함). 시간은 2스레드(오라클 2코어 흉내)로 칸 3개(브래킷 3장).
"""
import argparse
import json
import time
from pathlib import Path

import cv2
import numpy as np
import torch

import mps_patch  # noqa: F401
from dataset import IN_H, IN_W, load_coco
from onnx_engine import OnnxReader
from train_yolox import HERE, build


def export(size: str, run: Path) -> Path:
    model = build(size)
    model.load_state_dict(torch.load(run / "best.pth", map_location="cpu"))
    model.eval()
    model.head.decode_in_inference = True
    path = run / "model.onnx"
    torch.onnx.export(model, torch.zeros(1, 3, IN_H, IN_W), str(path), input_names=["images"], output_names=["output"],
                      opset_version=17, dynamo=False)
    return path


def score(reader: OnnxReader, items) -> dict:
    right, wrong = 0, []
    for path, _, truth in items:
        got = reader.read(cv2.imread(str(path), cv2.IMREAD_COLOR))
        if got == truth:
            right += 1
        else:
            wrong.append({"file": path.name, "truth": truth, "got": got})
    return {"n": len(items), "accuracy": round(right / max(1, len(items)), 4), "wrong": wrong}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", default="nano")
    ap.add_argument("--run", default="runs/yolox_nano")
    ap.add_argument("--photos", default="")
    args = ap.parse_args()
    run = HERE / args.run
    onnx_path = export(args.size, run)
    reader = OnnxReader(str(onnx_path), threads=2)
    sets = {"public_test": load_coco(HERE / "data/public/7seg_v3/test", public=True), "synth_val": load_coco(HERE / "data/synth/val")}
    if args.photos:
        sets["photos"] = load_coco(Path(args.photos))
    out = {name: score(reader, items) for name, items in sets.items()}

    img = cv2.imread(str(sets["public_test"][0][0]))
    for _ in range(5):
        reader.read(img)
    t = []
    for _ in range(30):
        t0 = time.perf_counter()
        for _ in range(3):
            reader.read(img)
        t.append((time.perf_counter() - t0) * 1000)
    out["ms_per_3_fields_2threads"] = {"median": round(float(np.median(t)), 1), "p90": round(float(np.percentile(t, 90)), 1)}
    out["onnx_mb"] = round(onnx_path.stat().st_size / 1e6, 2)
    (run / "eval.json").write_text(json.dumps(out, ensure_ascii=False, indent=1))
    print(json.dumps({k: (v if not isinstance(v, dict) or "wrong" not in v else {"n": v["n"], "accuracy": v["accuracy"]})
                      for k, v in out.items()}, ensure_ascii=False))


if __name__ == "__main__":
    main()
