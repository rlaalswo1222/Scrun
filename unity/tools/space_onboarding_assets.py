"""계획 0061: Figma "S · 게임 UI 전체 시안"(우주 온보딩) 레이어 → 앱 그림(UI/Images/Space/).

실행: .venv\\Scripts\\python unity/tools/space_onboarding_assets.py <onboarding.json> <layers.json> [--check <폴더>]
입력(Figma 플러그인 API로 내려받은 것, docs/claude_howto.md "Figma"):
  onboarding.json  {frames: {"A-02": [레이어 한 줄씩]}, shots, images}  — 별 위치(배경은 8개 화면이 같음)
  layers.json      {layers: [{frame, id, name, x, y, w, h, png(3배)}], extra: {icons, kakao}}
UI Toolkit은 그라데이션·흐림·타원 테두리를 못 그려서 배경·궤도 고리·빛은 여기서 그림으로 만든다.
글자·버튼·카드는 UXML(OnboardingSpace.uss)이 그리므로 그림에 넣지 않는다.
--check 폴더: 배경 + 장면을 Figma 화면 위치에 놓은 미리보기(shot과 나란히) → 맞는지 눈으로 확인
"""
import base64
import io
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'unity/TestRun/Assets/Scrun/App/UI/Images/Space'
S = 3                      # Figma 3배로 받음
W, H = 393, 852            # 세로 화면(pt)
BG_TOP, BG_BOTTOM = (0x0f, 0x2a, 0x33), (0x16, 0x21, 0x3f)
# Figma Template · 세로 배경(880:49)의 흐린 빛: (x, y, w, h, 색, 불투명도, 흐림)
GLOWS = [(-60, 240, 520, 420, '#6fe3ff', 0.06, 110), (180, 40, 300, 260, '#7a5cf0', 0.12, 110),
         (-100, 600, 360, 300, '#16213f', 1.0, 90), (140, 640, 360, 300, '#16213f', 1.0, 90)]
SHOOTING = ((250, 184), (320, 150), 1.2)   # 별똥별 선(시작, 끝, 굵기)
# 화면별 장면: 그림 이름 → 그 화면에서 쓸 레이어 이름(앞부분, Figma 순서대로 겹침)
SCENES = {
    'IntroRun': ('A-02', ['Stage glow', 'Orbit ring', 'Orbit ring inner', 'Hero', 'orbit planet']),
    'IntroTogether': ('A-03', ['Stage glow', 'Orbit ring', 'Orbit ring inner', 'orbit planet', 'Ghost', 'Friend',
                               'Runner', 'Me', 'Tag']),
    'IntroRecordStage': ('A-04', ['Stage glow', 'Orbit ring', 'Orbit ring inner', 'orbit planet']),
    'GoalFlag': ('A-04', ['Goal flag']),
    'LoginLogo': ('A-05', ['Logo']),
    'LoginPlanet': ('A-05', ['Hero']),
    'CalibHero': ('A-11', ['Stage glow', 'Hero']),
    'DoneScene': ('A-17', ['Rays', 'Burst glow', 'Pedestal', 'Pedestal top', 'Character']),
}
RAW = {'8eeb8a7c': 'PlanetSky'}    # 원본 그림(해시 앞 8자리) → 이름
ICONS = ['PermMotion', 'PermHeadphones', 'PermCamera', 'PermBell']   # A-07 순서


def hexrgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def png(b64):
    return Image.open(io.BytesIO(base64.b64decode(b64))).convert('RGBA')


def sparkle(draw, x, y, size):
    """Figma sparkle 벡터(12×12, 네 꼭짓점 별, 곡선은 꺾은선으로)"""
    k = size / 12 * S
    pts = [(6, 0), (6.8, 3.2), (8.8, 5.2), (12, 6), (8.8, 6.8), (6.8, 8.8), (6, 12), (5.2, 8.8), (3.2, 6.8), (0, 6),
           (3.2, 5.2), (5.2, 3.2)]
    draw.polygon([(x * S + px * k, y * S + py * k) for px, py in pts], fill=(255, 255, 255, 255))


