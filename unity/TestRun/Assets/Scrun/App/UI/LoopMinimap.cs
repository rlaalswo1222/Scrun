using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 루프 미니맵(BL-12 ③, Figma D-10 왼쪽 위, 계획 0041 정할 것 4 (가) 실제 코스 모양).
    // 섬이 넘긴 코스 모양(RunLink.RouteShape, 위에서 본 x·z)을 칸에 맞게 줄여 선으로 그리고, 코스 위 거리로 점을 찍는다.
    //   map.Set(shape, lap, myLapM, others);   // others: (한 바퀴 안 위치 m, 봇인지)
    public sealed class LoopMinimap : VisualElement
    {
        const float Pad = 7f;
        static readonly Color Line = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color Person = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color Bot = new Color(1f, 1f, 1f, 0.5f);
        static readonly Color Me = new Color(0.18f, 0.45f, 0.96f); // Figma 미니맵의 파란 점(UI Kit 값은 상준이 맞춤)

        Vector2[] shape;
        float lap, me = float.NaN;
        readonly List<(float d, bool bot)> others = new List<(float, bool)>();
        Vector2 min, max;

        public LoopMinimap()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public bool HasShape => shape != null && shape.Length >= 2 && lap > 0f;

        public void Set(Vector2[] routeShape, float lapM, float myLapM, IEnumerable<(float d, bool bot)> runners)
        {
            if (!ReferenceEquals(routeShape, shape))
            {
                shape = routeShape;
                if (shape != null && shape.Length > 0)
                {
                    min = max = shape[0];
                    foreach (var p in shape)
                    {
                        min = Vector2.Min(min, p);
                        max = Vector2.Max(max, p);
                    }
                }
            }
            lap = lapM;
            me = myLapM;
            others.Clear();
            if (runners != null) others.AddRange(runners);
            MarkDirtyRepaint();
        }

        // 코스 위 거리(m) → 칸 안 좌표. 점 사이는 곧게 이음
        public Vector2 PointAt(float d)
        {
            float t = LoopMath.Wrap(d, lap) / lap * shape.Length;
            int i = Mathf.FloorToInt(t) % shape.Length;
            var p = Vector2.Lerp(shape[i], shape[(i + 1) % shape.Length], t - Mathf.Floor(t));
            return ToLocal(p);
        }

        Vector2 ToLocal(Vector2 world)
        {
            var r = contentRect;
            Vector2 size = max - min;
            float scale = Mathf.Min((r.width - 2 * Pad) / Mathf.Max(1f, size.x), (r.height - 2 * Pad) / Mathf.Max(1f, size.y));
            Vector2 c = (min + max) * 0.5f;
            // 위에서 본 모양: 섬의 +z가 화면 위
            return new Vector2(r.center.x + (world.x - c.x) * scale, r.center.y - (world.y - c.y) * scale);
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (!HasShape || contentRect.width <= 2 * Pad || contentRect.height <= 2 * Pad) return;
            var p = ctx.painter2D;
            p.lineWidth = 3f;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = Line;
            p.BeginPath();
            p.MoveTo(ToLocal(shape[0]));
            for (int i = 1; i < shape.Length; i++) p.LineTo(ToLocal(shape[i]));
            p.ClosePath();
            p.Stroke();

            foreach (var (d, bot) in others) Dot(p, PointAt(d), 2.6f, bot ? Bot : Person);
            if (!float.IsNaN(me))
            {
                Dot(p, PointAt(me), 5.5f, Color.white);
                Dot(p, PointAt(me), 4f, Me);
            }
        }

        static void Dot(Painter2D p, Vector2 at, float r, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.Arc(at, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }
    }
}
