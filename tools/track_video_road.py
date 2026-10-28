"""1인칭 러닝 영상에서 내가 달리는 길의 경계를 추정한다 (일반 도로 인식 AI가 아니라 색 기반 근사).

길 경계를 화면 아래(96%)부터 지평선 근처(50%)까지 11줄에서 찾는다.
먼 길은 폭이 좁으므로 640x360 해상도에서 보고, 아래 줄부터 위로 올라가며 이전 줄과 이어지는 구간만 고른다.
길이 안 보이는 줄에서 멈추므로, 맨 윗줄이 "보이는 길 끝"(화면에서 가림선으로 쓰임)이 된다.

길 색을 찾는 방법 (--method)
  seed   (기본) 매 장면 발밑(화면 아래 가운데) 바닥 색을 배워서 비슷한 색을 따라간다. 아스팔트 보행로처럼 옆에 바다·잔디가 있는 길
  gravel 마운트쿡 산길용: 마른 풀보다 차갑고 채도가 낮은 자갈 색

  python tools/track_video_road.py run3d/assets/video/o0EmQXATQQI.local.mp4 run3d/assets/video/o0EmQXATQQI.road.json
  python tools/track_video_road.py run3d/assets/video/DQBCdbV2Enc.local.mp4 run3d/assets/video/DQBCdbV2Enc.road.json --method gravel
"""
import argparse
import json
from pathlib import Path
import cv2
import numpy as np

W, H = 640, 360
ROWS = [0.96, 0.89, 0.82, 0.76, 0.70, 0.66, 0.62, 0.59, 0.56, 0.53, 0.50]


def gravel_mask(small, state):
    lab = cv2.cvtColor(small, cv2.COLOR_BGR2LAB)
    hsv = cv2.cvtColor(small, cv2.COLOR_BGR2HSV)
    # Gravel is cooler and less saturated than the surrounding dry grass.
    seed = hsv[306:344, 210:430, 2][lab[306:344, 210:430, 2] < 128]
    max_value = min(210, float(np.median(seed))+55) if seed.size else 190
    return ((lab[:, :, 2] < 128) & (hsv[:, :, 1] < 90)
            & (hsv[:, :, 2] > 40) & (hsv[:, :, 2] < max_value)).astype('uint8') * 255


# 발밑 색과의 차이: 밝기(L)는 그늘·햇빛으로 크게 바뀌므로 덜, 푸른 바다·초록 잔디를 가르는 색(a, b)은 더 본다
SEED_WEIGHT = np.array([0.5, 1.0, 1.6], 'float32')
SEED_LIMIT = 13.0


def seed_mask(small, state):
    lab = cv2.cvtColor(small, cv2.COLOR_BGR2LAB).astype('float32')
    # 촬영자는 항상 길 위에 있으므로 매 장면 화면 아래 가운데 색을 새로 배운다.
    # (이전 장면 색·위치를 이어받으면 한 번 풀밭·바다로 빠졌을 때 계속 엇나간다)
    patch = lab[int(.88 * H):int(.97 * H), int(.40 * W):int(.60 * W)].reshape(-1, 3)
    state['color'] = np.median(patch, axis=0)
    state['center'] = W / 2
    diff = (lab - state['color']) * SEED_WEIGHT
    return (np.sqrt((diff ** 2).sum(axis=2)) < SEED_LIMIT).astype('uint8') * 255


MASKS = {'gravel': gravel_mask, 'seed': seed_mask}


