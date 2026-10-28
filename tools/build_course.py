"""기본 코스 "한강 반포 5km"(run3d/data/course.json)를 만든다.

  python tools/build_course.py              # 캐시된 OSM 원본이 있으면 재사용
  python tools/build_course.py --refetch    # OSM 원본을 다시 받기

코스: 동작대교 서쪽에서 출발해 반포·잠원 방향으로 5km. 한강이 잘 보이도록 물가에 붙은 강변 산책로를 우선한다.
사용자가 지도에서 고르는 코스는 server.py + coursegen/build.py가 만든다.
지도 데이터 © OpenStreetMap contributors (ODbL) — 화면에 출처 표기 필수.
"""
import argparse
import json
import sys
from pathlib import Path

from shapely.geometry import Point
from shapely.ops import substring, unary_union

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from coursegen import osm, routing, scene  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "run3d" / "data" / "raw" / "osm_banpo.json"
OUT = ROOT / "run3d" / "data" / "course.json"

BBOX = (37.495, 126.965, 37.540, 127.040)  # 남, 서, 북, 동
COURSE_NAME = "한강 반포 5km"
START = (37.5072, 126.9788)  # 동작대교 서쪽 한강남자전거길 (동작대교 밑을 지나 출발)
END = (37.5335, 127.0300)    # 잠원 방향 (5km 지점에서 자름)
LENGTH_M = 5000.0
MARGIN_M = 1200.0            # 코스 주변 몇 m까지 지도를 만들지
# 자전거길·산책로만
PATH_WEIGHT = {"cycleway": 1.0, "footway": 1.0, "path": 1.0, "pedestrian": 1.0}


def riverside_factor(water):
    """물가에 가까운 길일수록 싸게, 섬으로 건너가는 다리는 비싸게."""
    def factor(tags, a, b):
        if tags.get("bridge"):
            return 5.0
        d = water.distance(Point((a[0] + b[0]) / 2, (a[1] + b[1]) / 2))
        return 0.5 if d < 20 else 0.8 if d < 50 else 1.3 if d < 100 else 2.0
    return factor


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--refetch", action="store_true")
    args = p.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    if args.refetch or not RAW.exists():
        print("OSM 데이터 받는 중 (30초~1분)...")
        elements = osm.fetch_scenery(BBOX)
        RAW.parent.mkdir(parents=True, exist_ok=True)
        RAW.write_text(json.dumps({"elements": elements}, ensure_ascii=False), encoding="utf-8")
    elements = json.loads(RAW.read_text(encoding="utf-8"))["elements"]
    proj = osm.Projector(*START)

    # 코스: 최단 경로 -> 부드럽게 -> 정확히 5km로 자르기
    ways = [e for e in elements if e["type"] == "way" and "geometry" in e]
    rels = [e for e in elements if e["type"] == "relation"]
    water = unary_union([g for g in
                         [scene.relation_polygons(r, proj) for r in rels if r["tags"].get("natural") == "water"]
                         + [scene.closed_polygon(w, proj) for w in ways if w["tags"].get("natural") == "water"]
                         if g is not None])
    graph = routing.Graph(ways, proj, weights=PATH_WEIGHT, factor=riverside_factor(water))
    full = routing.smooth(routing.route_between(graph, proj.xy(*START), proj.xy(*END)))
    if full.length < LENGTH_M:
        sys.exit(f"오류: 경로가 {full.length:.0f}m로 {LENGTH_M:.0f}m보다 짧습니다. END를 더 멀리 잡으세요")
    route = routing.resample(substring(full, 0, LENGTH_M))

    course = scene.build_scene(elements, proj, route, name=COURSE_NAME, length_m=LENGTH_M, margin=MARGIN_M)
    OUT.write_text(json.dumps(course, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"코스 {route.length:.0f}m (경로 전체 {full.length:.0f}m에서 자름), "
          f"건물 {len(course['buildings'])}, 도로 {len(course['roads'])}, 나무 {len(course['trees'])}, "
          f"물 {len(course['water'])}, 녹지 {len(course['green'])}")
    print("표지판:", ", ".join(f"{s['label']}({s['d'] / 1000:.2f}km)" for s in course["signs"]))
    print(f"저장: {OUT} ({OUT.stat().st_size / 1e6:.1f}MB)")


if __name__ == "__main__":
    main()
