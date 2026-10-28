"""앞길 추적표({id}.ahead.json)를 다듬는다: 깜빡임 제거 + 위치 떨림 제거.

추적의 "앞뒤 확인" 검사가 예민해서, 실제로 보이는 점도 장면마다 들쭉날쭉 빠진다(같은 거리가 수백 번 켜졌다 꺼짐).
  1) 장면마다 1.5m 간격 거리 격자에서 위치를 보간 (보이는 점 사이 10m 넘게 빈 곳은 비움)
  2) 보임/가림: 앞뒤 2장면(총 5장면 = 5초) 중 3장면 이상에서 보이면 보임 (다수결)
  3) 위치: 그 5장면 중 보이는 값들의 중앙값 (튀는 값 제거)
  4) 각 장면에서 보이는 구간은 가까운 쪽부터 끊기지 않게: 처음 비는 곳부터 뒤는 가림
결과는 원래 파일을 덮어쓴다 (원본은 {id}.ahead.raw.json으로 보관).

  python tools/clean_video_ahead.py o0EmQXATQQI
"""
import json
import shutil
import sys
from pathlib import Path

import numpy as np

VIDEO_DIR = Path(__file__).resolve().parent.parent / "run3d" / "assets" / "video"
STEP = 1.5
MAX_GAP = 10.0
HALF = 2  # 앞뒤 장면 수


def grid_frame(rows, grid):
    """한 장면의 행들 -> 격자 위 [xl, xc, xr, y] (없으면 NaN)."""
    out = np.full((len(grid), 4), np.nan)
    if len(rows) < 2:
        return out
    r = np.array(rows)
    d = r[:, 0]
    for k, D in enumerate(grid):
        if D < d[0] or D > d[-1]:
            continue
        i = np.searchsorted(d, D)
        i = max(1, min(i, len(d) - 1))
        if d[i] - d[i - 1] > MAX_GAP:
            continue
        u = (D - d[i - 1]) / (d[i] - d[i - 1] or 1)
        out[k] = r[i - 1, 1:] + (r[i, 1:] - r[i - 1, 1:]) * u
    return out


def main():
    vid = sys.argv[1]
    path = VIDEO_DIR / f"{vid}.ahead.json"
    raw = VIDEO_DIR / f"{vid}.ahead.raw.json"
    if not raw.exists():
        shutil.copy(path, raw)
    data = json.loads(raw.read_text())
    frames = data["frames"]
    max_d = max((f[-1][0] for f in frames if f), default=0)
    grid = np.arange(7.0, max_d + STEP, STEP)
    g = np.stack([grid_frame(f, grid) for f in frames])  # (장면, 거리, 4)
    vis = ~np.isnan(g[..., 0])
    n = len(frames)
    out = []
    before = after = 0
    for i in range(n):
        a, b = max(0, i - HALF), min(n, i + HALF + 1)
        need = min(3, b - a)
        votes = vis[a:b].sum(axis=0)
        keep = votes >= need
        # 가까운 쪽부터 끊기지 않게: 처음 보이는 칸부터 이어진 구간만
        if keep.any():
            start = int(np.argmax(keep))
            rest = keep[start:]
            if not rest.all():
                keep[start + int(np.argmin(rest)):] = False
        with np.errstate(all="ignore"), __import__("warnings").catch_warnings():
            __import__("warnings").simplefilter("ignore")
            med = np.nanmedian(g[a:b], axis=0)
        rows = [[round(float(grid[k]), 1)] + [round(float(v), 4) for v in med[k]]
                for k in np.flatnonzero(keep) if not np.isnan(med[k]).any() and med[k, 0] < med[k, 2]]
        out.append(rows)
        before += vis[i].sum()
        after += len(rows)
    data["frames"] = out
    data["cleaned"] = True
    path.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
    # 깜빡임 측정: 거리 16m가 켜졌다 꺼진 횟수
    def flips(fr, D):
        v = np.array([bool(f) and f[0][0] <= D <= f[-1][0] for f in fr])
        return int(np.abs(np.diff(v.astype(int))).sum()), v.mean()
    fb, vb = flips(frames, 16)
    fa, va = flips(out, 16)
    print(f"16m 지점 켜짐/꺼짐 횟수 {fb} -> {fa}, 보이는 비율 {vb:.0%} -> {va:.0%}")
    print(f"저장: {path}")


if __name__ == "__main__":
    main()
