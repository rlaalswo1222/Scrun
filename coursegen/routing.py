"""길 그래프와 경로 찾기.

- route_between: 두 점 사이 최단(선호) 경로
- auto_route: 출발점에서 원하는 거리만큼 도는 순환 코스, 안 되면 왕복 코스
"""
import heapq
import math

import numpy as np
from shapely.geometry import LineString
from shapely.ops import substring

# 길 종류별 "달리기 불편함" 가중치: 산책로·자전거길을 우선하고 큰 도로는 되도록 피한다
RUN_WEIGHT = {
    "cycleway": 1.0, "footway": 1.0, "path": 1.0, "pedestrian": 1.0,
    "track": 1.2, "living_street": 1.2, "service": 1.5, "residential": 1.5, "unclassified": 1.6,
    "tertiary": 2.5, "tertiary_link": 2.5, "secondary": 3.5, "secondary_link": 3.5,
    "primary": 5.0, "primary_link": 5.0,
}
REUSE_PENALTY = 4.0  # 순환 코스에서 이미 지나온 길을 다시 쓰면 붙는 벌점 배수


def smooth(line):
    """급하게 꺾이는 곳을 부드럽게 깎는다(Chaikin). 양 끝점은 그대로."""
    p = np.asarray(line.coords)
    for _ in range(2):
        q = 0.75 * p[:-1] + 0.25 * p[1:]
        r = 0.25 * p[:-1] + 0.75 * p[1:]
        p = np.vstack([p[:1], np.column_stack([q, r]).reshape(-1, 2), p[-1:]])
    return LineString(p)


def resample(line, step=4.0):
    """일정 간격(step m)으로 점을 다시 찍는다."""
    n = max(2, int(line.length / step) + 1)
    return LineString([line.interpolate(i / (n - 1), normalized=True) for i in range(n)])


class Graph:
    def __init__(self, ways, proj, weights=RUN_WEIGHT, bonus_name=None, factor=None):
        """weights에 없는 길(고속도로·계단 등)과 출입 금지 길은 뺀다.
        bonus_name: 이 이름의 길은 가중치 1 (예: 한강남자전거길).
        factor(tags, a, b): 구간마다 가중치에 곱할 값 (예: 물가에 가까울수록 작게)."""
        self.coords = {}
        self.adj = {}
        for w in ways:
            tags = w.get("tags", {})
            hw = tags.get("highway")
            if hw not in weights or tags.get("access") in ("private", "no") or tags.get("foot") == "no":
                continue
            weight = 1.0 if bonus_name and tags.get("name") == bonus_name else weights[hw]
            for nid, p in zip(w["nodes"], proj.geom(w["geometry"])):
                self.coords[nid] = p
            for a, b in zip(w["nodes"], w["nodes"][1:]):
                length = math.dist(self.coords[a], self.coords[b])
                cost = length * weight * (factor(tags, self.coords[a], self.coords[b]) if factor else 1)
                self.adj.setdefault(a, []).append((b, length, cost))
                self.adj.setdefault(b, []).append((a, length, cost))
        if not self.adj:
            raise RuntimeError("근처에 달릴 수 있는 길이 없어요")
        self.ids = list(self.adj)
        self.xy = np.array([self.coords[n] for n in self.ids])
        # 갈림길(3방향 이상 만나는 곳): 순환 코스의 경유지는 여기에만 찍어서 막다른 길로 빠지지 않게 한다
        self.junctions = [n for n in self.ids if len({v for v, _, _ in self.adj[n]}) >= 3]
        self.jxy = np.array([self.coords[n] for n in self.junctions]) if self.junctions else self.xy

    def nearest(self, x, y, max_m=None, junction=False):
        ids, xy = (self.junctions, self.jxy) if junction and self.junctions else (self.ids, self.xy)
        i = int(np.argmin(np.hypot(xy[:, 0] - x, xy[:, 1] - y)))
        if max_m is not None and math.dist(xy[i], (x, y)) > max_m:
            raise RuntimeError(f"고른 지점에서 {max_m:.0f}m 안에 길이 없어요. 길 가까이를 골라 주세요")
        return ids[i]

    def dijkstra(self, src, dst=None, used=None):
        """반환: (비용, 실제 거리, 이전 노드). used에 든 간선은 벌점."""
        cost, dist, prev = {src: 0.0}, {src: 0.0}, {}
        heap = [(0.0, src)]
        while heap:
            c, u = heapq.heappop(heap)
            if u == dst:
                break
            if c > cost[u]:
                continue
            for v, length, w in self.adj[u]:
                if used and (u, v) in used:
                    w *= REUSE_PENALTY
                if c + w < cost.get(v, math.inf):
                    cost[v], dist[v], prev[v] = c + w, dist[u] + length, u
                    heapq.heappush(heap, (c + w, v))
        return cost, dist, prev

    def path(self, prev, src, dst):
        if dst != src and dst not in prev:
            return None
        nodes = [dst]
        while nodes[-1] != src:
            nodes.append(prev[nodes[-1]])
        return nodes[::-1]

    def line(self, nodes):
        return LineString([self.coords[n] for n in nodes])


