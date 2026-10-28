"""코스 지도의 출발 위치(s0)와 거리 배율(k)을 영상에 맞춰 찾는다.

정답: 화면 길 추적 데이터({id}.road.json)의 줄별 길 가운데 x.
예측: 영상 시각 t의 카메라 위치 s0 + k·d(t)에서, 그 줄 깊이만큼 앞의 지도상 길이 화면 어디에 보일지.
모든 장면·줄의 차이(중앙값)가 가장 작은 (s0, k)를 고른다. 추적이 엇나간 장면은 중앙값이 무시한다.

  python tools/align_video_course.py o0EmQXATQQI --osm run3d/data/raw/osm_otago.json \\
      --start -45.8893 170.5272 --end -45.8506 170.6177 --total-km 11.2 --run-start 53 --prefer "Otago Harbour Trail"
"""
import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tools"))
from coursegen import osm, routing  # noqa: E402
from build_video_course import WEIGHTS, distance_table  # noqa: E402

VFOV, HORIZON, CAM_H = 65.0, 0.46, 1.5
F = 0.5 / math.tan(math.radians(VFOV / 2))  # 화면 높이 단위 초점거리
FX = F * 9 / 16  # 화면 너비 단위


def main():
    p = argparse.ArgumentParser()
    p.add_argument("video_id")
    p.add_argument("--osm", required=True)
    p.add_argument("--start", type=float, nargs=2, required=True)
    p.add_argument("--end", type=float, nargs=2, required=True)
    p.add_argument("--total-km", type=float, required=True)
    p.add_argument("--run-start", type=float, default=0)
    p.add_argument("--prefer", default=None)
    args = p.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")

    elements = json.loads(Path(args.osm).read_text(encoding="utf-8"))["elements"]
    ways = [e for e in elements if e["type"] == "way" and "geometry" in e]
    proj = osm.Projector(*args.start)
    graph = routing.Graph(ways, proj, weights=WEIGHTS, bonus_name=args.prefer)
    full = routing.resample(routing.smooth(routing.route_between(graph, proj.xy(*args.start), proj.xy(*args.end))), 2.0)
    pts = np.array(full.coords)
    cum = np.r_[0, np.cumsum(np.hypot(*np.diff(pts, axis=0).T))]

    def point(s):
        s = np.clip(s, 0, cum[-1])
        return np.stack([np.interp(s, cum, pts[:, 0]), np.interp(s, cum, pts[:, 1])], axis=-1)

    bar = json.loads((ROOT / "run3d" / "data" / "raw" / f"{args.video_id}.bar.json").read_text())
    t_tab, d_tab = distance_table(bar, args.run_start, args.total_km * 1000)
    road = json.loads((ROOT / "run3d" / "assets" / "video" / f"{args.video_id}.road.json").read_text())

    # 관측: (영상 시각, 깊이 m, 길 가운데 x)
    obs = []
    for k in road["keyframes"][::3]:
        t = k["time"]
        if t < args.run_start + 5:
            continue
        for y, a, b in k["rows"]:
            if 0.53 <= y <= 0.82:
                obs.append((t, CAM_H * F / (y - HORIZON), (a + b) / 2))
    obs = np.array(obs)
    d_obs = np.interp(obs[:, 0], t_tab, d_tab)
    print(f"관측 {len(obs)}개, 경로 {cum[-1]:.0f}m")

    def score(s0, k):
        s_cam = s0 + k * d_obs
        c = point(s_cam)
        h = point(s_cam + 15) - point(s_cam - 3)
        h /= np.linalg.norm(h, axis=1, keepdims=True)
        q = point(s_cam + obs[:, 1]) - c
        fwd = (q * h).sum(axis=1)
        right = q[:, 0] * h[:, 1] - q[:, 1] * h[:, 0]
        ok = fwd > 1
        x = 0.5 + FX * right[ok] / fwd[ok]
        err = np.abs(x - obs[ok, 2])
        return float(np.median(np.minimum(err, 0.5))) if ok.any() else 1.0

    best = None
    for k in np.arange(0.94, 1.0601, 0.01):
        for s0 in np.arange(0, cum[-1] - k * d_tab[-1] + 1, 20):
            sc = score(s0, k)
            if best is None or sc < best[0]:
                best = (sc, s0, k)
    sc, s0, k = best
    # 세밀하게
    for kk in np.arange(k - 0.01, k + 0.0101, 0.002):
        for ss in np.arange(s0 - 30, s0 + 31, 2):
            if ss < 0:
                continue
            v = score(ss, kk)
            if v < sc:
                sc, s0, k = v, ss, kk
    print(f"최적 s0={s0:.0f}m, 거리 배율 k={k:.3f}, 화면 오차 중앙값 {sc:.3f} (화면 너비 비율)")
    for test in (0, 1100, 1959):
        print(f"  참고: s0={test}, k=1.0 -> {score(test, 1.0):.3f}")
    json.dump({"s0": float(s0), "k": float(k), "error": sc}, open(ROOT / "run3d" / "data" / "raw" / f"{args.video_id}.align.json", "w"))


if __name__ == "__main__":
    main()
