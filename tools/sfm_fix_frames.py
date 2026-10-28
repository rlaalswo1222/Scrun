"""직접 길 찍기(run3d/sfm-fix.html)용 장면 사진 뽑기: 3D 앞길이 틀린 구간을 1초 간격으로.

  python tools/sfm_fix_frames.py o0EmQXATQQI            # bad_ranges.json의 old 구간
  python tools/sfm_fix_frames.py o0EmQXATQQI 1488-1513 1664-1692
"""
import json
import sys
from pathlib import Path

import cv2

ROOT = Path(__file__).resolve().parent.parent
VIDEO_DIR = ROOT / "run3d" / "assets" / "video"
WORK = ROOT / "run3d" / "data" / "raw" / "sfm"
STEP_S = 1.0


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    vid = sys.argv[1]
    if len(sys.argv) > 2:
        ranges = [[float(x) for x in a.split("-")] for a in sys.argv[2:]]
    else:
        ranges = json.loads((WORK / "bad_ranges.json").read_text(encoding="utf-8")).get("old", [])
    out = VIDEO_DIR / f"{vid}.fix"
    out.mkdir(exist_ok=True)
    cap = cv2.VideoCapture(str(VIDEO_DIR / f"{vid}.local.mp4"))
    frames = []
    for a, b in ranges:
        t = float(a)
        while t <= b + 1e-6:
            cap.set(cv2.CAP_PROP_POS_MSEC, t * 1000)
            ok, img = cap.read()
            if ok:
                name = f"{t:.1f}.jpg"
                # 3D 계산과 같은 크기(960×540)라야 렌즈 값(초점 705px)이 그대로 맞는다
                cv2.imwrite(str(out / name), cv2.resize(img, (960, 540)), [cv2.IMWRITE_JPEG_QUALITY, 88])
                frames.append({"t": round(t, 1), "img": name})
            t += STEP_S
    (out / "index.json").write_text(json.dumps({"width": 960, "height": 540, "frames": frames}), encoding="utf-8")
    print(f"{len(frames)}장: {out}")


if __name__ == "__main__":
    main()
