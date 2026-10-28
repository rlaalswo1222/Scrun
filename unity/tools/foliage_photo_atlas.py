# Poly Haven(CC0) 사진 잎으로 잎 아틀라스의 "활엽수 잎 뭉치"·"침엽수 가지" 칸을 만든다.
#  입력: fir_tree_01 twig(diff/alpha), tree_small_02 leaves(diff/alpha) 2k jpg
#  출력: Assets/Scrun/ThirdParty/PolyHaven/Derived/Sty_FoliagePhoto.png (2048², RGBA)
#        왼쪽 위 1024² = 침엽수 가지(u = 밑동→끝, 가로 가운데가 줄기), 오른쪽 아래 1024² = 활엽수 잎 뭉치
#  색은 회색 명암 위주로 정규화(평균 밝기 0.78, 색 편차 35%만 남김) → 실제 색은 메쉬 정점 색으로 입힌다(FoliageAtlas와 같은 규칙).
# usage: python foliage_photo_atlas.py <src_dir> <out_png>
import sys, math, random
import numpy as np
from PIL import Image
from scipy import ndimage

src, out = sys.argv[1], sys.argv[2]
N = 2048; Q = N // 2
rng = random.Random(4242)

def load(name):
    d = np.asarray(Image.open(f'{src}/{name}_diff.jpg').convert('RGB'), np.float32) / 255
    a = np.asarray(Image.open(f'{src}/{name}_alpha.jpg').convert('L'), np.float32) / 255
    return d, a

def normalize(rgb, alpha):
    m = alpha > 0.5
    mean = rgb[m].mean(0)
    lum = (rgb * [0.3, 0.59, 0.11]).sum(-1, keepdims=True)
    mlum = float((mean * [0.3, 0.59, 0.11]).sum())
    ratio = rgb / np.maximum(mean, 1e-3) * mlum           # 평균색을 회색으로
    g = lum + (ratio - lum) * 0.35                         # 색 편차 35%
    g = g / mlum * 0.78
    g = np.where(g > 0.8, 0.8 + (g - 0.8) * 0.45, g)
    return np.clip(g, 0, 1)

def sprite(rgb, a, box):
    x0, y0, x1, y1 = box
    return rgb[y0:y1, x0:x1].copy(), a[y0:y1, x0:x1].copy()

# 덮어 칠하기: 스프라이트를 회전·축소해 canvas에 얹음. shade = 밝기 배율
canvas = np.zeros((N, N, 4), np.float32)
def stamp(spr, cx, cy, scale, angle_deg, shade, region):
    rgb, a = spr
    img = Image.fromarray((np.dstack([rgb, a]) * 255).astype(np.uint8), 'RGBA')
    w, h = img.size
    img = img.resize((max(2, int(w * scale)), max(2, int(h * scale))), Image.LANCZOS)
    img = img.rotate(angle_deg, Image.BICUBIC, expand=True)
    s = np.asarray(img, np.float32) / 255
    sh, sw = s.shape[:2]
    rx0, ry0, rx1, ry1 = region
    x0 = int(cx - sw / 2); y0 = int(cy - sh / 2)
    for yy in range(sh):
        Y = y0 + yy
        if Y < ry0 or Y >= ry1: continue
        xs0 = max(x0, rx0); xs1 = min(x0 + sw, rx1)
        if xs1 <= xs0: continue
        src_row = s[yy, xs0 - x0:xs1 - x0]
        dst = canvas[Y, xs0:xs1]
        al = src_row[:, 3:4]
        rgbn = src_row[:, :3] * shade
        outA = al + dst[:, 3:4] * (1 - al)
        dst[:, :3] = np.where(outA > 0, (rgbn * al + dst[:, :3] * dst[:, 3:4] * (1 - al)) / np.maximum(outA, 1e-4), dst[:, :3])
        dst[:, 3:4] = outA

