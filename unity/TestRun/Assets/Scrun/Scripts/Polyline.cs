using System.Collections.Generic;
using UnityEngine;

namespace Scrun
{
    // 거리(m)로 조회할 수 있는 길. 트랙·도로 구간·코스가 모두 이걸로 위치를 구한다.
    public interface IPath
    {
        float Length { get; }
        Vector3 GetPoint(float distance);
        Vector3 GetForward(float distance);
    }

    public static class PathExt
    {
        public static Vector3 GetRight(this IPath p, float distance) =>
            Vector3.Cross(Vector3.up, p.GetForward(distance)).normalized;
    }

    // 촘촘한 꺾은선 + 누적 거리표. 닫힌 길이면 거리가 한 바퀴를 넘을 때 반복한다.
    public class Polyline : IPath
    {
        readonly Vector3[] pts;
        readonly float[] cum;
        readonly bool closed;

        public float Length => cum[^1];
        public IReadOnlyList<Vector3> Points => pts;

        public Polyline(IList<Vector3> points, bool closed)
        {
            this.closed = closed;
            var list = new List<Vector3>(points);
            if (closed && list.Count > 1 && list[0] != list[^1]) list.Add(list[0]);
            pts = list.ToArray();
            cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        }

        float Clamp(float d) => closed ? Mathf.Repeat(d, Length) : Mathf.Clamp(d, 0f, Length);

        int Locate(float distance, out float f)
        {
            float d = Clamp(distance);
            int lo = 0, hi = cum.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] <= d) lo = mid; else hi = mid;
            }
            float seg = cum[hi] - cum[lo];
            f = seg > 1e-6f ? (d - cum[lo]) / seg : 0f;
            return lo;
        }

        public Vector3 GetPoint(float distance)
        {
            if (pts.Length == 1) return pts[0];
            int i = Locate(distance, out float f);
            return Vector3.Lerp(pts[i], pts[i + 1], f);
        }

        public Vector3 GetForward(float distance)
        {
            // ±1m 중앙 차분(열린 길의 양 끝은 안쪽으로 당김): 꺾은선 마디에서 방향이 튀지 않게
            float a = distance - 1f, b = distance + 1f;
            if (!closed) { a = Mathf.Max(0f, a); b = Mathf.Min(Length, b); if (b - a < 0.5f) { a = Mathf.Max(0f, b - 1f); } }
            Vector3 dir = GetPoint(b) - GetPoint(a);
            dir.y = 0f;
            return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
        }

        // ---------- 곡선 ----------
        // centripetal Catmull-Rom(alpha 0.5). 열린 곡선은 양 끝에 직선 연장점을 붙여 끝까지 지나가게 한다.
        public static Polyline FromControlPoints(IList<Vector3> ctrl, bool closed, float spacing = 0.5f)
        {
            int n = ctrl.Count;
            var outPts = new List<Vector3>();
            if (n < 2) { outPts.AddRange(ctrl); return new Polyline(outPts, false); }

            Vector3 P(int i)
            {
                if (closed) return ctrl[(i % n + n) % n];
                if (i < 0) return ctrl[0] * 2f - ctrl[1];
                if (i >= n) return ctrl[n - 1] * 2f - ctrl[n - 2];
                return ctrl[i];
            }

            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector3 p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
                int steps = Mathf.Max(4, Mathf.CeilToInt(Vector3.Distance(p1, p2) / spacing));
                for (int s = 0; s < steps; s++) outPts.Add(CatmullRom(p0, p1, p2, p3, (float)s / steps));
            }
            outPts.Add(closed ? outPts[0] : ctrl[n - 1]);
            return new Polyline(outPts, closed);
        }

        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float t0 = 0f;
            float t1 = t0 + Mathf.Max(1e-4f, Mathf.Sqrt(Vector3.Distance(p0, p1)));
            float t2 = t1 + Mathf.Max(1e-4f, Mathf.Sqrt(Vector3.Distance(p1, p2)));
            float t3 = t2 + Mathf.Max(1e-4f, Mathf.Sqrt(Vector3.Distance(p2, p3)));
            float t = Mathf.Lerp(t1, t2, u);

            Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }
    }
}
