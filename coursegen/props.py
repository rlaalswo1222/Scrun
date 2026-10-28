"""거리 소품 배치.

- OSM에 기록된 것은 그 위치에: 신호등, 횡단보도, 버스정류장, 울타리·담장·생울타리·가드레일
- 기록이 없는 것은 규칙으로: 큰길 교차로의 신호등·횡단보도, 가로등(30m)
같은 코스는 항상 같은 모습이어야 하므로(같이 달리는 사람이 같은 거리를 봐야 함) 난수는 고정 시드를 쓴다.
각도는 지도 좌표(x=동, y=북)에서 그 소품의 "앞쪽"이 향하는 방향(라디안).
"""
import math

import numpy as np
import shapely
from shapely.geometry import LineString, Point
from shapely.ops import unary_union

CAR_ROADS = {"motorway", "trunk", "primary", "secondary", "tertiary", "residential", "unclassified",
             "living_street", "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link"}
MAJOR_ROADS = {"primary", "secondary", "tertiary"}
LAMP_ROADS = {"primary", "secondary", "tertiary", "residential", "unclassified"}
BARRIERS = {"fence": "fence", "wall": "wall", "retaining_wall": "wall", "hedge": "hedge", "guard_rail": "rail"}


def r1(v):
    return round(float(v), 1)


def r2(v):
    return round(float(v), 2)


class RoadIndex:
    """차도들 중 한 점에서 가장 가까운 것을 찾는다."""

    def __init__(self, roads):
        self.roads = roads  # [(LineString, 폭, 등급, 일방통행, 노드)]
        self.tree = shapely.STRtree([r[0] for r in roads]) if roads else None

    def at(self, pt, max_d):
        """반환: (도로 위 가장 가까운 점, 진행 방향 각도, 폭, 점이 도로의 왼쪽(+1)/오른쪽(-1)) 또는 None"""
        if self.tree is None:
            return None
        idx = self.tree.query_nearest(pt, max_distance=max_d)
        if not len(idx):
            return None
        line, width = self.roads[idx[0]][:2]
        d = line.project(pt)
        a = line.interpolate(max(d - 2, 0))
        b = line.interpolate(min(d + 2, line.length))
        on = line.interpolate(d)
        ang = math.atan2(b.y - a.y, b.x - a.x)
        cross = (b.x - a.x) * (pt.y - on.y) - (b.y - a.y) * (pt.x - on.x)
        return on, ang, width, 1 if cross >= 0 else -1


def along(line, every, offset, start=0.0):
    """선을 따라 every m마다, 진행 방향 오른쪽으로 offset m 떨어진 점과 진행 각도."""
    out = []
    for d in np.arange(start, line.length, every):
        a = line.interpolate(max(d - 1, 0))
        b = line.interpolate(min(d + 1, line.length))
        ang = math.atan2(b.y - a.y, b.x - a.x)
        p = line.interpolate(d)
        out.append((p.x + math.sin(ang) * offset, p.y - math.cos(ang) * offset, ang))
    return out


def junction_props(car_roads, near, keep_clear):
    """OSM에 신호등이 거의 기록돼 있지 않아서, 큰길이 낀 실제 교차로를 찾아
    교차로에서 뻗어 나간 각 방향에 횡단보도와 신호등을 놓는다."""
    refs = {}
    for ri, road in enumerate(car_roads):
        for pi, (nid, _) in enumerate(road[4]):
            refs.setdefault(nid, []).append((ri, pi))
    signals, crossings, done = [], [], []
    for nid, here in refs.items():
        branches = []  # (도로 번호, 노드 위치, 바깥쪽 방향 +1/-1)
        for ri, pi in here:
            n = len(car_roads[ri][4])
            if pi > 0:
                branches.append((ri, pi, -1))
            if pi < n - 1:
                branches.append((ri, pi, 1))
        kinds = {car_roads[ri][2] for ri, _ in here}
        if len(branches) < 3 or not kinds & MAJOR_ROADS:
            continue
        jx, jy = car_roads[here[0][0]][4][here[0][1]][1]
        if not near.contains(Point(jx, jy)) or any(math.dist((jx, jy), d) < 25 for d in done):
            continue  # 상행·하행이 따로 그려진 큰길은 교차로 점이 여러 개 -> 25m 안은 하나로
        done.append((jx, jy))
        box_half = max(car_roads[ri][1] for ri, _ in here) / 2  # 교차로 한가운데 크기
        for ri, pi, step in branches:
            _, w, _, _, raw = car_roads[ri]
            if w < 6:
                continue
            pts = [xy for _, xy in (raw[pi::step] if step > 0 else raw[pi::-1])]
            if len(pts) < 2:
                continue
            arm = LineString(pts)
            for dist, put in ((box_half + 4.5, "cross"), (box_half + 7.5, "signal")):
                if arm.length < dist + 1:
                    continue
                a, b = arm.interpolate(dist - 1), arm.interpolate(dist + 1)
                ang = math.atan2(b.y - a.y, b.x - a.x)
                p = arm.interpolate(dist)
                if put == "cross":
                    crossings.append([r1(p.x), r1(p.y), r2(ang), r1(w)])
                else:
                    x = p.x + math.sin(ang) * (w / 2 + 0.8)  # 바깥쪽 방향의 오른쪽 보도
                    y = p.y - math.cos(ang) * (w / 2 + 0.8)
                    if not keep_clear.contains(Point(x, y)):
                        signals.append([r1(x), r1(y), r2(ang + math.pi / 2)])
    return signals, crossings


