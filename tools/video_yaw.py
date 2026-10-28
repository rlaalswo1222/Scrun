"""영상 속 카메라가 좌우로 얼마나 도는지(yaw) 시간별로 측정한다.

먼 풍경(지평선 근처 띠)이 화면에서 옆으로 밀리는 양을 위상 상관(phase correlation)으로 재서,
픽셀 이동 → 각도로 바꾼다. 가까운 물체(오른쪽 절벽·나무)의 시차를 피하려고 왼쪽(바다 쪽) 띠만 본다.
결과: [[영상 시각(초), 그 사이 회전(도, 오른쪽 +)], ...]

  python tools/video_yaw.py run3d/assets/video/o0EmQXATQQI.local.mp4 out.json --band 0.38 0.455 --xmin 0.12 --xmax 0.55

고정 글자(거리·눈금 표시)가 띠에 들어가면 "안 돌았다"는 쪽으로 끌려가므로 반드시 피한다.
"""
import argparse
import json
import math

import cv2
import numpy as np


def main():
    p = argparse.ArgumentParser()
    p.add_argument('video')
    p.add_argument('output')
    p.add_argument('--step', type=float, default=0.25, help='측정 간격(초)')
    p.add_argument('--band', type=float, nargs=2, default=[0.36, 0.50], help='화면 높이 비율 범위')
    p.add_argument('--xmin', type=float, default=0.0, help='이 비율부터 (영상에 고정으로 찍힌 글자를 피함)')
    p.add_argument('--xmax', type=float, default=0.6, help='이 비율까지')
    p.add_argument('--hfov', type=float, default=97.0, help='가로 화각(도)')
    args = p.parse_args()
    cap = cv2.VideoCapture(args.video)
    fps = cap.get(cv2.CAP_PROP_FPS)
    every = max(1, round(fps * args.step))
    w, h = 640, 360
    y0, y1 = int(args.band[0] * h), int(args.band[1] * h)
    x0, x1 = int(args.xmin * w), int(args.xmax * w)
    window = cv2.createHanningWindow((x1 - x0, y1 - y0), cv2.CV_32F)
    deg_per_px = args.hfov / w
    out, prev, i = [], None, 0
    while True:
        if not cap.grab():
            break
        if i % every == 0:
            ok, frame = cap.retrieve()
            if not ok:
                break
            gray = cv2.cvtColor(cv2.resize(frame, (w, h)), cv2.COLOR_BGR2GRAY)
            band = np.float32(gray[y0:y1, x0:x1])
            if prev is not None:
                (dx, _), response = cv2.phaseCorrelate(prev, band, window)
                # 풍경이 왼쪽으로 밀리면(dx<0) 카메라는 오른쪽으로 돈 것
                out.append([round(i / fps, 3), round(-dx * deg_per_px, 4), round(response, 3)])
            prev = band
            if len(out) % 2000 == 0 and out:
                print(f'{i / fps:.0f}s', flush=True)
        i += 1
    cap.release()
    json.dump(out, open(args.output, 'w'))
    print(f'Saved {len(out)} yaw samples', flush=True)


if __name__ == '__main__':
    main()
