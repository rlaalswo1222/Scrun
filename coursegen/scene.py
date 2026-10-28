"""코스 주변의 강·공원·도로·다리·건물·나무를 3D 화면용 데이터(course.json)로 정리한다."""
import math
import re

import numpy as np
import shapely
import shapely.ops
from shapely.geometry import LineString, Polygon, box
from shapely.ops import linemerge, polygonize, unary_union

from .props import build_props, street_tree_spots

ROAD_WIDTH = {"motorway": 22, "trunk": 22, "primary": 16, "secondary": 12, "tertiary": 9,
              "residential": 6, "unclassified": 6, "living_street": 5,
              "motorway_link": 7, "trunk_link": 7, "primary_link": 7, "secondary_link": 6,
              "tertiary_link": 6, "cycleway": 3, "footway": 2, "path": 2, "pedestrian": 3}
PATH_TYPES = {"cycleway", "footway", "path", "pedestrian"}
GREEN = {("leisure", "park"), ("leisure", "garden"), ("landuse", "grass"),
         ("landuse", "recreation_ground"), ("landuse", "forest"), ("landuse", "meadow"),
         ("landuse", "village_green"), ("natural", "wood"), ("natural", "scrub"),
         ("natural", "grassland")}
# OSM에 bridge:name이 없어서 도로 이름으로만 붙어 있는 다리
# 반포대교는 다리 중간에서 도로 이름이 반포대로 -> 녹사평대로로 바뀐다
BRIDGE_ALIAS = {"반포대로": "반포대교", "녹사평대로": "반포대교", "한남대로": "한남대교", "동작대로": "동작대교",
                "동호로": "동호대교"}
LANDMARK_BRIDGES = ["동작대교", "반포대교", "동호대교"]
# 코스 표면: 자전거길은 붉은 포장, 보도·산책로는 보도블록, 나머지는 차도
SURFACE = {"cycleway": "bike", "footway": "walk", "pedestrian": "walk", "path": "walk", "steps": "walk",
           "track": "walk", "living_street": "walk"}
# 땅 용도 (공원·숲은 green으로 따로)
LANDUSE = {("landuse", "residential"): "residential", ("landuse", "commercial"): "commercial",
           ("landuse", "retail"): "commercial", ("amenity", "parking"): "parking",
           ("amenity", "school"): "school", ("amenity", "university"): "school",
           ("amenity", "kindergarten"): "school", ("leisure", "pitch"): "pitch",
           ("leisure", "playground"): "pitch", ("leisure", "track"): "pitch"}


def relation_polygons(rel, proj):
    """멀티폴리곤 관계: 바깥 테두리로 면을 만들고 안쪽(섬)을 뺀다."""
    def faces(role):
        lines = [LineString(proj.geom(m["geometry"])) for m in rel["members"]
                 if m.get("role") == role and len(m.get("geometry", [])) > 1]
        return unary_union(list(polygonize(unary_union(lines)))) if lines else None
    outer, inner = faces("outer"), faces("inner")
    if outer is None:
        return None
    return outer.difference(inner) if inner is not None else outer


def closed_polygon(w, proj):
    pts = proj.geom(w["geometry"])
    if len(pts) < 4 or w["nodes"][0] != w["nodes"][-1]:
        return None
    poly = Polygon(pts)
    return poly if poly.is_valid else poly.buffer(0)


def polys_out(geom, simplify=1.0):
    """shapely 면 -> [[바깥 고리], [구멍]...] 목록 (좌표 소수 1자리)."""
    out = []
    for g in getattr(geom, "geoms", [geom]):
        if g.is_empty or g.geom_type != "Polygon" or g.area < 20:
            continue
        g = g.simplify(simplify)
        rings = [g.exterior] + list(g.interiors)
        out.append([[[round(x, 1), round(y, 1)] for x, y in r.coords[:-1]] for r in rings])
    return out


def building_height(tags, rng):
    try:
        return float(re.match(r"[\d.]+", tags["height"]).group())
    except (KeyError, AttributeError, ValueError):
        pass
    try:
        return float(tags["building:levels"]) * 3.2
    except (KeyError, ValueError):
        pass
    kind = tags.get("building")
    if kind == "apartments":
        return rng.uniform(30, 50)  # 높이 정보 없는 아파트: 서울 고층 아파트 기준 임의값
    if kind in ("house", "detached", "residential"):
        return rng.uniform(7, 12)
    return rng.uniform(8, 18)