# ---------- 침엽수 가지(왼쪽 위 칸: x 0..Q, y 0..Q, PIL 좌표 위가 0) ----------
fd, fa = load('fir_tree_01__twig')
fd = normalize(fd, fa)
# 전나무 잔가지 4개(줄기가 아래, 끝이 위로 서 있음)
sprays = [sprite(fd, fa, b) for b in [(626, 819, 1320, 1588), (1299, 926, 1961, 1700), (1336, 82, 1921, 755), (387, 95, 878, 638)]]
region = (0, 0, Q, Q)
cy = Q * 0.5
# 줄기를 따라 잔가지를 양옆으로 번갈아 붙임(앞쪽으로 비스듬히), 뒤쪽 층은 어둡게 먼저
for layer in range(2):
    t = 0.03
    k = 0
    while t < 0.9:
        prof = math.sin(math.pi * (0.2 + 0.8 * t)) * (1 - 0.3 * t)   # 가운데 넓고 끝이 좁음
        side = 1 if k % 2 == 0 else -1
        spr = sprays[rng.randrange(len(sprays))]
        size = Q * (0.52 * prof + 0.12) / max(spr[0].shape[:2])
        if layer == 0: size *= 1.08
        # 잔가지 끝 방향: 줄기(+x)에서 앞으로 35~60° 옆으로
        ang = (40 + 20 * rng.random()) * side
        rad = math.radians(ang)
        # 스프라이트의 위(-y) 방향이 끝 → 회전각: PIL rotate는 반시계. 위(0,-1)를 (cos a, -sin a*side)로
        rot = -90 + ang                      # 위쪽을 오른쪽(+x)으로 눕힌 뒤 ang만큼 옆으로
        reach = size * max(spr[0].shape[:2]) * 0.42
        px = Q * (0.04 + 0.9 * t) + math.cos(rad) * reach
        py = cy - math.sin(rad) * reach
        shade = (0.62 + 0.1 * rng.random()) if layer == 0 else (0.9 + 0.2 * rng.random()) * (1.06 if side > 0 else 0.94)
        stamp(spr, px, py, size, rot, shade, region)
        t += 0.085 * (0.8 + 0.4 * rng.random()) * (0.7 + 0.5 * prof)
        k += 1
# 끝 잔가지
spr = sprays[0]
size = Q * 0.3 / max(spr[0].shape[:2])
stamp(spr, Q * 0.86, cy, size, -90, 1.0, region)

# ---------- 활엽수 잎 뭉치(오른쪽 아래 칸: x Q..N, y Q..N) ----------
ld, la = load('tree_small_02__leaves')
ld = normalize(ld, la)
# 잎 하나씩 떼어내기: 알파를 깎아 잎자루를 끊고 → 덩어리 번호 → 다시 원래 알파 안에서 부풀림
m = la > 0.5
orig = np.asarray(Image.open(f'{src}/tree_small_02__leaves_diff.jpg').convert('RGB'), np.float32) / 255
core = ndimage.binary_erosion(m, iterations=8)
lab, n = ndimage.label(core)
leaves = []
for i, sl in enumerate(ndimage.find_objects(lab)):
    comp = lab[sl] == i + 1
    area = comp.sum()
    if area < 2500: continue
    pad = 12
    y0, x0 = max(0, sl[0].start - pad), max(0, sl[1].start - pad)
    y1, x1 = min(la.shape[0], sl[0].stop + pad), min(la.shape[1], sl[1].stop + pad)
    full = np.zeros(la.shape, bool); full[sl] = comp
    grown = ndimage.binary_dilation(full, iterations=4)[y0:y1, x0:x1] & m[y0:y1, x0:x1]
    # 초록 잎만: 원본 색이 뚜렷한 초록(회색 쐐기·노란 조각 제외), 꽉 찬 모양(가지 조각 제외)
    o = orig[y0:y1, x0:x1][grown]
    green = float((o[:, 1] - np.maximum(o[:, 0], o[:, 2])).mean())
    solidity = grown.sum() / float(grown.shape[0] * grown.shape[1])
    if green < 0.06 or solidity < 0.42: continue
    a = ndimage.gaussian_filter(np.where(grown, la[y0:y1, x0:x1], 0).astype(np.float32), 0.6)
    leaves.append((ld[y0:y1, x0:x1], a))
print('leaves', len(leaves))
# 잎 방향: 원본 잎은 대략 가로로 누워 있음 → 긴 축 기준으로 회전
region = (Q, Q, N, N)
center = (Q + Q * 0.5, Q + Q * 0.5)
radius = Q * 0.37
items = []
for i in range(260):
    ang = rng.random() * math.pi * 2
    rr = rng.random() ** 0.6 * radius
    pos = (center[0] + math.cos(ang) * rr, center[1] + math.sin(ang) * rr)
    depth = rng.random() * 0.6 + rr / radius * 0.4
    items.append((depth, pos, ang))
