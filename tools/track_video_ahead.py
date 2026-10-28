"""앞선 러너가 서야 할 화면 위치를 영상에서 직접 추적한다 (옵티컬 플로우 역추적).

카메라(촬영자)는 실제로 그 길을 달렸다. 그래서 "지금 D m 앞의 길 위 점" = "촬영자가 나중에 지나갈 곳".
  1) 나중 장면 j에서 촬영자 발 바로 앞(SEED_M m)의 길 위 점 3개(왼쪽 끝·가운데·오른쪽 끝)를 찍는다 (확실히 길 위)
  2) 그 점들을 장면 j -> j-1 -> ... -> 지금 장면 i로 옵티컬 플로우로 거꾸로 따라온다
  3) 지금 장면에서 그 점들의 위치 = 그 거리에 있는 러너의 발 위치와 길 폭 (커브·오르내리막 그대로)
  추적이 앞뒤로 어긋나면(가려짐·화면 밖) 그 점은 지금 장면에서 보이지 않는 것으로 본다.

거리: 코스 지도({id}.course.json)의 "영상 시각 -> 달린 거리" 표로 depth = s(j) - s(i) + SEED_M.
결과: run3d/assets/video/{id}.ahead.json
  { "t0", "step", "frames": [ [[depth, xl, xc, xr, y], ...], ... ] }   (화면 좌표 0~1, 보이는 점만, depth 오름차순)

  python tools/track_video_ahead.py o0EmQXATQQI --start 55
  python tools/track_video_ahead.py o0EmQXATQQI --start 300 --end 420 --out 시험.json   # 일부만
"""
import argparse
import json
import math
import sys
import time as clock
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
VIDEO_DIR = ROOT / "run3d" / "assets" / "video"

W, H = 480, 270
FPS = 10  # 추적에 쓰는 장면 수 (초당)
AHEAD_S = 25  # 몇 초 뒤 장면까지 거꾸로 따라올지
SEED_EVERY = 5  # 0.5초마다 씨앗
SEED_M = 6.0  # 씨앗: 촬영자 발 앞 6m (평평한 땅 가정으로 화면 높이 계산)
HORIZON, VFOV, CAM_H = 0.46, 65.0, 1.5
FB_TOL = 1.5  # 앞뒤 추적 어긋남 허용 (px)


def seed_y():
    f = 0.5 / math.tan(math.radians(VFOV / 2))
    return HORIZON + f * CAM_H / SEED_M


def road_bounds(road, t, y):
    """길 추적 데이터에서 시각 t, 높이 y의 길 [왼쪽, 오른쪽] (가장 가까운 키프레임)."""
    keys = road["keyframes"]
    k = min(range(len(keys)), key=lambda i: abs(keys[i]["time"] - t)) if len(keys) < 50 else \
        min(max(0, int(round(t / (keys[1]["time"] - keys[0]["time"])))), len(keys) - 1)
    rows = keys[k]["rows"]
    if y < rows[0][0]:
        return None
    for a, b in zip(rows, rows[1:]):
        if a[0] <= y <= b[0]:
            u = (y - a[0]) / (b[0] - a[0])
            return a[1] + (b[1] - a[1]) * u, a[2] + (b[2] - a[2]) * u
    return rows[-1][1], rows[-1][2]


