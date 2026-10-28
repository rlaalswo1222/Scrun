"""코스 지도 검증: 영상 장면 위에 "그 시각 카메라에서 본 지도상 앞길"을 그린다.
빨간 선(앞길 가운데)과 노란 점(10·25·50·100m 앞)이 영상 속 길과 겹치면 출발점·동기화·방향이 맞는 것.

  python tools/check_course_overlay.py o0EmQXATQQI out.png 300,900,1500,2100,2700,3300
화면과 같은 카메라 가정: 세로 화각 65°, 지평선 46%, 카메라 높이 1.5m
"""
import json
import math
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
VFOV, HORIZON, CAM_H = 65.0, 0.46, 1.5


class Course:
    def __init__(self, data):
        self.pts = np.array(data["route"], float)
        seg = np.hypot(*np.diff(self.pts, axis=0).T)
        self.cum = np.r_[0, np.cumsum(seg)]
        self.t = np.array(data["sync"]["t"])
        self.s = np.array(data["sync"]["s"])

    def point(self, s):
        s = np.clip(s, 0, self.cum[-1])
        return np.array([np.interp(s, self.cum, self.pts[:, 0]), np.interp(s, self.cum, self.pts[:, 1])])

    def heading(self, s, back=3, ahead=15):
        d = self.point(s + ahead) - self.point(s - back)
        return d / (np.linalg.norm(d) or 1)


def project(course, time, s_target, W, H):
    s_cam = np.interp(time, course.t, course.s)
    c = course.point(s_cam)
    h = course.heading(s_cam)
    d = course.point(s_target) - c
    forward = d @ h
    right = d[0] * h[1] - d[1] * h[0]
    if forward < 1:
        return None
    f = 0.5 / math.tan(math.radians(VFOV / 2))  # 화면 높이 단위 초점거리
    y = HORIZON + f * CAM_H / forward
    x = 0.5 + f * (H / W) * right / forward
    return int(x * W), int(y * H)


def main():
    vid, out, times = sys.argv[1], sys.argv[2], [float(t) for t in sys.argv[3].split(",")]
    course_path = Path(sys.argv[4]) if len(sys.argv) > 4 else ROOT / "run3d" / "assets" / "video" / f"{vid}.course.json"
    data = json.loads(course_path.read_text(encoding="utf-8"))
    course = Course(data)
    cap = cv2.VideoCapture(str(ROOT / "run3d" / "assets" / "video" / f"{vid}.local.mp4"))
    tiles = []
    for t in times:
        cap.set(cv2.CAP_PROP_POS_MSEC, t * 1000)
        ok, f = cap.read()
        f = cv2.resize(f, (640, 360))
        W, H = 640, 360
        s_cam = np.interp(t, course.t, course.s)
        line = [project(course, t, s_cam + k, W, H) for k in np.arange(3, 200, 2)]
        line = [p for p in line if p and 0 <= p[1] < H]
        for a, b in zip(line, line[1:]):
            cv2.line(f, a, b, (0, 0, 255), 2)
        for k in (10, 25, 50, 100):
            q = project(course, t, s_cam + k, W, H)
            if q:
                cv2.circle(f, q, 4, (0, 255, 255), -1)
        cv2.putText(f, f"{t:.0f}s  s={s_cam:.0f}m", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 0), 2)
        tiles.append(f)
    if len(tiles) % 2:
        tiles.append(np.zeros_like(tiles[0]))
    cv2.imwrite(out, np.vstack([np.hstack(tiles[i:i + 2]) for i in range(0, len(tiles), 2)]))


if __name__ == "__main__":
    main()