def building_type(tags, h):
    """외관을 고르기 위한 건물 종류: apt(아파트), office(유리 외벽), shop(1층 가게 있는 저층 상가), house(주택)."""
    b = tags.get("building")
    if b == "apartments" or (b in ("residential", "dormitory") and h >= 20):
        return "apt"
    if b in ("office", "commercial", "hotel", "government") or tags.get("office"):
        return "office" if h >= 25 else "shop"
    if b in ("retail", "supermarket", "kiosk") or tags.get("shop") or tags.get("amenity"):
        return "shop"
    if b in ("house", "detached", "semidetached_house", "terrace", "residential"):
        return "house"
    # 정보 없는 건물(building=yes): 서울 길가의 낮은 건물은 대개 1층에 가게가 있는 근린생활시설
    if h < 16:
        return "shop"
    return "office" if h < 35 else "apt"


def route_surfaces(route, lines, step=5.0, min_len=25.0):
    """코스를 따라 5m마다 가장 가까운 길의 종류를 보고 표면 구간 목록을 만든다.
    반환: [{"a": 시작 m, "b": 끝 m, "s": "bike"|"walk"|"road"}]"""
    if not lines:
        return [{"a": 0.0, "b": round(route.length, 1), "s": "walk"}]
    geoms = [g for g, _ in lines]
    kinds = [k for _, k in lines]
    tree = shapely.STRtree(geoms)
    samples = []
    for d in np.arange(0, route.length, step):
        p = route.interpolate(d)
        idx = tree.query_nearest(p, max_distance=8)
        samples.append(SURFACE.get(kinds[idx[0]], "road") if len(idx) else "walk")
    segs = []
    for i, s in enumerate(samples):
        a = i * step
        if segs and segs[-1]["s"] == s:
            segs[-1]["b"] = a + step
        else:
            segs.append({"a": a, "b": a + step, "s": s})
    # 너무 짧은 구간은 앞 구간에 합쳐서 색이 깜빡이듯 바뀌지 않게
    merged = []
    for seg in segs:
        if merged and (seg["b"] - seg["a"] < min_len or merged[-1]["s"] == seg["s"]):
            merged[-1]["b"] = seg["b"]
        else:
            merged.append(seg)
    merged[-1]["b"] = route.length
    return [{"a": round(s["a"], 1), "b": round(s["b"], 1), "s": s["s"]} for s in merged]


def bridge_elevation(tags):
    if tags.get("bridge") in (None, "no") or tags.get("tunnel"):
        return 0.0
    if tags.get("bridge") == "low_water_crossing":
        return 0.0  # 잠수교처럼 물 높이에 붙은 다리는 바닥 도로로 그린다
    try:
        layer = int(tags.get("layer", 1))
    except ValueError:
        layer = 1
    return 4.0 + 6.0 * max(layer, 0)


def scatter_trees(green, route, water, roads, buildings, rng, max_trees=4000, complexes=None, street=()):
    """공원 안쪽, 코스 양옆, 아파트 단지·학교 안에 나무를 흩뿌린다. 물·도로·건물·코스 위는 피한다."""
    blocked = unary_union([water, roads, buildings, route.buffer(4)])
    pts = []
    # 1) 코스 양옆 가로수
    for d in np.arange(0, route.length, 9.0):
        p = route.interpolate(d)
        q = route.interpolate(min(d + 1, route.length))
        tx, ty = q.x - p.x, q.y - p.y
        norm = math.hypot(tx, ty) or 1
        for side in (-1, 1):
            off = side * rng.uniform(6, 10)
            pts.append((p.x - ty / norm * off, p.y + tx / norm * off))
    # 2) 코스 근처 공원 안
    area = green.intersection(route.buffer(300))
    if not area.is_empty:
        minx, miny, maxx, maxy = area.bounds
        n = int(area.area / 150)
        xs, ys = rng.uniform(minx, maxx, n * 3), rng.uniform(miny, maxy, n * 3)
        inside = shapely.contains_xy(area, xs, ys)
        pts += list(zip(xs[inside][:n], ys[inside][:n]))
    # 3) 코스 근처 아파트 단지·학교 안 (조경수, 공원보다 성기게)
    if complexes is not None and not complexes.is_empty:
        area = complexes.intersection(route.buffer(250)).difference(buildings)
        if not area.is_empty:
            minx, miny, maxx, maxy = area.bounds
            n = int(area.area / 350)
            xs, ys = rng.uniform(minx, maxx, n * 3), rng.uniform(miny, maxy, n * 3)
            inside = shapely.contains_xy(area, xs, ys)
            pts += list(zip(xs[inside][:n], ys[inside][:n]))
    # 4) 큰길 보도의 가로수
    pts += list(street)
    pts = np.array(pts)
    ok = ~shapely.contains_xy(blocked, pts[:, 0], pts[:, 1])
    pts = pts[ok]
    # 5) 강 쪽 시야 열기: 코스 근처에서 코스보다 물에 더 가까운 나무는 뺀다 (달리면서 강이 보이게)
    if not water.is_empty:
        samples = np.array([route.interpolate(d).coords[0] for d in np.arange(0, route.length, 10)])
        sample_dw = shapely.distance(water, shapely.points(samples))
        tree_dw = shapely.distance(water, shapely.points(pts))
        d2 = ((pts[:, None, :] - samples[None, :, :]) ** 2).sum(axis=2)
        nearest = d2.argmin(axis=1)
        near_route = np.sqrt(d2[np.arange(len(pts)), nearest]) < 150
        pts = pts[~(near_route & (tree_dw < sample_dw[nearest] - 2))]
    pts = pts[:max_trees]
    return [[round(x, 1), round(y, 1), round(rng.uniform(0.8, 1.4), 2)] for x, y in pts]