def build_props(nodes, ways, proj, route, roads, reach=300):
    """roads: [(LineString, 폭, 등급, 일방통행, 노드)] (땅 위 도로만)"""
    near = route.buffer(reach)
    keep_clear = route.buffer(3)  # 코스 위에는 소품을 두지 않는다
    car_roads = [r for r in roads if r[2] in CAR_ROADS]
    index = RoadIndex(car_roads)

    signals, crossings, stops = [], [], []
    for n in nodes:
        t = n.get("tags", {})
        pt = Point(proj.xy(n["lat"], n["lon"]))
        if not near.contains(pt):
            continue
        hw = t.get("highway")
        if hw == "traffic_signals":
            hit = index.at(pt, 15)
            if hit:
                on, ang, w, _ = hit
                # 기둥은 진행 방향 오른쪽 보도에, 팔은 도로 쪽(왼쪽)을 향함
                x = on.x + math.sin(ang) * (w / 2 + 0.8)
                y = on.y - math.cos(ang) * (w / 2 + 0.8)
                signals.append([r1(x), r1(y), r2(ang + math.pi / 2)])
        elif hw == "crossing" and t.get("crossing") not in ("unmarked", "no", "informal"):
            hit = index.at(pt, 4)
            if hit:
                on, ang, w, _ = hit
                crossings.append([r1(on.x), r1(on.y), r2(ang), r1(w)])
        elif hw == "bus_stop" or (t.get("public_transport") == "platform" and t.get("bus") == "yes"):
            hit = index.at(pt, 25)
            if hit:
                on, ang, w, side = hit
                # 정류장은 노드가 있는 쪽 보도에, 앞쪽(지붕 열린 쪽)이 도로를 향하게
                off = side * (w / 2 + 2.2)
                x, y = on.x - math.sin(ang) * off, on.y + math.cos(ang) * off
                if not keep_clear.contains(Point(x, y)):
                    stops.append([r1(x), r1(y), r2(ang - side * math.pi / 2)])

    barriers = []
    for w in ways:
        kind = BARRIERS.get(w["tags"].get("barrier"))
        if not kind:
            continue
        line = LineString(proj.geom(w["geometry"]))
        if line.intersects(near):
            line = line.difference(keep_clear)
            for g in getattr(line, "geoms", [line]):
                if g.geom_type == "LineString" and g.length > 2:
                    barriers.append({"t": kind, "p": [[r1(x), r1(y)] for x, y in g.simplify(0.3).coords]})

    lamps = []
    for line, w, hw, oneway, _ in car_roads:
        if not line.intersects(near):
            continue
        # 가로등: 보도 바깥쪽, 양쪽에서 엇갈려 30m마다. 팔은 도로 쪽
        if hw in LAMP_ROADS:
            sides = (1,) if oneway else (1, -1)
            for side in sides:
                for x, y, ang in along(line, 30, side * (w / 2 + 2.6), start=15 if side > 0 else 0):
                    if near.contains(Point(x, y)):
                        lamps.append([r1(x), r1(y), r2(ang + (math.pi / 2 if side > 0 else -math.pi / 2))])

    # 규칙으로 보충한 교차로 신호등·횡단보도 (이미 기록된 횡단보도와 6m 안이면 하나만)
    extra_signals, extra_crossings = junction_props(car_roads, near, keep_clear)
    signals += extra_signals
    for c in extra_crossings:
        if all(math.dist(c[:2], o[:2]) > 6 for o in crossings):
            crossings.append(c)

    return {"signals": signals, "crossings": crossings, "stops": stops, "barriers": barriers,
            "lamps": lamps[:1500]}


def street_tree_spots(roads, route, reach=200):
    """큰길 보도의 가로수 자리: 10m마다, 차도 가장자리에서 2.4m."""
    near = route.buffer(reach)
    spots = []
    for line, w, hw, oneway, _ in roads:
        if hw not in ("primary", "secondary", "tertiary") or not line.intersects(near):
            continue
        for side in ((1,) if oneway else (1, -1)):
            for x, y, _ in along(line, 10, side * (w / 2 + 2.4), start=5):
                if near.contains(Point(x, y)):
                    spots.append((x, y))
    return spots


def union_or_empty(geoms):
    return unary_union(geoms) if geoms else shapely.Polygon()