items.sort()
light = (-0.45, -0.9)   # 위·왼쪽(PIL y 아래가 +)
for depth, pos, ang in items:
    spr = leaves[rng.randrange(len(leaves))]
    h, w = spr[1].shape
    size = Q * (0.10 + 0.05 * rng.random()) / max(h, w)
    off = ((pos[0] - center[0]) / radius, (pos[1] - center[1]) / radius)
    lt = (off[0] * light[0] + off[1] * light[1]) * 0.5 + 0.5
    shade = (0.55 + 0.5 * (lt * 0.65 + depth * 0.35)) * (0.9 + 0.2 * rng.random())
    rot = -math.degrees(ang) + (rng.random() - 0.5) * 90   # 바깥을 향하게 + 흔들림
    stamp(spr, pos[0], pos[1], size, rot, shade, region)

# ---------- 원거리 침엽수 실루엣(왼쪽 아래 칸의 오른쪽 절반: x Q/2..Q, y Q..N) ----------
# 줄기 + 층층이 처진 잔가지로 나무 한 그루 모양 → 멀리서 십자 카드 2장으로 그림(원뿔 덩어리 대신)
region = (Q // 2, Q, Q, N)
rx0, ry0, rx1, ry1 = region
cxm = (rx0 + rx1) / 2
top, bottom = ry0 + 20, ry1 - 30
for layer in range(2):
    y = top + 40
    while y < bottom - 80:
        t = (y - top) / (bottom - top)              # 0 = 꼭대기
        half = (rx1 - rx0) * 0.47 * (0.08 + 0.92 * t ** 0.9)
        for side in (-1, 1):
            for j in range(2):
                spr = sprays[rng.randrange(len(sprays))]
                L = half * (0.75 + 0.35 * rng.random())
                size = L * 1.15 / max(spr[0].shape[:2])
                ang = (-18 - 22 * rng.random()) if side > 0 else (180 + 18 + 22 * rng.random())
                rad = math.radians(ang)
                px = cxm + math.cos(rad) * L * 0.5
                py = y - math.sin(rad) * L * 0.5 + rng.random() * 12
                shade = (0.55 + 0.1 * rng.random()) if layer == 0 else (0.85 + 0.25 * rng.random()) * (1.08 if side < 0 else 0.92) * (0.9 + 0.2 * (1 - t))
                stamp(spr, px, py, size, ang - 90, shade, region)
        y += (18 + 26 * t) * (0.8 + 0.4 * rng.random())
# 꼭대기 순
stamp(sprays[3], cxm, top + 60, 120 / max(sprays[3][0].shape[:2]), 0, 1.0, region)
# 줄기(아래 짧게 보임)
for yy in range(bottom - 120, ry1 - 4):
    for xx in range(int(cxm - 7), int(cxm + 7)):
        canvas[yy, xx] = (0.42, 0.36, 0.30, 1.0)
# 잎 아닌 칸: 흰색 꽉 찬 칸(정점 색만 쓰는 면)
canvas[N - 410:N, 0:410] = (1, 1, 1, 1)

# ---------- 투명 픽셀 색 번짐(밉맵에서 테두리가 검게 되지 않게) ----------
a = canvas[..., 3]
rgb = canvas[..., :3] * (a[..., None] > 0.02)
wsum = (a > 0.02).astype(np.float32)
fill = rgb.copy(); fw = wsum.copy()
for s in (2, 6, 16, 40):
    br = np.dstack([ndimage.gaussian_filter(rgb[..., c], s) for c in range(3)])
    bw = ndimage.gaussian_filter(wsum, s)
    need = fw < 0.5
    fill[need] = br[need] / np.maximum(bw[need, None], 1e-4)
    fw = np.maximum(fw, (bw > 1e-3).astype(np.float32))
canvas[..., :3] = np.where(a[..., None] > 0.02, canvas[..., :3], fill)
Image.fromarray((np.clip(canvas, 0, 1) * 255).astype(np.uint8), 'RGBA').save(out)
print('saved', out)
