"""계획 0062: Figma "스크런 우주 디자인" > B · 홈, C · 달리기 준비 → 앱 그림(UI/Images/Space/).

실행: .venv\\Scripts\\python unity/tools/space_home_assets.py <bc.json> [--check <폴더>]
입력: bc.json  {frames, shots, images(원본), layers(위쪽 단계 3배 PNG + 위치)} — Figma 플러그인 API로 내려받음(docs/claude_howto.md)
글자·카드·버튼은 UXML·USS(HomeSpace.uss)가 그리므로 그림에 넣지 않는다. 좌표는 Figma 393×852(pt), 그림은 3배(바탕은 2배)
"""
import base64
import io
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'unity/TestRun/Assets/Scrun/App/UI/Images/Space'
S = 3
W, H = 393, 852
TOP, BOTTOM = (0x10, 0x16, 0x4a), (0x35, 0x57, 0xd6)   # B·C 바탕 그라데이션(위 → 아래)
CARD = (0x22, 0x32, 0x59)                              # 카드 바탕 #223259
FACE_CROP = (0.3893, 0.0366, 0.2184, 0.3337)          # 얼굴: 원본(Character / stand)에서 Figma CROP 창(x, y, 폭, 높이 비율)
ZONES = ['8eeb8a7c', 'b2aa3048', '50a02b49', '1f303176', '0a51db19']   # 구간 행성 1–5(구름 위·화성·숲 호수·얼음·달 기지)
DOCK = {'b5e55e82': 'DockHome', '004795a4': 'DockFriends', 'c2ee97eb': 'DockRecords', '9c9c0fee': 'DockMe'}
PLANET, FACE, MAP = 'fb6f8935', '68036ca1', '494c4c34'


def raw(d, prefix):
    h = next(k for k in d['images'] if k.startswith(prefix))
    return Image.open(io.BytesIO(base64.b64decode(d['images'][h]))).convert('RGBA')


def layer(d, frame, name):
    l = next(x for x in d['layers'] if x['frame'] == frame and x['name'].startswith(name))
    return Image.open(io.BytesIO(base64.b64decode(l['png']))).convert('RGBA'), l


def gradient(size, stops):
    """세로 그라데이션: stops = [(y비율, 색)]"""
    w, h = size
    img = Image.new('RGBA', size)
    px = img.load()
    for y in range(h):
        t = y / max(1, h - 1)
        for i in range(len(stops) - 1):
            (t0, c0), (t1, c1) = stops[i], stops[i + 1]
            if t0 <= t <= t1:
                k = 0 if t1 == t0 else (t - t0) / (t1 - t0)
                col = tuple(round(c0[j] + (c1[j] - c0[j]) * k) for j in range(3)) + (255,)
                break
        for x in range(w):
            px[x, y] = col
    return img


def paste_layer(can, d, frame, name):
    im, l = layer(d, frame, name)
    can.alpha_composite(im, (round(l['x'] * S), round(l['y'] * S)))


def place(can, img, x, y, w, h):
    """Figma 위치(pt)에 그림을 그 크기로(FILL = 가운데 맞춰 꽉 채움)"""
    tw, th = round(w * S), round(h * S)
    r = max(tw / img.width, th / img.height)
    im = img.resize((round(img.width * r), round(img.height * r)), Image.LANCZOS)
    ox, oy = (im.width - tw) // 2, (im.height - th) // 2
    can.alpha_composite(im.crop((ox, oy, ox + tw, oy + th)), (round(x * S), round(y * S)))


def rounded(img, r, corners=(True, True, True, True)):
    m = Image.new('L', img.size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, img.width - 1, img.height - 1], radius=round(r * S), fill=255,
                                        corners=corners)
    img.putalpha(ImageChops.multiply(img.getchannel('A'), m))
    return img


def card_crop(img, src_box, card_box, radius, corners=(True, True, True, True), bg=CARD):
    """카드(card_box: x, y, w, h)에 그림(src_box 위치·크기로 놓인)이 보이는 부분 + 카드 바탕, 모서리 둥글게"""
    cx, cy, cw, ch = card_box
    can = Image.new('RGBA', (round(cw * S), round(ch * S)), bg + (255,))
    sx, sy, sw, sh = src_box
    place(can, img, sx - cx, sy - cy, sw, sh)
    return rounded(can, radius, corners)


def save(img, name, rgb=False):
    (img.convert('RGB') if rgb else img).save(OUT / f'{name}.png', optimize=True)
    return img