def route_between(graph, a_xy, b_xy):
    src, dst = graph.nearest(*a_xy), graph.nearest(*b_xy)
    _, _, prev = graph.dijkstra(src, dst)
    nodes = graph.path(prev, src, dst)
    if nodes is None:
        raise RuntimeError("두 지점이 길로 이어져 있지 않아요")
    return graph.line(nodes)


def out_and_back(graph, src, length):
    """출발점에서 length/2 만큼 갔다가 같은 길로 돌아오는 코스 (길이가 정확히 length)."""
    cost, dist, prev = graph.dijkstra(src)
    half = length / 2
    # 반환점 후보: 길 따라 거리가 반 이상인 곳 중 "편한 길"(비용/거리가 낮은 곳)
    cands = [n for n, d in dist.items() if half <= d <= half * 1.25]
    if not cands:
        raise RuntimeError(f"출발점 주변 길이 짧아서 {length / 1000:.0f}km 코스를 만들 수 없어요")
    turn = min(cands, key=lambda n: cost[n] / dist[n])
    # 먼저 부드럽게 다듬은 뒤 자르므로 최종 길이가 정확히 length가 된다
    out = substring(smooth(graph.line(graph.path(prev, src, turn))), 0, half)
    back = list(out.coords)[::-1][1:]
    return LineString(list(out.coords) + back), "왕복"


def remove_spurs(nodes):
    """u -> v -> u 처럼 들어갔다 바로 되돌아 나오는 부분을 접어서 없앤다 (막다른 길 왕복 제거)."""
    out = []
    for n in nodes:
        if len(out) >= 2 and out[-2] == n:
            out.pop()
        else:
            out.append(n)
    return out


def loop(graph, src, length, attempts=12):
    """출발점 - A - B - 출발점 삼각형 모양으로 도는 순환 코스. 길이는 목표와 조금 다를 수 있다.
    반환: (경로, 겹침 비율, 길이 오차 비율) 중 가장 좋은 것"""
    sx, sy = graph.coords[src]
    best = None
    side = length / 3 / 1.3  # 길이 직선보다 30% 정도 돌아간다고 가정
    for k in range(attempts):
        bearing = 2 * math.pi * k / attempts
        for scale in (0.85, 1.0, 1.15):
            r = side * scale
            a = graph.nearest(sx + r * math.sin(bearing), sy + r * math.cos(bearing), junction=True)
            b = graph.nearest(sx + r * math.sin(bearing + math.pi / 3), sy + r * math.cos(bearing + math.pi / 3),
                              junction=True)
            nodes, used = [src], set()
            ok = True
            for target in (a, b, src):
                _, _, prev = graph.dijkstra(nodes[-1], target, used)
                leg = graph.path(prev, nodes[-1], target)
                if leg is None:
                    ok = False
                    break
                for u, v in zip(leg, leg[1:]):
                    used.add((u, v))
                    used.add((v, u))
                nodes += leg[1:]
            nodes = remove_spurs(nodes)
            if not ok or len(nodes) < 4:
                continue
            line = graph.line(nodes)
            # 같은 간선을 두 번 지나는 비율 (높으면 왕복과 다를 바 없음)
            edges = [frozenset(e) for e in zip(nodes, nodes[1:])]
            seen, repeated = set(), 0.0
            for e, (u, v) in zip(edges, zip(nodes, nodes[1:])):
                if e in seen:
                    repeated += math.dist(graph.coords[u], graph.coords[v])
                seen.add(e)
            overlap = repeated / line.length
            error = abs(line.length - length) / length
            score = overlap + error
            if best is None or score < best[0]:
                best = (score, line, overlap, error)
    if best is None:
        return None
    return smooth(best[1]), best[2], best[3]


def auto_route(graph, start_xy, length, mode="loop"):
    """반환: (경로 LineString, 형태 이름). 순환이 마땅치 않으면 왕복으로 바꾼다."""
    src = graph.nearest(*start_xy, max_m=300)
    if mode == "loop":
        found = loop(graph, src, length)
        if found and found[1] < 0.35 and found[2] < 0.2:
            return found[0], "순환"
    return out_and_back(graph, src, length)
