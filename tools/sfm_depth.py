"""실사 모드 가려짐용 깊이 사진: 3D 복원 장면(1초에 4장)마다 AI 깊이 추정(Depth Anything V2 Small)으로
화면 전체의 상대 거리를 만든다. 3D 점이 안 생기는 밋밋한 벽(창고 판자벽 등)도 꽉 찬 면으로 나온다.
미터로 맞추기는 합칠 때 그 장면의 3D 점들로 한다 (tools/sfm_video.py ScreenOccluder).

  python tools/sfm_depth.py o0EmQXATQQI --start 120 --end 1860
  결과: run3d/data/raw/sfm/depth/{첫 장면 번호}.npz  (disp: 장면 × 135 × 240 float16, 클수록 가까움)
"""
import argparse
import sys
import time
from pathlib import Path

import cv2
import numpy as np
import torch

ROOT = Path(__file__).resolve().parent.parent
VIDEO_DIR = ROOT / "run3d" / "assets" / "video"
OUT = ROOT / "run3d" / "data" / "raw" / "sfm" / "depth"
FPS = 4
BLOCK = 240  # 파일 하나에 1분치
IN_H, IN_W = 266, 476  # 모델 입력 (14의 배수, 16:9 근처). 가림 판단에는 이 정도면 충분
OUT_H, OUT_W = 135, 240


def main():
    p = argparse.ArgumentParser()
    p.add_argument("video_id")
    p.add_argument("--start", type=float, default=120)
    p.add_argument("--end", type=float, default=1860)
    p.add_argument("--threads", type=int, default=12)
    args = p.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    torch.set_num_threads(args.threads)
    from transformers import AutoModelForDepthEstimation
    model = AutoModelForDepthEstimation.from_pretrained("depth-anything/Depth-Anything-V2-Small-hf").eval()
    mean = torch.tensor([0.485, 0.456, 0.406]).view(1, 3, 1, 1)
    std = torch.tensor([0.229, 0.224, 0.225]).view(1, 3, 1, 1)
    OUT.mkdir(parents=True, exist_ok=True)
    n_total = int(round((args.end - args.start) * FPS))
    cap = cv2.VideoCapture(str(VIDEO_DIR / f"{args.video_id}.local.mp4"))
    started = time.time()
    done = 0
    for b0 in range(0, n_total, BLOCK):
        path = OUT / f"{b0:05d}.npz"
        if path.exists():
            continue
        idx = range(b0, min(b0 + BLOCK, n_total))
        cap.set(cv2.CAP_PROP_POS_MSEC, (args.start + b0 / FPS) * 1000)
        disp = np.zeros((len(idx), OUT_H, OUT_W), np.float16)
        pos_t = args.start + b0 / FPS
        batch, slots = [], []

        def flush():
            x = torch.from_numpy(np.stack(batch)).permute(0, 3, 1, 2).float() / 255
            with torch.no_grad():
                y = model(pixel_values=(x - mean) / std).predicted_depth  # (n, IN_H, IN_W)
            y = torch.nn.functional.interpolate(y[:, None], (OUT_H, OUT_W), mode="area")[:, 0].numpy()
            for k, s in enumerate(slots):
                disp[s] = y[k]
            batch.clear()
            slots.clear()

        for s, i in enumerate(idx):
            want = args.start + i / FPS
            # 원하는 시각까지 디코딩만 하고 넘긴다 (60fps 영상에서 1초에 4장)
            while pos_t < want - 1e-3:
                if not cap.grab():
                    break
                pos_t = cap.get(cv2.CAP_PROP_POS_MSEC) / 1000
            ok, frame = cap.retrieve()
            if not ok:
                continue
            batch.append(cv2.cvtColor(cv2.resize(frame, (IN_W, IN_H), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2RGB))
            slots.append(s)
            if len(batch) == 8:
                flush()
        if batch:
            flush()
        tmp = path.with_suffix(".tmp.npz")
        np.savez_compressed(tmp, disp=disp, t0=args.start + b0 / FPS, fps=FPS)
        tmp.replace(path)
        done += len(idx)
        el = time.time() - started
        print(f"{time.strftime('%H:%M')} 장면 {b0 + len(idx)}/{n_total} ({el / done:.2f}초/장, 남은 {(n_total - b0 - len(idx)) * el / done / 60:.0f}분)",
              flush=True)


if __name__ == "__main__":
    main()