def edges(frame, method, state):
    small = cv2.GaussianBlur(cv2.resize(frame, (W, H)), (5, 5), 0)
    mask = MASKS[method](small, state)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((3, 7), 'uint8'))
    if method == 'seed':
        lab = cv2.cvtColor(small, cv2.COLOR_BGR2LAB).astype('float32')
        ref = state['color'].copy()
    rows = []
    center = state.get('center', W / 2) if method == 'seed' else W / 2
    previous_width = 440
    previous_y = 1.0
    for y in ROWS:
        if method == 'seed':
            # 바로 아래 줄에서 찾은 길 색을 기준으로: 멀어질수록 뿌옇게 밝아지는 길 색을 따라간다
            band = lab[int(y * H)-2:int(y * H)+3]
            diff = (band - ref) * SEED_WEIGHT
            row = np.mean(np.sqrt((diff ** 2).sum(axis=2)) < SEED_LIMIT, axis=0) > 0.4
        else:
            row = np.mean(mask[int(y * H)-2:int(y * H)+3], axis=0) > 100
        indices = np.flatnonzero(np.diff(np.r_[False, row, False]))
        # 멀수록 길이 좁으므로 최소 폭도 줄인다 (가까운 줄 18px, 먼 줄 5px)
        min_run = 18 if y >= 0.66 else 5
        # 줄 사이 간격이 클수록 길이 옆으로 더 휠 수 있다
        reach = max(30, previous_width * 0.7) * (1 + 3 * (previous_y - y))
        runs = [(a, b) for a, b in zip(indices[::2], indices[1::2]) if b-a >= min_run]
        runs = [(a, b) for a, b in runs if abs((a+b)/2-center) < reach]
        if not runs:
            break  # 이 줄에서 길을 놓치면 더 먼 줄은 믿을 수 없으므로 멈춘다
        a, b = min(runs, key=lambda ab: abs((ab[0]+ab[1])/2-center) - min(ab[1]-ab[0], 220)*0.10)
        if abs((a+b)/2-center) >= 190:
            break
        center = (a+b)/2
        width = min(b-a, previous_width*1.02)
        if width / W < 0.016:
            break  # 화면 코드(validRows)가 받는 최소 폭(1.5%)보다 좁으면 더 먼 줄은 쓰지 않는다
        a, b = center-width/2, center+width/2
        if method == 'seed':
            picked = lab[int(y * H)-2:int(y * H)+3, int(a)+1:max(int(a)+2, int(b)-1)].reshape(-1, 3)
            ref = 0.5 * ref + 0.5 * np.median(picked, axis=0)
        previous_width = width
        previous_y = y
        rows.append([y, a/W, b/W])
    rows = list(reversed(rows))
    return rows


def main():
    p = argparse.ArgumentParser()
    p.add_argument('video')
    p.add_argument('output')
    p.add_argument('--step', type=float, default=2)
    p.add_argument('--limit', type=float, default=None, help='앞에서부터 이 초까지만 (시간 측정용)')
    p.add_argument('--method', choices=sorted(MASKS), default='seed')
    p.add_argument('--start', type=float, default=0, help='이 초부터 (인트로 건너뛰기, 그 전은 첫 결과로 채움)')
    args = p.parse_args()
    cap = cv2.VideoCapture(args.video)
    duration = cap.get(cv2.CAP_PROP_FRAME_COUNT) / cap.get(cv2.CAP_PROP_FPS)
    keys = []
    previous = [[.62, .45, .55], [.70, .35, .65], [.82, .25, .75], [.96, .15, .85]]
    state = {'center': W / 2, 'color': None}
    for t in np.arange(0, args.limit or duration, args.step):
        if t < args.start:
            keys.append({'time': round(float(t), 2), 'rows': None})
            continue
        cap.set(cv2.CAP_PROP_POS_MSEC, t*1000)
        ok, frame = cap.read()
        if not ok:
            break
        points = edges(frame, args.method, state)
        if len(points) >= 3:
            previous = points
        keys.append({'time': round(float(t), 2), 'rows': [[round(v, 4) for v in row] for row in previous]})
        if len(keys) % 150 == 0:
            print(f'{int(t)}/{int(duration)} seconds', flush=True)
    cap.release()
    first = next((k['rows'] for k in keys if k['rows']), previous)
    for k in keys:
        if k['rows'] is None:
            k['rows'] = [r[:] for r in first]
    # Median filter removes short segmentation jumps while preserving bends.
    # 줄 수가 프레임마다 달라서, 같은 높이의 줄이 있는 이웃 프레임끼리만 섞는다.
    for i, key in enumerate(keys):
        for row in key['rows']:
            neighbors = [r for k in keys[max(0, i-2):i+3] for r in k['rows'] if r[0] == row[0]]
            if neighbors:
                row[1:] = np.round(np.median(np.array(neighbors)[:, 1:], axis=0), 4).tolist()
    video_id = Path(args.video).name.split('.')[0]
    Path(args.output).write_text(json.dumps({'version': 2, 'videoId': video_id,
        'method': f'{args.method}-color-11rows', 'duration': duration, 'keyframes': keys}, separators=(',', ':')), encoding='utf-8')
    print(f'Saved {len(keys)} road keyframes', flush=True)


if __name__ == '__main__':
    main()