def background(frame_lines):
    img = Image.new('RGBA', (W * S, H * S))
    top, bot = BG_TOP, BG_BOTTOM
    grad = Image.linear_gradient('L').resize((W * S, H * S))      # 위 0 → 아래 255
    img = Image.composite(Image.new('RGBA', img.size, bot + (255,)), Image.new('RGBA', img.size, top + (255,)), grad)
    for x, y, w, h, col, op, blur in GLOWS:
        layer = Image.new('RGBA', img.size, hexrgb(col) + (0,))
        m = Image.new('L', img.size, 0)
        ImageDraw.Draw(m).ellipse([x * S, y * S, (x + w) * S, (y + h) * S], fill=int(255 * op))
        layer.putalpha(m.filter(ImageFilter.GaussianBlur(blur / 2 * S)))
        img = Image.alpha_composite(img, layer)
    stars = Image.new('RGBA', img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(stars)
    for l in frame_lines:
        m = re.search(r'(EL star|VE sparkle) \[(-?\d+),(-?\d+) (\d+)x(\d+)\](?:.* o=([\d.]+))?', l)
        if not m:
            continue
        kind, x, y, w, h = m.group(1), *map(int, m.group(2, 3, 4, 5))
        a = int(255 * float(m.group(6) or 1))
        if kind == 'EL star':
            d.ellipse([x * S, y * S, (x + w) * S, (y + h) * S], fill=(255, 255, 255, a))
        else:
            sparkle(d, x, y, w)
    (x0, y0), (x1, y1), sw = SHOOTING
    d.line([x0 * S, y0 * S, x1 * S, y1 * S], fill=(255, 255, 255, 230), width=round(sw * S))
    return Image.alpha_composite(img, stars)


def screen_over(bg, img, box):
    """SCREEN 혼합 결과 = 1 − (1 − 그림)(1 − 배경). 알파(둥근 흐림 마스크)는 그대로 둬 가장자리가 배경으로 이어짐"""
    from PIL import ImageChops
    x, y = round(box[0] * S), round(box[1] * S)
    under = bg.crop((x, y, x + img.width, y + img.height)).convert('RGB')
    mixed = ImageChops.screen(img.convert('RGB'), under)
    mixed.putalpha(img.getchannel('A'))
    return mixed


def scene(layers, frame, names):
    picked = [l for l in layers if l['frame'] == frame and any(l['name'].startswith(n) for n in names)]
    picked.sort(key=lambda l: l['order'])
    x0, y0 = min(l['x'] for l in picked), min(l['y'] for l in picked)
    x1, y1 = max(l['x'] + l['w'] for l in picked), max(l['y'] + l['h'] for l in picked)
    can = Image.new('RGBA', (round((x1 - x0) * S), round((y1 - y0) * S)), (0, 0, 0, 0))
    for l in picked:
        im = png(l['png'])
        can.alpha_composite(im, (round((l['x'] - x0) * S), round((l['y'] - y0) * S)))
    return can, (x0, y0, x1 - x0, y1 - y0)


def main():
    onb = json.load(open(sys.argv[1], encoding='utf-8'))
    lay = json.load(open(sys.argv[2], encoding='utf-8'))
    check = Path(sys.argv[sys.argv.index('--check') + 1]) if '--check' in sys.argv else None
    OUT.mkdir(parents=True, exist_ok=True)
    bg = background(onb['frames']['A-02'])
    # 바탕은 무압축(그라데이션 띠 방지)이라 메모리를 아끼려 2배 크기로 저장(부드러운 그라데이션·작은 별이라 3배가 필요 없음)
    bg.convert('RGB').resize((W * 2, H * 2), Image.LANCZOS).save(OUT / 'SpaceBg.png', optimize=True)
    boxes = {}
    for name, (frame, names) in SCENES.items():
        img, box = scene(lay['layers'], frame, names)
        if name == 'LoginLogo':
            # Figma에선 로고 사진이 SCREEN 혼합(어두운 바탕이 배경에 묻힘). UI Toolkit엔 혼합이 없어 그 자리 배경과 미리 섞음
            img = screen_over(bg, img, box)
        img.save(OUT / f'{name}.png', optimize=True)
        boxes[name] = box
        print(f'{name}: x {box[0]:.1f} y {box[1]:.1f} w {box[2]:.1f} h {box[3]:.1f}  ({img.size[0]}×{img.size[1]})')
    for icon, data in zip(ICONS, lay['extra']['icons']):
        png(data['png']).save(OUT / f'{icon}.png', optimize=True)
    png(lay['extra']['kakao']).save(OUT / 'KakaoSymbol.png', optimize=True)
    # A-04 기록 카드 아이콘(행성 원본 768 → 44pt 3배)
    for prefix, name in RAW.items():
        h = next(k for k in onb['images'] if k.startswith(prefix))
        png(onb['images'][h]).resize((44 * S, 44 * S), Image.LANCZOS).save(OUT / f'{name}.png', optimize=True)
    if check:
        check.mkdir(parents=True, exist_ok=True)
        for frame in sorted({f for f, _ in SCENES.values()}):
            pv = bg.copy()
            for name, (f, _) in SCENES.items():
                if f == frame:
                    x, y, _, _ = boxes[name]
                    pv.alpha_composite(Image.open(OUT / f'{name}.png'), (round(x * S), round(y * S)))
            shot = Image.open(io.BytesIO(base64.b64decode(onb['shots'][frame]))).convert('RGBA').resize(pv.size)
            both = Image.new('RGBA', (pv.width * 2 + 30, pv.height), (40, 40, 40, 255))
            both.paste(pv, (0, 0))
            both.paste(shot, (pv.width + 30, 0))
            both.convert('RGB').resize((both.width // 3, both.height // 3)).save(check / f'check_{frame}.png')
    print('→', OUT.relative_to(ROOT))


if __name__ == '__main__':
    main()