def main():
    d = json.load(open(sys.argv[1], encoding='utf-8'))
    check = Path(sys.argv[sys.argv.index('--check') + 1]) if '--check' in sys.argv else None
    OUT.mkdir(parents=True, exist_ok=True)

    # 홈·코스 목록 바탕: 그라데이션 + 성운 빛 + 별(B-01). 무압축이라 2배로 저장
    bg = gradient((W * S, H * S), [(0, TOP), (1, BOTTOM)])
    paste_layer(bg, d, 'B-01', 'nebula glow')
    paste_layer(bg, d, 'B-01', 'Stars')
    save(bg.resize((W * 2, H * 2), Image.LANCZOS), 'HomeBg', rgb=True)

    # 코스 상세 바탕: 위 420은 #10164a(맨 위 그림이 이 색으로 흐려짐), 아래는 그라데이션(C-02 base) + 별
    cbg = gradient((W * S, H * S), [(0, TOP), (420 / H, TOP), (1, BOTTOM)])
    paste_layer(cbg, d, 'C-02', 'Stars')
    save(cbg.resize((W * 2, H * 2), Image.LANCZOS), 'CourseDetailBg', rgb=True)

    # 홈 위 카드: 내 행성 렌더(카드 기준 −79, −24 525×525), 키 230(B-01)·150(B-02 목표 없음)
    planet = raw(d, PLANET)
    save(card_crop(planet, (-59, 236, 525, 525), (20, 260, 353, 230), 28), 'HomeHero')
    save(card_crop(planet, (-59, 264, 525, 525), (20, 288, 353, 150), 28), 'HomeHeroShort')

    # 코스 카드 그림(C-01): 우주 맵 렌더(−2,154 397×210)가 카드 위 136에 보임, 위 모서리만 둥글게
    m = raw(d, MAP)
    save(card_crop(m, (-2, 154, 397, 210), (20, 194, 353, 136), 20, corners=(True, True, False, False)), 'CourseThumb')
    # 코스 상세 맨 위(C-02): 렌더(−190,0 767×405) + 아래 200을 #10164a로 흐림, 405–420은 #10164a
    hero = Image.new('RGBA', (W * S, 420 * S), TOP + (255,))
    place(hero, m, -190, 0, 767, 405)
    fade = gradient((W * S, 200 * S), [(0, TOP), (1, TOP)])
    # Figma 흐림 막대(220–420)는 위에서부터 빨리 어두워짐 → 투명도를 0.6제곱 곡선으로(선형이면 아래에서 갑자기 끊겨 보임)
    fade.putalpha(Image.linear_gradient('L').resize((W * S, 200 * S)).point(lambda v: round(255 * (v / 255) ** 0.6)))
    hero.alpha_composite(fade, (0, 220 * S))
    # 앱 화면은 안전 영역(상태 표시줄 54) 아래에서 시작 → 위 54를 잘라 393×366
    save(hero.crop((0, 54 * S, W * S, 420 * S)), 'CourseHero', rgb=True)

    # 얼굴(동그라미 안), 구간 행성 5개, 탭 바 아이콘 4개
    face = raw(d, FACE)
    fx, fy, fw, fh = FACE_CROP
    box = (round(fx * face.width), round(fy * face.height), round((fx + fw) * face.width), round((fy + fh) * face.height))
    save(face.crop(box).resize((44 * S, 44 * S), Image.LANCZOS), 'Face')
    for i, h in enumerate(ZONES, 1):
        save(raw(d, h).resize((40 * S, 40 * S), Image.LANCZOS), f'Zone{i}')
    for h, name in DOCK.items():
        save(raw(d, h).resize((44 * S, 44 * S), Image.LANCZOS), name)

    if check:
        check.mkdir(parents=True, exist_ok=True)
        pv = bg.copy()
        pv.alpha_composite(Image.open(OUT / 'HomeHero.png').convert('RGBA'), (20 * S, 260 * S))
        pv.alpha_composite(Image.open(OUT / 'CourseThumb.png').convert('RGBA'), (20 * S, 560 * S))
        pv2 = cbg.copy()
        pv2.alpha_composite(Image.open(OUT / 'CourseHero.png').convert('RGBA'), (0, 54 * S))
        for name, img, shot in (('home', pv, 'B-01'), ('detail', pv2, 'C-02')):
            s = Image.open(io.BytesIO(base64.b64decode(d['shots'][shot]))).convert('RGBA').resize(img.size)
            both = Image.new('RGBA', (img.width * 2 + 30, img.height), (40, 40, 40, 255))
            both.paste(img, (0, 0))
            both.paste(s, (img.width + 30, 0))
            both.convert('RGB').resize((both.width // 3, both.height // 3)).save(check / f'check_{name}.png')
    print('→', OUT.relative_to(ROOT))


if __name__ == '__main__':
    main()