def build_scene(elements, proj, route, *, name, length_m, margin, kind="편도", seed=42):
    """route(미터 좌표 LineString) 주변 margin(m) 안의 지도를 course.json 형태로 만든다."""
    ways = [e for e in elements if e["type"] == "way" and "geometry" in e]
    rels = [e for e in elements if e["type"] == "relation"]
    nodes = [e for e in elements if e["type"] == "node"]
    rng = np.random.default_rng(seed)
    region = box(*route.bounds).buffer(margin, join_style="mitre")
    near = route.buffer(margin)

    # 물
    water_geoms = [relation_polygons(r, proj) for r in rels
                   if r["tags"].get("natural") == "water" or r["tags"].get("waterway") == "riverbank"]
    water_geoms += [closed_polygon(w, proj) for w in ways
                    if w["tags"].get("natural") == "water" or w["tags"].get("waterway") == "riverbank"]
    water = unary_union([g for g in water_geoms if g is not None]).intersection(region)

    # 녹지
    green = unary_union([g for w in ways
                         if any((k, w["tags"].get(k)) in GREEN for k in ("leisure", "landuse", "natural"))
                         for g in [closed_polygon(w, proj)] if g is not None])
    green = green.intersection(region).difference(water)

    # 땅 용도: 아파트 단지, 상업지, 주차장, 학교, 운동장
    landuse_geoms = {}
    for w in ways:
        kind = next((LANDUSE[(k, w["tags"].get(k))] for k in ("landuse", "amenity", "leisure")
                     if (k, w["tags"].get(k)) in LANDUSE), None)
        g = closed_polygon(w, proj) if kind else None
        if g is not None:
            landuse_geoms.setdefault(kind, []).append(g)
    landuse = {k: unary_union(v).intersection(region).difference(water) for k, v in landuse_geoms.items()}

    # 도로 (다리는 높이를 줌)
    road_ways = [w for w in ways if w["tags"].get("highway") in ROAD_WIDTH
                 and w["tags"].get("tunnel") not in ("yes", "building_passage")]
    # 다리 끝이 다른 다리와 이어지지 않으면 땅으로 내려오는 경사로를 만든다
    bridge_nodes = {}
    for w in road_ways:
        if bridge_elevation(w["tags"]) > 0:
            for nid in w["nodes"]:
                bridge_nodes[nid] = bridge_nodes.get(nid, 0) + 1
    roads, road_lines = [], []
    flat_roads = []  # 소품 배치용: (선, 폭, 등급, 일방통행, [(노드 id, 좌표)])
    for w in road_ways:
        tags = w["tags"]
        hw = tags["highway"]
        line = LineString(proj.geom(w["geometry"]))
        limit = near if hw not in PATH_TYPES else route.buffer(400)
        if not line.intersects(limit):
            continue
        width = ROAD_WIDTH[hw]
        lanes = int(tags["lanes"]) if str(tags.get("lanes", "")).isdigit() else None
        # 큰길은 상행·하행이 따로 그려진 일방통행인 경우가 많다 -> 한쪽 방향 폭으로
        oneway = tags.get("oneway") in ("yes", "1", "-1")
        if oneway and hw in ("motorway", "trunk", "primary", "secondary", "tertiary"):
            width = (lanes or (3 if hw in ("motorway", "trunk", "primary") else 2)) * 3.3 + 1.0
        elev = bridge_elevation(tags)
        line = line.simplify(0.5)
        road = {"w": round(width, 1), "k": hw, "p": [[round(x, 1), round(y, 1)] for x, y in line.coords]}
        if lanes:
            road["n"] = lanes
        if oneway:
            road["o"] = 1
        if elev > 0:
            ramp = min(80.0, line.length / 2.5)
            ramp_start = bridge_nodes[w["nodes"][0]] < 2
            ramp_end = bridge_nodes[w["nodes"][-1]] < 2
            s = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(np.asarray(line.coords), axis=0).T))])
            k = np.ones_like(s)
            if ramp_start:
                k = np.minimum(k, s / ramp)
            if ramp_end:
                k = np.minimum(k, (s[-1] - s) / ramp)
            road["z"] = [round(float(elev * v), 1) for v in k]
        else:
            road_lines.append(line.buffer(width / 2))
            flat_roads.append((line, width, hw, oneway, list(zip(w["nodes"], proj.geom(w["geometry"])))))
        roads.append(road)

    # 건물
    buildings, footprints = [], []
    for w in ways:
        if "building" not in w["tags"]:
            continue
        poly = closed_polygon(w, proj)
        if poly is None or poly.area < 25 or not poly.intersects(near):
            continue
        poly = poly.simplify(0.5)
        if poly.geom_type != "Polygon":
            continue
        footprints.append(poly)
        h = building_height(w["tags"], rng)
        buildings.append({"h": round(h, 1), "t": building_type(w["tags"], h),
                          "p": [[round(x, 1), round(y, 1)] for x, y in poly.exterior.coords[:-1]]})

    # 다리 이름 표지판: 코스가 다리 밑을 지나는 지점. 가까운 다리끼리는 하나로 합침
    found = {}
    for w in ways:
        tags = w["tags"]
        bname = tags.get("bridge:name") or BRIDGE_ALIAS.get(tags.get("name"), "")
        if not tags.get("bridge") or not bname.endswith(("대교", "잠수교")) or bname in found:
            continue
        line = LineString(proj.geom(w["geometry"]))
        if line.distance(route) < 40:
            found[bname] = route.project(shapely.ops.nearest_points(route, line)[0])
    signs = {}
    for bname, d in sorted(found.items(), key=lambda kv: kv[1]):
        near_label = next((k for k, v in signs.items() if abs(v - d) < 120), None)
        if near_label:
            signs[f"{near_label}·{bname}"] = signs.pop(near_label)
        else:
            signs[bname] = d

    # 랜드마크 다리: 강 위를 지나는 구간의 중심선과 높이 (분수·아치 장식용)
    landmarks = []
    for lname in LANDMARK_BRIDGES:
        decks = [w for w in road_ways if w["tags"].get("bridge")
                 and (w["tags"].get("bridge:name") or BRIDGE_ALIAS.get(w["tags"].get("name"))) == lname]
        if not decks or water.is_empty:
            continue
        # 다리 하나가 여러 토막(way)으로 나뉘어 있으므로 이어 붙인 뒤 강 위 구간만 남긴다
        merged = linemerge(unary_union([LineString(proj.geom(w["geometry"])) for w in decks]))
        part = merged.intersection(water)
        pieces = [g for g in getattr(part, "geoms", [part]) if g.geom_type == "LineString"]
        if not pieces:
            continue
        best = max(pieces, key=lambda g: g.length)
        if best.length > 200:
            landmarks.append({"name": lname, "z": max(bridge_elevation(w["tags"]) for w in decks),
                              "line": [[round(x, 1), round(y, 1)] for x, y in best.simplify(1).coords]})

    complexes = unary_union([landuse[k] for k in ("residential", "school") if k in landuse])
    trees = scatter_trees(green, route, water, unary_union(road_lines), unary_union(footprints), rng,
                          complexes=complexes, street=street_tree_spots(flat_roads, route))
    props = build_props(nodes, ways, proj, route, flat_roads)
    surface_lines = [(LineString(proj.geom(w["geometry"])), w["tags"]["highway"]) for w in road_ways
                     if bridge_elevation(w["tags"]) == 0]
    surfaces = route_surfaces(route, surface_lines)

    return {
        "name": name,
        "kind": kind,
        "length_m": length_m,
        "origin": [proj.lat0, proj.lon0],
        "attribution": "© OpenStreetMap contributors",
        "bounds": [round(v, 1) for v in region.bounds],
        "route": [[round(x, 1), round(y, 1)] for x, y in route.coords],
        "signs": sorted(({"d": round(d, 1), "label": n} for n, d in signs.items()), key=lambda s: s["d"]),
        "water": polys_out(water),
        "green": polys_out(green),
        "landuse": {k: polys_out(g) for k, g in landuse.items()},
        "surfaces": surfaces,
        "roads": roads,
        "buildings": buildings,
        "trees": trees,
        "landmarks": landmarks,
        "props": props,
    }
