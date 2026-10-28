"""1인칭 러닝 영상의 "코스 지도"(run3d/assets/video/{id}.course.json)를 만든다.

화면(video.html)은 이 파일이 있으면 카메라와 러너를 영상 속 실제 길 위에 놓는다.

  1) 길 모양: OSM 길 그래프에서 출발지 -> 도착지 경로 (자전거·보행로 우선, --prefer 이름의 길은 더 우선)
  2) 영상 시각 -> 달린 거리: 영상에 찍힌 Relive/Garmin 진행 막대(왼쪽 초록 기둥)의 높이를 2초마다 잰다.
     막대가 가려져 짧게 읽힌 값은 이웃 중앙값으로 거르고, "거리는 줄지 않는다"(등위 회귀)로 맞춘 뒤 20초 평균
  3) 출발점: 경로 위 어디서 영상 속 달리기가 시작했는지 (--s0, m). 기본은 "도착지에서 끝나도록" 경로 길이 - 총 거리

  python tools/build_video_course.py o0EmQXATQQI --osm run3d/data/raw/osm_otago.json \\
      --start -45.8893 170.5272 --end -45.8506 170.6177 --total-km 11.2 --run-start 53 \\
      --prefer "Otago Harbour Trail" --name "Vauxhall → Broad Bay 해안길"

지도 데이터 © OpenStreetMap contributors (ODbL)
"""
import argparse
import json
import sys
from pathlib import Path

import cv2
import numpy as np
from shapely.geometry import LineString
from shapely.ops import substring
from sklearn.isotonic import IsotonicRegression

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
from coursegen import osm, routing  # noqa: E402

VIDEO_DIR = ROOT / "run3d" / "assets" / "video"
WEIGHTS = {"cycleway": 1.0, "footway": 2.5, "path": 3, "pedestrian": 2.5, "residential": 4, "tertiary": 6,
           "secondary": 6, "service": 4, "unclassified": 4, "track": 4}

# 진행 막대(Relive/Garmin 오버레이) 위치: 1280x720 기준 가로 58~63px, 맨 아래 671px
BAR_X0, BAR_X1 = 58, 64
BAR_BOTTOM = 671


def bar_top(frame):
    """초록 막대가 아래에서부터 끊기지 않고 이어진 꼭대기의 y(px). 못 읽으면 None."""
    hsv = cv2.cvtColor(frame[150:680, 46:76], cv2.COLOR_BGR2HSV)
    g = (hsv[..., 0] > 45) & (hsv[..., 0] < 85) & (hsv[..., 1] > 90) & (hsv[..., 2] > 80)
    inside = g[:, BAR_X0 - 46:BAR_X1 - 46].sum(axis=1) >= 4
    outside = g[:, 0:8].sum(axis=1) + g[:, 22:30].sum(axis=1)  # 잔디처럼 넓은 초록이면 양옆도 초록
    bar = inside & (outside <= 2)
    y = BAR_BOTTOM - 150
    if not bar[y - 4:y].any():
        return None
    top, miss = y, 0
    while top > 0 and miss < 3:
        top -= 1
        miss = 0 if bar[top] else miss + 1
    return top + miss + 150


def measure_bar(video, step=2.0):
    cap = cv2.VideoCapture(str(video))
    duration = cap.get(cv2.CAP_PROP_FRAME_COUNT) / cap.get(cv2.CAP_PROP_FPS)
    out = []
    for t in np.arange(0, duration, step):
        cap.set(cv2.CAP_PROP_POS_MSEC, t * 1000)
        ok, f = cap.read()
        if not ok:
            break
        out.append([float(t), bar_top(f)])
    cap.release()
    return out


