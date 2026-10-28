"""LED 7세그먼트 계기판 칸 하나 읽기: 꺼진 획("888")으로 숫자 칸·기울기를 찾고, 획마다 켜졌는지 직접 판별 (학습 없음).

1. 모든 획(켜진 것 + 꺼져서 흐리게 비치는 것) = 빨간 채널 Otsu → 숫자 칸 격자
2. 기울기: 세로 투영의 빈 열이 가장 많아지는 shear
3. 칸마다 7획 + 소수점 자리의 밝기(켜진 LED는 하얗게 타서 초록 채널이 큼) → 칸 안 획 밝기를 두 무리로 나눠 켜짐/꺼짐
사용: python seg7_grid.py <칸 이미지(컬러)> [디버그 이미지 저장 경로]
"""
import sys

import cv2
import numpy as np

TABLE = {'abcdef': '0', 'bc': '1', 'abdeg': '2', 'abcdg': '3', 'bcfg': '4', 'acdfg': '5', 'acdefg': '6', 'abc': '7',
         'abcdefg': '8', 'abcdfg': '9', 'g': '-', 'acdef': 'C', 'abefg': 'P', 'adefg': 'E', 'aefg': 'F', 'def': 'L',
         'cdeg': 'o', 'dfeg': 't'}
# 숫자 칸(기울기 바로잡은 뒤) 안 상대 좌표 x0, x1, y0, y1
ZONES = {'a': (.3, .7, .0, .12), 'b': (.78, 1., .15, .42), 'c': (.78, 1., .58, .85), 'd': (.3, .7, .88, 1.),
         'e': (.0, .22, .58, .85), 'f': (.0, .22, .15, .42), 'g': (.3, .7, .44, .56)}


def shear(img, k):
    h, w = img.shape[:2]
    return cv2.warpAffine(img, np.float32([[1, k, 0], [0, 1, 0]]), (w + int(k * h) + 2, h), flags=cv2.INTER_NEAREST)


def runs(mask1d, min_gap=3):
    idx = np.where(mask1d)[0]
    if not len(idx):
        return []
    out, s, p = [], idx[0], idx[0]
    for i in idx[1:]:
        if i - p > min_gap:
            out.append((s, p + 1)); s = i
        p = i
    out.append((s, p + 1))
    return out


def segment_mask(color_bgr, mode):
    if mode == 'ghost':   # 노출을 낮춘 사진: 켜진 + 꺼진 획 모두(빨간 채널)
        R = cv2.GaussianBlur(color_bgr[..., 2], (5, 5), 0)
        _, m = cv2.threshold(R, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        return cv2.morphologyEx(m, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    # 보통 노출: LED가 번져 꺼진 획이 묻힘 → 하얗게 타는 중심(초록 채널)만, 빈 가운데는 메움
    G = cv2.GaussianBlur(color_bgr[..., 1], (5, 5), 0)
    _, m = cv2.threshold(G, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    return cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))


def read(color_bgr, debug=None, mode='core'):
    allseg = segment_mask(color_bgr, mode)
    lit = color_bgr[..., 1].astype(float) + 0.3 * color_bgr[..., 2]                  # 켜짐 정도
    ys = np.where(allseg.sum(1) > 0.15 * allseg.sum(1).max())[0]
    if not len(ys):
        return ''
    y0, y1 = ys[0], ys[-1] + 1
    seg, lit = allseg[y0:y1], lit[y0:y1]
    k = max(np.arange(0, 0.4, 0.01), key=lambda k: (shear(seg, k).sum(0) == 0).sum())
    seg, lit = shear(seg, k), shear(lit.astype(np.float32), k)
    H = seg.shape[0]
    cells = []                       # (x0, x1, 종류) 종류: digit / dot
    for a, b in runs(seg.sum(0) > 0):
        hh = np.where(seg[:, a:b].sum(1) > 0)[0]
        tall = hh[-1] - hh[0] + 1
        if tall > 0.6 * H and (b - a) > 0.25 * H:
            cells.append((a, b, 'digit'))
        elif tall > 0.6 * H:         # 좁고 긴 칸 = 1 (꺼진 격자가 없을 때)
            cells.append((a, b, 'one'))
        elif tall < 0.3 * H and hh[0] > 0.6 * H:
            cells.append((a, b, 'dot'))
    # 칸마다 획 밝기
    vals = []
    for a, b, kind in cells:
        if kind == 'dot':
            vals.append(('dot', lit[int(.7 * H):, a:b].max()))
            continue
        if kind == 'one':
            vals.append(('one', None))
            continue
        w = b - a
        zs = {}
        for z, (x0, x1, yy0, yy1) in ZONES.items():
            patch = lit[int(yy0 * H):max(int(yy1 * H), int(yy0 * H) + 1), a + int(x0 * w):a + max(int(x1 * w), int(x0 * w) + 1)]
            zs[z] = float(np.percentile(patch, 90)) if patch.size else 0
        vals.append(('digit', zs))
    allv = np.array([v for kind, x in vals if kind != 'one' for v in (x.values() if kind == 'digit' else [x])])
    if not len(allv):
        return ''
    # 켜짐/꺼짐 경계: 획 밝기를 두 무리로 (가장 큰 틈)
    s = np.sort(allv)
    gap = np.argmax(np.diff(s)) if len(s) > 1 else 0
    thr = (s[gap] + s[gap + 1]) / 2 if len(s) > 1 else s[0] + 1
    if len(s) > 1 and s[gap + 1] - s[gap] < 0.15 * (s[-1] + 1):   # 뚜렷한 틈이 없으면 다 꺼짐으로
        thr = s[-1] + 1
    out = ''
    for kind, x in vals:
        if kind == 'one':
            out += '1'
            continue
        if kind == 'dot':
            out += '.' if x > thr else ''
            continue
        on = ''.join(z for z in 'abcdefg' if x[z] > thr)
        if on:
            out += TABLE.get(on, f'?{on}?')
        elif out:                    # 앞쪽 빈 칸은 무시, 숫자 사이 빈 칸은 공백
            out += ' '
    if debug:
        vis = cv2.cvtColor(seg, cv2.COLOR_GRAY2BGR)
        for a, b, kind in cells:
            cv2.rectangle(vis, (a, 0), (b - 1, H - 1), {'digit': (0, 255, 0), 'one': (0, 255, 255)}.get(kind, (255, 0, 0)), 1)
        cv2.imwrite(debug, vis)
    return out.strip()


if __name__ == '__main__':
    print(read(cv2.imread(sys.argv[1]), sys.argv[2] if len(sys.argv) > 2 else None))