def sample(flow, pts):
    """flow(H, W, 2)를 점들(N, 2, px)에서 쌍선형으로 읽는다."""
    x = np.clip(pts[:, 0], 0, W - 1.001)
    y = np.clip(pts[:, 1], 0, H - 1.001)
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    f = flow.astype(np.float32)
    return (f[y0, x0] * (1 - fx) * (1 - fy) + f[y0, x0 + 1] * fx * (1 - fy)
            + f[y0 + 1, x0] * (1 - fx) * fy + f[y0 + 1, x0 + 1] * fx * fy)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("video_id")
    p.add_argument("--start", type=float, default=0)
    p.add_argument("--end", type=float, default=None)
    p.add_argument("--out", default=None)
    args = p.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")

    course = json.loads((VIDEO_DIR / f"{args.video_id}.course.json").read_text(encoding="utf-8"))
    st, ss = np.array(course["sync"]["t"]), np.array(course["sync"]["s"])
    s_at = lambda t: float(np.interp(t, st, ss))  # noqa: E731
    road = json.loads((VIDEO_DIR / f"{args.video_id}.road.json").read_text())
    ys = seed_y()

    cap = cv2.VideoCapture(str(VIDEO_DIR / f"{args.video_id}.local.mp4"))
    fps = cap.get(cv2.CAP_PROP_FPS)
    duration = cap.get(cv2.CAP_PROP_FRAME_COUNT) / fps
    end = min(args.end or duration, duration)
    cap.set(cv2.CAP_PROP_POS_MSEC, args.start * 1000)
    skip = max(1, round(fps / FPS))
    dis = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_FAST)

    win = AHEAD_S * FPS
    frames, times, fwd, bwd = [], [], [], []  # 슬라이딩 창
    out_frames = []
    t_first = None
    started = clock.time()

    def emit():
        """창의 첫 장면(i=0)에 대해 앞쪽 씨앗들을 거꾸로 따라와 기록."""
        n = len(frames)
        seeds = []  # (시작 장면 j, 점 3개)
        for j in range(SEED_EVERY, n, SEED_EVERY):
            b = road_bounds(road, times[j], ys)
            if b is None:
                continue
            xl, xr = b
            seeds.append((j, np.array([[xl * W, ys * H], [(xl + xr) / 2 * W, ys * H], [xr * W, ys * H]])))
        if not seeds:
            out_frames.append([])
            return
        pts = np.zeros((len(seeds) * 3, 2))
        alive = np.zeros(len(seeds) * 3, bool)
        start_j = np.repeat([s[0] for s in seeds], 3)
        for k in range(n - 2, -1, -1):  # 장면 k+1 -> k
            new = start_j == k + 1
            if new.any():
                idx = np.flatnonzero(new)
                pts[idx] = np.vstack([s[1] for s in seeds if s[0] == k + 1])
                alive[idx] = True
            a = np.flatnonzero(alive)
            if not len(a):
                continue
            q = pts[a] + sample(bwd[k], pts[a])
            r = q + sample(fwd[k], q)
            ok = (np.linalg.norm(r - pts[a], axis=1) < FB_TOL) & (q[:, 0] >= 0) & (q[:, 0] < W) & (q[:, 1] >= 0) & (q[:, 1] < H)
            pts[a] = q
            alive[a[~ok]] = False
        rows = []
        s0 = s_at(times[0])
        for m, (j, _) in enumerate(seeds):
            trio = slice(3 * m, 3 * m + 3)
            if not alive[trio].all():
                continue
            l, c, r = pts[trio]
            depth = s_at(times[j]) - s0 + SEED_M
            if depth <= SEED_M or l[0] >= r[0]:
                continue
            rows.append([round(depth, 2), round(l[0] / W, 4), round(c[0] / W, 4), round(r[0] / W, 4),
                         round(c[1] / H, 4)])
        rows.sort()
        out_frames.append(rows)

    i = 0
    while True:
        ok, f = cap.read()
        if not ok:
            break
        t = cap.get(cv2.CAP_PROP_POS_MSEC) / 1000
        if t > end + AHEAD_S:
            break
        g = cv2.cvtColor(cv2.resize(f, (W, H), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2GRAY)
        if frames:
            fwd.append(dis.calc(frames[-1], g, None).astype(np.float16))
            bwd.append(dis.calc(g, frames[-1], None).astype(np.float16))
        frames.append(g)
        times.append(t)
        if t_first is None:
            t_first = t
        if len(frames) > win:
            if times[0] <= end and len(out_frames) * (1 / FPS) * 0 == 0:
                if round((times[0] - t_first) * FPS) % FPS == 0:  # 1초마다 기록
                    emit()
            frames.pop(0); times.pop(0); fwd.pop(0); bwd.pop(0)
        for _ in range(skip - 1):
            cap.grab()
        i += 1
        if i % 600 == 0:
            el = clock.time() - started
            print(f"{t:.0f}s / {end:.0f}s  ({el:.0f}s 경과)", flush=True)
    cap.release()
    result = {"t0": round(t_first, 2), "step": 1.0, "seedM": SEED_M, "frames": out_frames}
    out = Path(args.out) if args.out else VIDEO_DIR / f"{args.video_id}.ahead.json"
    out.write_text(json.dumps(result, separators=(",", ":")), encoding="utf-8")
    print(f"저장: {out} ({len(out_frames)}장면, 평균 {np.mean([len(f) for f in out_frames]):.1f}점)")


if __name__ == "__main__":
    main()