def distance_table(bar, run_start, total_m):
    """[(영상 시각, 막대 y)] -> 영상 시각별 달린 거리(m).

    막대 1칸(약 23m)이 올라가는 순간 = 그 칸 경계를 막 지난 순간이므로, 그 순간들을 점으로 찍고 직선으로 잇는다.
    (계단을 이동 평균으로 부드럽게 하면 느렸다 빨랐다를 반복하는 가짜 속도 변화가 생긴다)
    """
    pts = [b for b in bar if b[1] is not None and b[0] >= run_start]
    t = np.array([p[0] for p in pts])
    px = np.array([p[1] for p in pts], float)
    med = np.array([np.median(px[max(0, i - 4):i + 5]) for i in range(len(px))])
    keep = px <= med + 2  # 막대가 가려져 짧게 읽힌 값 제거
    level = np.round(IsotonicRegression(increasing=False).fit(t[keep], px[keep]).predict(t))
    start_px, end_px = level[0], level[-1]
    m_per_px = total_m / (start_px - end_px)
    knots_t, knots_d = [run_start], [0.0]
    for i in range(1, len(level)):
        if level[i] < level[i - 1]:
            # 칸이 바뀐 순간: 직전 측정과 이번 측정 사이 가운데
            knots_t.append((t[i - 1] + t[i]) / 2)
            knots_d.append((start_px - level[i - 1]) * m_per_px + m_per_px / 2)
    knots_t.append(t[-1])
    knots_d.append(total_m)
    d = np.interp(t, knots_t, np.minimum(knots_d, total_m))
    # 막대 읽기 잡음(칸 경계가 몇 초씩 앞뒤로 흔들림)을 없애려고 90초 이동 평균. 몇 분 단위의 속도 변화만 남는다
    k = 22  # 2초 간격 x 앞뒤 22칸
    pad = np.r_[2 * d[0] - d[k:0:-1], d, 2 * d[-1] - d[-2:-k - 2:-1]]  # 끝을 기울기 그대로 연장
    d = np.convolve(pad, np.ones(2 * k + 1) / (2 * k + 1), mode="valid")
    d = np.clip(np.maximum.accumulate(d - d[0]), 0, total_m)
    # 처음 150초는 막대가 화면 맨 아래라 잡음이 크고, 이동 평균이 출발 속도를 낮게 만든다(영상은 출발부터 일정한 속도).
    # 그 구간은 150초 지점까지의 평균 속도로 일정하게
    t_a = t[0] + 150
    d_a = float(np.interp(t_a, t, d))
    early = t < t_a
    d[early] = d_a * (t[early] - t[0]) / 150
    return t, d


def main():
    p = argparse.ArgumentParser()
    p.add_argument("video_id")
    p.add_argument("--osm", required=True, help="Overpass 형식 JSON (osm_xml_to_json.py 결과도 가능)")
    p.add_argument("--start", type=float, nargs=2, required=True, metavar=("LAT", "LON"))
    p.add_argument("--end", type=float, nargs=2, required=True, metavar=("LAT", "LON"))
    p.add_argument("--total-km", type=float, required=True, help="영상 속 달리기 총 거리 (인트로 등에 표시)")
    p.add_argument("--run-start", type=float, default=0, help="달리기가 시작하는 영상 시각(초)")
    p.add_argument("--prefer", default=None, help="이 이름의 길을 가장 우선")
    p.add_argument("--s0", type=float, default=None, help="경로 위 출발 위치(m). 없으면 도착지에서 끝나도록")
    p.add_argument("--name", default=None)
    p.add_argument("--bar-cache", default=None, help="진행 막대 측정 결과를 저장·재사용할 파일")
    p.add_argument("--scale", type=float, default=1.0, help="지도 거리 / 영상 속 거리 (지도 경로가 GPS보다 길면 >1)")
    p.add_argument("--out", default=None, help="저장 경로 (기본: run3d/assets/video/{id}.course.json)")
    args = p.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    total = args.total_km * 1000

    # 1) 길 모양
    elements = json.loads(Path(args.osm).read_text(encoding="utf-8"))["elements"]
    ways = [e for e in elements if e["type"] == "way" and "geometry" in e]
    proj = osm.Projector(*args.start)
    graph = routing.Graph(ways, proj, weights=WEIGHTS, bonus_name=args.prefer)
    full = routing.smooth(routing.route_between(graph, proj.xy(*args.start), proj.xy(*args.end)))
    s0 = args.s0 if args.s0 is not None else max(0.0, full.length - total)
    print(f"경로 {full.length:.0f}m, 출발 위치 s0={s0:.0f}m")

    # 2) 영상 시각 -> 달린 거리
    video = VIDEO_DIR / f"{args.video_id}.local.mp4"
    cache = Path(args.bar_cache) if args.bar_cache else None
    if cache and cache.exists():
        bar = json.loads(cache.read_text())
    else:
        print("진행 막대 측정 중 (몇 분)...", flush=True)
        bar = measure_bar(video)
        if cache:
            cache.write_text(json.dumps(bar))
    t, d = distance_table(bar, args.run_start, total)
    v = np.gradient(d, t) * 3.6
    print(f"달린 거리 {d[-1]:.0f}m, 속도 중앙값 {np.median(v):.1f}km/h")

    # 3) 코스 파일: 출발 20m 전부터 도착까지 경로를 잘라 2m 간격, s는 잘린 경로 기준
    lead = min(20.0, s0)
    route = routing.resample(substring(full, s0 - lead, min(full.length, s0 + args.scale * total + 30)), step=2.0)
    s = (lead + args.scale * d).round(1)
    out = {
        "name": args.name or args.video_id,
        "attribution": "© OpenStreetMap contributors",
        "origin": list(args.start),
        "route": [[round(x, 2), round(y, 2)] for x, y in route.coords],
        "sync": {"t": [0.0] + t.round(1).tolist(), "s": [float(lead)] + s.tolist()},
    }
    path = Path(args.out) if args.out else VIDEO_DIR / f"{args.video_id}.course.json"
    path.write_text(json.dumps(out, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"저장: {path} (경로 {route.length:.0f}m, 동기화 {len(t)}점)")


if __name__ == "__main__":
    main()
