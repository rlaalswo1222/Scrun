"""7세그먼트 계기판 칸 합성(계획 0038, ADR-0012). 10/29 시험(계획 초안 0011)에서 실패를 줄였던 특징을 다시 넣는다.

한 장 = 계기판 숫자 칸 하나(거리·시간 등). 글자마다 상자(0–9, '-', '.', ':')를 단다. 상자를 달지 않는 것:
- 꺼진 획이 비치는 "88"(빈 자리), 숫자 아닌 글자(P·E·H·L·o·t·S·C·F), 칸 밖 단위 글자(km·km/h·min·kcal)
보이는 모습: 색(빨강·주황·초록·파랑·흰색 LED, LCD), 번짐, 과노출(가운데가 하얗게), 기울기, 원근·회전, 흐림, 잡음, JPEG, 작게 찍힘.
사용: python synth7seg.py <출력 폴더> <장 수> [시드]  → images/*.jpg + annotations.json(COCO)
"""
import json
import math
import sys
from pathlib import Path

import cv2
import numpy as np

CLASSES = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "-", ".", ":"]
DIGIT_SEGS = {"0": "abcdef", "1": "bc", "2": "abdeg", "3": "abcdg", "4": "bcfg", "5": "acdfg", "6": "acdefg",
              "7": "abc", "8": "abcdefg", "9": "abcdfg", "-": "g"}
# 숫자와 모양이 같은 글자는 넣지 않음('S' = 5, 'O' = 0, 'b' = 6, 'g' = 9 — 상자 없는 글자로 그리면 숫자를 안 읽게 배움, 11/17)
LETTER_SEGS = {"P": "abefg", "E": "adefg", "H": "bcefg", "L": "def", "o": "cdeg", "t": "defg",
               "C": "adef", "F": "aefg", "r": "eg", "n": "ceg"}
UNITS = ["km", "km/h", "min", "kcal", "KM", "TIME", "DIST", "m", "bpm", "%"]
LED_COLORS = [(40, 40, 255), (30, 110, 255), (60, 230, 60), (255, 160, 40), (235, 235, 235), (20, 200, 255)]  # BGR


def seg_polys(x, y, w, h, t, slant):
    """칸 (x, y, w, h) 안 7획의 다각형. slant = 위로 갈수록 오른쪽으로 밀리는 비율."""
    def P(px, py):  # 기울임: 아래(py=h)는 그대로, 위로 갈수록 오른쪽
        return (x + px + slant * (h - py), y + py)

    m = h / 2
    g = t * 0.12  # 획 사이 틈
    def hbar(cy, x0, x1):
        return [P(x0 + g, cy), P(x0 + t / 2 + g, cy - t / 2), P(x1 - t / 2 - g, cy - t / 2), P(x1 - g, cy),
                P(x1 - t / 2 - g, cy + t / 2), P(x0 + t / 2 + g, cy + t / 2)]

    def vbar(cx, y0, y1):
        return [P(cx, y0 + g), P(cx + t / 2, y0 + t / 2 + g), P(cx + t / 2, y1 - t / 2 - g), P(cx, y1 - g),
                P(cx - t / 2, y1 - t / 2 - g), P(cx - t / 2, y0 + t / 2 + g)]

    l, r = t / 2, w - t / 2
    return {"a": hbar(t / 2, l, r), "g": hbar(m, l, r), "d": hbar(h - t / 2, l, r),
            "f": vbar(l, t / 2, m), "b": vbar(r, t / 2, m), "e": vbar(l, m, h - t / 2), "c": vbar(r, m, h - t / 2)}


def random_text(rng):
    """칸에 뜰 문자열과 자리 수. 거리(0.13, 3.12, 12.45), 시간(18:30, 1.00, 45:07), 정수(kcal 132), 빈 자리, 글자."""
    kind = rng.choice(["dist", "time_colon", "time_dot", "int", "neg", "letters"], p=[.3, .22, .18, .18, .04, .08])
    if kind == "dist":
        v = rng.choice([rng.uniform(0, 1), rng.uniform(1, 10), rng.uniform(10, 42)])
        s = f"{v:.{rng.choice([1, 2, 2, 3])}f}"
    elif kind in ("time_colon", "time_dot"):
        mm, ss = int(rng.integers(0, 100)), int(rng.integers(0, 60))
        sep = ":" if kind == "time_colon" else "."
        s = f"{mm}{sep}{ss:02d}" if rng.random() < .7 else f"{int(rng.integers(0, 3))}{sep}{mm % 60:02d}{sep}{ss:02d}"
    elif kind == "int":
        s = str(int(rng.choice([rng.integers(0, 10), rng.integers(10, 1000), rng.integers(100, 10000)])))
    elif kind == "neg":
        s = "-" * int(rng.integers(1, 5))
    else:
        s = "".join(rng.choice(list(LETTER_SEGS), size=int(rng.integers(2, 5))))
    cells = len(s.replace(".", "").replace(":", ""))
    return s, cells + int(rng.choice([0, 0, 1, 2]))  # 앞쪽 빈 자리(꺼진 88)


