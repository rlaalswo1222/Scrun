using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scrun
{
    // 섬의 도로망: 교차로(노드) + 도로 구간(엣지) + 코스(엣지를 이은 경로).
    // 데이터는 에디터 빌더가 IslandLayout.json에서 채운다. 위치 계산은 런타임에 꺾은선으로 만든다.
    // 높낮이는 화면에만 쓴다(오르막이어도 속도·러닝머신 경사와 무관): 노드 높이 + 엣지별 높이표(HeightStep m 간격)
    [ExecuteAlways]
    public class RoadNetwork : MonoBehaviour
    {
        [Serializable]
        public class Node
        {
            public string id;
            public Vector3 position;
        }

        [Serializable]
        public class Edge
        {
            public string id, from, to, region;
            public List<Vector3> controlPoints = new();   // 시작 노드 … 경유점 … 끝 노드(높이는 무시, 평면 곡선)
            public List<float> heights = new();           // 평면 곡선 거리 HeightStep m마다의 노면 높이(비면 0)
        }

        public const float HeightStep = 5f;

        [Serializable]
        public class Route
        {
            public string id, name;
            public List<string> path = new();   // 엣지 id, 앞에 "-"면 거꾸로
        }

        [Serializable]
        public class Region
        {
            public string id, name;
            public Vector3 center;
            public float radius;
        }

        public List<Region> regions = new();
        public List<Node> nodes = new();
        public List<Edge> edges = new();
        public List<Route> routes = new();

        readonly Dictionary<string, Polyline> edgeLines = new();

        public Node FindNode(string id) => nodes.Find(n => n.id == id);
        public Edge FindEdge(string id) => edges.Find(e => e.id == id);
        public Route FindRoute(string id) => routes.Find(r => r.id == id);

        void OnEnable() => edgeLines.Clear();
        void OnValidate() => edgeLines.Clear();
        public void ClearCache() => edgeLines.Clear();

        public Polyline EdgeLine(string edgeId)
        {
            if (edgeLines.TryGetValue(edgeId, out var line)) return line;
            var e = FindEdge(edgeId);
            var flatCtrl = new List<Vector3>(e.controlPoints.Count);
            foreach (var c in e.controlPoints) flatCtrl.Add(new Vector3(c.x, 0f, c.z));
            line = Polyline.FromControlPoints(flatCtrl, false);
            if (e.heights != null && e.heights.Count > 1)
            {
                // 평면 곡선의 누적 거리로 높이표를 읽어 점마다 높이를 입힘(입힌 뒤 거리는 경사만큼 조금 길어짐)
                var pts = new List<Vector3>(line.Points);
                float acc = 0f;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (i > 0) acc += Vector2.Distance(new Vector2(pts[i - 1].x, pts[i - 1].z), new Vector2(pts[i].x, pts[i].z));
                    float f = acc / HeightStep;
                    int k = Mathf.Clamp(Mathf.FloorToInt(f), 0, e.heights.Count - 2);
                    var p = pts[i];
                    p.y = Mathf.Lerp(e.heights[k], e.heights[k + 1], Mathf.Clamp01(f - k));
                    pts[i] = p;
                }
                line = new Polyline(pts, false);
            }
            edgeLines[edgeId] = line;
            return line;
        }

        // 코스를 하나의 닫힌 꺾은선으로. 교차로에서 꺾이는 곳은 반경 cornerRadius로 둥글게 깎는다.
        public Polyline BuildRoute(string routeId, float cornerRadius = 12f, float spacing = 0.5f)
        {
            var route = FindRoute(routeId);
            if (route == null) throw new ArgumentException($"코스 없음: {routeId}");

            var legs = new List<List<Vector3>>();
            foreach (var step in route.path)
            {
                bool rev = step.StartsWith("-");
                var line = EdgeLine(step.TrimStart('-'));
                var pts = new List<Vector3>(line.Points);
                if (rev) pts.Reverse();
                legs.Add(pts);
            }

            var outPts = new List<Vector3>();
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                var next = legs[(i + 1) % legs.Count];
                var prev = legs[(i - 1 + legs.Count) % legs.Count];
                // 앞뒤 교차로 근처 cornerRadius만큼 잘라 낸 몸통
                int a = IndexAtDistance(leg, cornerRadius, fromEnd: false);
                int b = IndexAtDistance(leg, cornerRadius, fromEnd: true);
                if (b <= a) { a = leg.Count / 2; b = a; }
                for (int k = a; k <= b; k++) outPts.Add(leg[k]);

                // 이 구간 끝 → 다음 구간 시작을 잇는 둥근 모서리(2차 베지어, 제어점 = 교차로)
                Vector3 p0 = leg[b], corner = leg[^1], p2 = next[IndexAtDistance(next, cornerRadius, false)];
                int steps = Mathf.Max(4, Mathf.CeilToInt((Vector3.Distance(p0, corner) + Vector3.Distance(corner, p2)) / spacing));
                for (int s = 1; s < steps; s++)
                {
                    float t = (float)s / steps;
                    outPts.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * corner + t * t * p2);
                }
            }
            return new Polyline(outPts, true);
        }

        static int IndexAtDistance(List<Vector3> pts, float dist, bool fromEnd)
        {
            float acc = 0f;
            if (!fromEnd)
            {
                for (int i = 1; i < pts.Count; i++) { acc += Vector3.Distance(pts[i - 1], pts[i]); if (acc >= dist) return i; }
                return pts.Count - 1;
            }
            for (int i = pts.Count - 2; i >= 0; i--) { acc += Vector3.Distance(pts[i + 1], pts[i]); if (acc >= dist) return i; }
            return 0;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f);
            foreach (var e in edges)
            {
                var line = EdgeLine(e.id);
                var p = line.Points;
                for (int i = 4; i < p.Count; i += 4) Gizmos.DrawLine(p[i - 4], p[i]);
            }
            Gizmos.color = Color.cyan;
            foreach (var n in nodes) Gizmos.DrawSphere(n.position, 6f);
        }
#endif
    }
}
