using System.Collections.Generic;
using UnityEngine;

namespace Scrun
{
    // 닫힌 곡선 코스. 제어점을 centripetal Catmull-Rom으로 잇는다(뾰족하게 꺾이거나 고리가 생기지 않음).
    // 곡선의 매개변수는 거리에 비례하지 않으므로 촘촘한 꺾은선 + 누적 거리 표(Polyline)로
    // 거리(m)를 조회한다 → 곡선 구간에서도 속도가 일정.
    [ExecuteAlways]
    public class SplineTrack : Track
    {
        [SerializeField] List<Vector3> points = new();   // 로컬 좌표 제어점, 마지막→처음으로 닫힘
        [SerializeField] float sampleSpacing = 0.5f;      // 꺾은선 간격(m)

        Polyline line;

        public IReadOnlyList<Vector3> Points => points;
        public override float LapLength => Line.Length;

        public void SetPoints(IEnumerable<Vector3> localPoints)
        {
            points = new List<Vector3>(localPoints);
            line = null;
        }

        void OnEnable() => line = null;
        void OnValidate() => line = null;

        Polyline Line
        {
            get
            {
                if (line != null) return line;
                if (points.Count < 3) return line = new Polyline(new[] { transform.position }, false);
                var world = new List<Vector3>(points.Count);
                foreach (var p in points) world.Add(transform.TransformPoint(p));
                return line = Polyline.FromControlPoints(world, true, sampleSpacing);
            }
        }

        public override Vector3 GetPoint(float distance) => Line.GetPoint(distance);
        public override Vector3 GetForward(float distance) => Line.GetForward(distance);

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            var p = Line.Points;
            Gizmos.color = new Color(1f, 0.55f, 0.1f);
            for (int i = 4; i < p.Count; i += 4) Gizmos.DrawLine(p[i - 4], p[i]);
            Gizmos.color = Color.cyan;
            foreach (var c in points) Gizmos.DrawSphere(transform.TransformPoint(c), 3f);
        }
#endif
    }
}
