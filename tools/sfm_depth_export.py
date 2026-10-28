"""픽셀 단위 가림용 깊이 지도 내보내기 (run3d/src/video-depth.js가 읽는다).

장면(1초에 4장)마다 AI 깊이 사진을 미터로 맞춘 거리 지도를 160×90, 로그 눈금 8비트로 줄여
15초(60장)씩 한 PNG(6×10칸)에 담는다. 브라우저는 재생 위치 근처 PNG만 받아 아바타를 픽셀마다
영상 속 물체와 비교해 가린다.
  미터 맞춤: 3D 복원이 맞는 장면은 그 장면의 3D 점(다수결), 3D가 틀린 구간·직접 찍은 장면은 앞 2~20m 길바닥.

  python tools/sfm_depth_export.py o0EmQXATQQI
"""
import json
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import sfm_video as S  # noqa: E402

TW, TH = 320, 180  # 깊이 사진(240×135)보다 크게: 가림막 윤곽 경계를 또렷하게
COLS, ROWS = 6, 10
PER = COLS * ROWS
ZMIN, ZMAX = 0.5, 300.0


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    vid = sys.argv[1]
    t0, t1, fps = 120.0, 1860.0, 4
    S.DEPTH = S.DepthStore(t0, fps)
    bad_file = S.WORK / "bad_ranges.json"
    bad = json.loads(bad_file.read_text(encoding="utf-8"))["ranges"] if bad_file.exists() else []
    course = json.loads((S.VIDEO_DIR / f"{vid}.course.json").read_text(encoding="utf-8"))
    st, ss = np.array(course["sync"]["t"]), np.array(course["sync"]["s"])
    s_at = lambda t: np.interp(t, st, ss)  # noqa: E731
    segs = S.stitch(S.load_all_chunks(list(np.arange(t0, t1, 40.0)), s_at), fps)
    S.smooth_scale(segs, s_at, fps)
    keymap = {}
    for g in segs:  # 이어 붙인 본 경로가 앞에 있다 -> 먼저 들어간 것 우선
        for i, k in enumerate(g["keys"]):
            keymap.setdefault(int(k), (g, i))
    sfm = json.loads((S.VIDEO_DIR / f"{vid}.sfm.json").read_text(encoding="utf-8"))
    frames = sfm["frames"]
    ddir = S.VIDEO_DIR / f"{vid}.depth"
    out = ddir / "base"  # AI 깊이만. 최종 PNG는 occluders.compose가 가림막 윤곽을 얹어 ddir에 쓴다
    out.mkdir(parents=True, exist_ok=True)
    n = len(frames)
    ln = math.log(ZMAX / ZMIN)
    stats = {"points": 0, "rows": 0, "none": 0}
    for f0 in range(0, n, PER):
        atlas = np.full((ROWS * TH, COLS * TW), 255, np.uint8)  # 255 = 아주 멂 (가리지 않음)
        for j in range(PER):
            k = f0 + j
            if k >= n:
                break
            t = t0 + k / fps
            Z = None
            in_bad = any(a <= t <= b for a, b in bad)
            gi = keymap.get(int(round(t * fps)))
            if gi is not None and not in_bad:
                g, i = gi
                Z = S.ScreenOccluder(g["tree"], g["C"][i], g["R"][i], g["upm"][i], t).Z
                if Z is not None:
                    stats["points"] += 1
            if Z is None:
                Z = S.depth_from_rows(frames[k], S.DEPTH.get(t))
                if Z is not None:
                    stats["rows"] += 1
            if Z is None:
                stats["none"] += 1
                continue
            z = cv2.resize(Z.astype(np.float32), (TW, TH), interpolation=cv2.INTER_AREA)
            q = np.clip(np.round(255 * np.log(np.clip(z, ZMIN, ZMAX) / ZMIN) / ln), 0, 254).astype(np.uint8)
            r, c = divmod(j, COLS)
            atlas[r * TH:(r + 1) * TH, c * TW:(c + 1) * TW] = q
        cv2.imwrite(str(out / f"{f0 // PER:04d}.png"), atlas, [cv2.IMWRITE_PNG_COMPRESSION, 9])
        print(f"{f0 + PER}/{n}", flush=True)
    (ddir / "index.json").write_text(json.dumps({
        "t0": t0, "fps": fps, "tileW": TW, "tileH": TH, "cols": COLS, "rows": ROWS, "perFile": PER,
        "zmin": ZMIN, "zmax": ZMAX, "frames": n, "srcW": S.W, "srcH": S.H, "f": S.F_640, "k1": S.K1}), encoding="utf-8")
    import occluders
    occluders.compose(vid)
    print(f"끝: 3D 점으로 맞춤 {stats['points']}, 길바닥으로 맞춤 {stats['rows']}, 없음 {stats['none']}")


if __name__ == "__main__":
    main()