def render(rng, out_h=None):
    """칸 하나를 그려 (BGR 이미지, [(클래스, x0, y0, x1, y1)])."""
    text, ncells = random_text(rng)
    lcd = rng.random() < 0.15
    ch = int(rng.integers(60, 140))                     # 숫자 높이(px, 그린 뒤 줄임)
    cw = int(ch * rng.uniform(0.45, 0.65))
    t = max(3.0, ch * rng.uniform(0.09, 0.16))
    slant = rng.uniform(0, 0.15) if rng.random() < 0.7 else 0.0
    gap = int(cw * rng.uniform(0.25, 0.55))
    margin = int(ch * rng.uniform(0.25, 0.8))
    W = margin * 2 + ncells * cw + (ncells - 1) * gap + int(slant * ch) + int(t * 3 * text.count(":")) + 60
    H = ch + margin * 2

    if lcd:
        bg = np.array(rng.uniform([120, 140, 120], [200, 215, 200]))
        on = np.array(rng.uniform(10, 50, 3))
    else:
        bg = np.array(rng.uniform(0, 35, 3))
        on = np.array(LED_COLORS[int(rng.integers(len(LED_COLORS)))], float) * rng.uniform(0.7, 1.0)
    ghost = rng.choice([0.0, rng.uniform(0.03, 0.12), rng.uniform(0.12, 0.35)], p=[.25, .45, .3])

    lit = np.zeros((H, W), np.float32)    # 켜진 획 세기
    off = np.zeros((H, W), np.float32)    # 꺼진 획(비침)
    boxes = []

    # 문자열을 칸에 배치: 숫자·글자는 칸 하나, '.'은 앞 칸 오른쪽 아래, ':'은 칸 사이
    chars = []
    for c in text:
        if c in ".:" and chars:
            chars[-1][1] += c
        else:
            chars.append([c, ""])
    chars = [["", ""]] * (ncells - len(chars)) + chars   # 앞쪽 빈 자리
    x = float(margin)
    for c, after in chars:
        polys = seg_polys(x, margin, cw, ch, t, slant)
        segs = DIGIT_SEGS.get(c, LETTER_SEGS.get(c, ""))
        for name, poly in polys.items():
            pts = np.round(np.array(poly)).astype(np.int32)
            cv2.fillPoly(lit if name in segs else off, [pts], 1.0)
        if c in DIGIT_SEGS:
            pts = np.concatenate([np.array(polys[s]) for s in segs])
            boxes.append((CLASSES.index(c), *pts.min(0), *pts.max(0)))
        dot_r = t * rng.uniform(0.5, 0.75)
        dx = x + cw + gap * 0.45
        if "." in after:
            cx, cy = dx, margin + ch - dot_r
            cv2.circle(lit, (int(cx), int(cy)), int(round(dot_r)), 1.0, -1)
            boxes.append((CLASSES.index("."), cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r))
        elif rng.random() < 0.5:  # 꺼진 소수점도 비침
            cv2.circle(off, (int(dx), int(margin + ch - dot_r)), int(round(dot_r)), 1.0, -1)
        step = cw + gap
        if ":" in after:  # 콜론은 칸 사이 넓힌 자리 가운데
            cx0 = x + cw + gap * 0.5 + dot_r * 2
            ys = [margin + ch * 0.3, margin + ch * 0.7]
            for cy in ys:
                cv2.circle(lit, (int(cx0 + slant * (margin + ch - cy)), int(cy)), int(round(dot_r)), 1.0, -1)
            bx = cx0 + slant * ch * 0.5
            boxes.append((CLASSES.index(":"), bx - dot_r * 1.5, ys[0] - dot_r * 1.5, bx + dot_r * 1.5, ys[1] + dot_r * 1.5))
            step += dot_r * 4
        x += step

    # 색칠: 번짐(LED)과 과노출(가운데가 하얗게 탐)
    img = np.ones((H, W, 3), np.float32) * bg
    if lcd:
        img = img * (1 - lit[..., None]) + on * lit[..., None]
        img = img * (1 - ghost * off[..., None]) + on * ghost * off[..., None]
    else:
        glow = cv2.GaussianBlur(lit, (0, 0), t * rng.uniform(0.5, 2.0)) * rng.uniform(0.2, 0.9)
        light = lit + glow + off * ghost
        img = img + on * light[..., None]
        burn = rng.uniform(0, 1.0)            # 0: 색 그대로, 1: 켜진 획 가운데가 흰색
        core = cv2.GaussianBlur(lit, (0, 0), max(t * 0.15, 0.5))
        img = img + burn * 255 * (core ** 2)[..., None]
    # 칸 밖 단위 글자(상자 없음)
    if rng.random() < 0.5:
        unit = str(rng.choice(UNITS))
        scale = ch / 90 * rng.uniform(0.5, 0.9)
        org = (int(min(x, W - 60)), int(margin + ch * rng.uniform(0.4, 1.0)))
        mask = np.zeros((H, W), np.uint8)  # OpenCV 5는 8비트에만 글자를 그림
        cv2.putText(mask, unit, org, cv2.FONT_HERSHEY_SIMPLEX, scale, 255, max(1, int(scale * 2)))
        a = (mask.astype(np.float32) / 255)[..., None]
        img = img * (1 - a) + on * a
    img = np.clip(img, 0, 255)

    img, boxes = _geometry(rng, img, boxes)
    img = _degrade(rng, img)
    if out_h:
        s = out_h / img.shape[0]
        img = cv2.resize(img, (max(1, int(img.shape[1] * s)), out_h), interpolation=cv2.INTER_AREA)
        boxes = [(c, x0 * s, y0 * s, x1 * s, y1 * s) for c, x0, y0, x1, y1 in boxes]
    return img, boxes, text


