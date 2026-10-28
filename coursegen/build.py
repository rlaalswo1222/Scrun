"""출발점(위경도) + 거리 -> course.json. 서버(server.py)와 명령줄에서 쓴다.

  python -m coursegen.build 37.5126 126.9960 5000 --mode loop --name "반포 순환 5km"
"""
import argparse
import json
import sys

from . import osm, routing, scene

SCENERY_MARGIN = 500  # 코스 주변 몇 m까지 3D 지도를 만들지


def build_course(lat, lon, length_m, mode="loop", name=None, progress=print):
    progress("길 데이터 받는 중 (처음 고른 동네는 30초~1분)")
    network = osm.fetch_network(lat, lon, length_m / 2 + 300)
    proj = osm.Projector(lat, lon)

    progress("경로 찾는 중")
    graph = routing.Graph([e for e in network if e["type"] == "way"], proj)
    route, kind = routing.auto_route(graph, proj.xy(lat, lon), length_m, mode)
    route = routing.resample(route)
    # 왕복은 정확히 목표 거리, 순환은 실제 길이(10m 단위)
    length = length_m if kind == "왕복" else round(route.length / 10) * 10

    progress("주변 지도 받는 중")
    minx, miny, maxx, maxy = route.bounds
    s, w = proj.latlon(minx - SCENERY_MARGIN, miny - SCENERY_MARGIN)
    n, e = proj.latlon(maxx + SCENERY_MARGIN, maxy + SCENERY_MARGIN)
    elements = osm.fetch_scenery((s, w, n, e))

    progress("3D 지도 만드는 중")
    name = name or f"{kind} {length / 1000:.1f}km"
    return scene.build_scene(elements, proj, route, name=name, length_m=length,
                             margin=SCENERY_MARGIN, kind=kind)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("lat", type=float)
    p.add_argument("lon", type=float)
    p.add_argument("length", type=float, help="목표 거리(m)")
    p.add_argument("--mode", choices=["loop", "out_and_back"], default="loop")
    p.add_argument("--name")
    p.add_argument("--out", default="course_out.json")
    args = p.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    course = build_course(args.lat, args.lon, args.length, args.mode, args.name)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(course, f, ensure_ascii=False, separators=(",", ":"))
    print(f"{course['name']}: {course['length_m']}m, 건물 {len(course['buildings'])}, 나무 {len(course['trees'])} -> {args.out}")


if __name__ == "__main__":
    main()
