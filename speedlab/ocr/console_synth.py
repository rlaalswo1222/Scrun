"""계기판 전체 그림 합성(계획 0042): 칸 찾기·이름 붙이기(field.py) 흐름 시험용. 점수는 정확도가 아니다.

칸 4–6개(거리·시간 + 경사·속도·칼로리·심박·페이스 중 몇 개)를 격자로 놓고, 라벨(한·영, 칸 위·아래·왼쪽)을 붙인다.
헷갈리는 경우를 일부러 넣는다: 시간을 점으로("28.40", 10/29 기종), 페이스는 콜론("6:30"), 속도 라벨 km/h,
라벨이 아이콘뿐(글자 없음)인 계기판. 글꼴은 맥 기본(AppleSDGothicNeo), 다른 곳에선 네 번째 인자로.

    python console_synth.py data/console_synth 200 1     → 그림 + truth.json
    python console_synth.py data/console_synth 200 1 C:/Windows/Fonts/malgun.ttf   (Windows)
"""
import json
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

from synth7seg import DIGIT_SEGS, LED_COLORS, seg_polys

FONT = "/System/Library/Fonts/AppleSDGothicNeo.ttc"

LABELS = {  # 종류 → 라벨 후보(한·영). 단위가 라벨에 붙기도 함
    "distance": ["거리", "거리 km", "DISTANCE", "DIST", "DIST km", "km"],
    "time": ["시간", "운동시간", "TIME", "TIME min"],
    "incline": ["경사도", "경사", "INCLINE", "ELEV %"],
    "speed": ["속도", "속도 km/h", "SPEED", "km/h"],
    "calories": ["칼로리", "칼로리 kcal", "CALORIES", "kcal"],
    "pulse": ["심박수", "PULSE", "HR bpm"],
    "pace": ["페이스", "PACE", "PACE /km"],
}


def fmt_time(rng, s: int) -> str:
    m, sec = divmod(s, 60)
    if m >= 100 or (m >= 60 and rng.random() < 0.6):
        h, m = divmod(m, 60)
        return f"{h}:{m:02d}:{sec:02d}"
    return f"{m}{'.' if rng.random() < 0.35 else ':'}{sec:02d}"   # 35%는 점(10/29 기종처럼)


def field_values(rng):
    """(종류, 보이는 글자) 목록과 정답."""
    dist_m = int(rng.integers(200, 21000))
    dur_s = int(dist_m / rng.uniform(1.4, 4.2))             # 5–15 km/h
    fields = [("distance", f"{dist_m / 1000:.2f}" if rng.random() < 0.85 else f"{dist_m / 1000:.1f}"),
              ("time", fmt_time(rng, dur_s))]
    others = {"incline": f"{rng.uniform(0, 12):.1f}", "speed": f"{rng.uniform(4, 16):.1f}",
              "calories": str(int(dist_m * rng.uniform(0.05, 0.08))), "pulse": str(int(rng.integers(90, 180))),
              "pace": f"{int(rng.integers(3, 12))}:{int(rng.integers(0, 60)):02d}"}
    extra = rng.choice(list(others), size=int(rng.integers(2, 5)), replace=False)
    fields += [(k, others[k]) for k in extra]
    rng.shuffle(fields)
    km = float(dict(fields)["distance"])   # 0.1 단위로 보이면 정답도 그 단위(보이는 값이 정답)
    return fields, {"distance_m": int(round(km * 1000)), "duration_s": dur_s}


def draw_digits(img, text, x, y, ch, color, rng):
    """7세그먼트 글자를 (x, y) 왼쪽 위부터. 그린 폭을 돌려줌."""
    cw, t = int(ch * 0.55), max(3.0, ch * 0.13)
    gap, slant = int(cw * 0.35), 0.08
    lit = np.zeros(img.shape[:2], np.uint8)
    cx = float(x)
    chars = []
    for c in text:
        if c in ".:" and chars:
            chars[-1][1] += c
        else:
            chars.append([c, ""])
    for c, after in chars:
        polys = seg_polys(cx, y, cw, ch, t, slant)
        for name in DIGIT_SEGS.get(c, ""):
            cv2.fillPoly(lit, [np.round(np.array(polys[name])).astype(np.int32)], 255)
        r = int(round(t * 0.6))
        if "." in after:
            cv2.circle(lit, (int(cx + cw + gap * 0.45), int(y + ch - r)), r, 255, -1)
        step = cw + gap
        if ":" in after:
            c0 = cx + cw + gap * 0.5 + r * 2
            for cy in (y + ch * 0.3, y + ch * 0.7):
                cv2.circle(lit, (int(c0 + slant * (y + ch - cy)), int(cy)), r, 255, -1)
            step += r * 4
        cx += step
    a = cv2.GaussianBlur(lit.astype(np.float32) / 255, (0, 0), t * 0.3)[..., None]
    img[:] = img * (1 - a) + np.array(color, np.float32) * a
    return int(cx - x)