def _geometry(rng, img, boxes):
    """작은 원근·회전. 상자는 네 모서리를 옮겨 다시 감쌈."""
    H, W = img.shape[:2]
    d = rng.uniform(0, 0.08) * min(H, W)
    src = np.float32([[0, 0], [W, 0], [W, H], [0, H]])
    dst = src + rng.uniform(-d, d, (4, 2)).astype(np.float32)
    ang = math.radians(rng.uniform(-4, 4))
    rot = np.array([[math.cos(ang), -math.sin(ang)], [math.sin(ang), math.cos(ang)]], np.float32)
    dst = (dst - [W / 2, H / 2]) @ rot.T + [W / 2, H / 2]
    dst -= dst.min(0)
    M = cv2.getPerspectiveTransform(src, dst.astype(np.float32))
    size = (int(dst[:, 0].max()) + 1, int(dst[:, 1].max()) + 1)
    border = img[0, 0].tolist()
    out = cv2.warpPerspective(img, M, size, borderMode=cv2.BORDER_CONSTANT, borderValue=border)
    nb = []
    for c, x0, y0, x1, y1 in boxes:
        pts = np.float32([[[x0, y0], [x1, y0], [x1, y1], [x0, y1]]])
        p = cv2.perspectiveTransform(pts, M)[0]
        nb.append((c, *p.min(0), *p.max(0)))
    return out, nb


def _degrade(rng, img):
    """초점·손떨림 흐림, 잡음, 밝기, 작게 찍힌 뒤 늘림, JPEG."""
    if rng.random() < 0.6:
        img = cv2.GaussianBlur(img, (0, 0), rng.uniform(0.3, 2.0))
    if rng.random() < 0.25:
        k = int(rng.integers(3, 9))
        kern = np.zeros((k, k), np.float32)
        kern[k // 2, :] = 1 / k
        M = cv2.getRotationMatrix2D((k / 2, k / 2), rng.uniform(0, 180), 1)
        img = cv2.filter2D(img, -1, cv2.warpAffine(kern, M, (k, k)))
    img = img * rng.uniform(0.6, 1.3) + rng.uniform(-20, 20)
    img = img + rng.normal(0, rng.uniform(0, 10), img.shape)
    img = np.clip(img, 0, 255).astype(np.uint8)
    if rng.random() < 0.4:  # 멀리서 작게 찍힘(숫자 높이 15–30 px)
        H, W = img.shape[:2]
        s = rng.uniform(15, 30) / (H * 0.6)
        small = cv2.resize(img, (max(8, int(W * s)), max(8, int(H * s))), interpolation=cv2.INTER_AREA)
        img = cv2.resize(small, (W, H), interpolation=cv2.INTER_LINEAR)
    q = int(rng.integers(30, 96))
    return cv2.imdecode(cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, q])[1], cv2.IMREAD_COLOR)


def write_coco(out: Path, n: int, seed: int) -> None:
    rng = np.random.default_rng(seed)
    (out / "images").mkdir(parents=True, exist_ok=True)
    images, anns = [], []
    for i in range(n):
        img, boxes, text = render(rng, out_h=int(rng.integers(80, 200)))
        name = f"synth_{seed}_{i:05d}.jpg"
        cv2.imwrite(str(out / "images" / name), img)
        h, w = img.shape[:2]
        images.append({"id": i, "file_name": name, "width": w, "height": h, "text": text})
        for c, x0, y0, x1, y1 in boxes:
            x0, y0, x1, y1 = max(0, x0), max(0, y0), min(w, x1), min(h, y1)
            if x1 - x0 < 1 or y1 - y0 < 1:
                continue
            anns.append({"id": len(anns), "image_id": i, "category_id": int(c), "bbox": [float(x0), float(y0), float(x1 - x0), float(y1 - y0)],
                         "area": float((x1 - x0) * (y1 - y0)), "iscrowd": 0})
    cats = [{"id": i, "name": c} for i, c in enumerate(CLASSES)]
    (out / "annotations.json").write_text(json.dumps({"images": images, "annotations": anns, "categories": cats}))


if __name__ == "__main__":
    write_coco(Path(sys.argv[1]), int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 0)