def render(rng, font_path=FONT):
    fields, truth = field_values(rng)
    cols = 3 if len(fields) > 4 else int(rng.choice([2, 4]))
    rows = (len(fields) + cols - 1) // cols
    where = str(rng.choice(["above", "above", "below", "left"]))
    cell_w, cell_h = (470 if where == "left" else 320), 200   # 왼쪽 라벨은 숫자 앞 자리(라벨 폭 170)
    W, H = cols * cell_w + 80, rows * cell_h + 80
    img = np.ones((H, W, 3), np.float32) * rng.uniform(10, 40)
    color = np.array(LED_COLORS[int(rng.integers(len(LED_COLORS)))], np.float32)
    icons_only = rng.random() < 0.12                         # 라벨 글자가 없는 계기판(아이콘뿐)
    labels = []
    ch = int(rng.integers(55, 85))
    for i, (kind, text) in enumerate(fields):
        r, c = divmod(i, cols)
        x0, y0 = 40 + c * cell_w, 40 + r * cell_h
        dx = x0 + (170 if where == "left" else 30)
        dy = y0 + (90 if where == "above" else 30 if where == "below" else 60)
        n = sum(c.isdigit() or c == "-" for c in text)
        fit = (cell_w - 70) / (n * 0.75 + 0.35 * text.count(":"))   # 칸 폭에 맞게(숫자 폭 0.55 + 틈 0.19)
        draw_digits(img, text, dx, dy + max(0, ch - int(min(ch, fit))), int(min(ch, fit)), color, rng)
        if icons_only:
            continue
        lab = str(rng.choice(LABELS[kind]))
        lx, ly = (x0 + 30, y0 + 40) if where == "above" else (x0 + 30, dy + ch + 18) if where == "below" else (x0 + 5, dy + ch // 3)
        labels.append((lab, lx, ly))
    pil = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)[..., ::-1])
    d = ImageDraw.Draw(pil)
    font = ImageFont.truetype(font_path, int(rng.integers(20, 30)))
    lab_color = tuple(int(v) for v in rng.uniform(170, 230, 3))
    for lab, lx, ly in labels:
        d.text((lx, ly), lab, font=font, fill=lab_color)
    img = np.array(pil)[..., ::-1].astype(np.float32)
    img = _photo(rng, img)
    return img, {**truth, "fields": [list(f) for f in fields], "icons_only": icons_only, "label_pos": where}


def _photo(rng, img):
    """사진처럼: 약한 원근·기울기, 반사, 흐림, 잡음, JPEG."""
    H, W = img.shape[:2]
    j = 0.04
    src = np.float32([[0, 0], [W, 0], [W, H], [0, H]])
    dst = (src + rng.uniform(-j, j, (4, 2)) * [W, H]).astype(np.float32)
    M = cv2.getPerspectiveTransform(src, dst)
    pad = int(0.08 * W)
    M[0, 2] += pad
    M[1, 2] += pad
    img = cv2.warpPerspective(img, M, (W + 2 * pad, H + 2 * pad), borderValue=tuple(rng.uniform(40, 90, 3).tolist()))
    if rng.random() < 0.4:   # 유리 반사(흰 띠)
        h2, w2 = img.shape[:2]
        band = np.zeros((h2, w2), np.float32)
        x = int(rng.integers(0, w2))
        cv2.line(band, (x, 0), (x + int(rng.integers(-200, 200)), h2), 1.0, int(rng.integers(30, 120)))
        img += cv2.GaussianBlur(band, (0, 0), 25)[..., None] * rng.uniform(30, 90)
    if rng.random() < 0.5:
        img = cv2.GaussianBlur(img, (0, 0), rng.uniform(0.5, 1.6))
    img += rng.normal(0, rng.uniform(2, 8), img.shape)
    img = np.clip(img, 0, 255).astype(np.uint8)
    q = int(rng.integers(55, 90))
    return cv2.imdecode(cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, q])[1], cv2.IMREAD_COLOR)


def write(out: Path, n: int, seed: int, font_path=FONT):
    out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(seed)
    truth = {}
    for i in range(n):
        img, t = render(rng, font_path)
        name = f"console_{i:04d}.jpg"
        cv2.imwrite(str(out / name), img)
        truth[name] = t
    (out / "truth.json").write_text(json.dumps(truth, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    write(Path(sys.argv[1]), int(sys.argv[2]), int(sys.argv[3]), *sys.argv[4:5])
