using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 운동 결과(E-12)의 "속도 변화" 선 그래프(계획 0051). 축·숫자 없이 모양만(Figma E-12): 가장 빠른 칸이 위, 0 km/h가 아래.
    //   graph.Set(detail.SpeedSeries.SpeedsKmh);
    public sealed class SpeedGraph : VisualElement
    {
        const float Pad = 4f;
        const int MaxPoints = 120;
        static readonly Color Line = new Color(0.184f, 0.435f, 0.894f); // --color-primary #2F6FE4

        List<float> points = new List<float>();
        float top;

        public SpeedGraph()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public bool HasData => points.Count >= 2 && top > 0f;

        public void Set(IReadOnlyList<float> speedsKmh)
        {
            points = RunResultText.Downsample(speedsKmh, MaxPoints);
            top = 0f;
            foreach (var v in points) top = Mathf.Max(top, v);
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (!HasData || r.width <= 2 * Pad || r.height <= 2 * Pad) return;
            var p = ctx.painter2D;
            p.lineWidth = 2.5f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.strokeColor = Line;
            p.BeginPath();
            for (int i = 0; i < points.Count; i++)
            {
                float x = r.xMin + Pad + (r.width - 2 * Pad) * i / (points.Count - 1);
                float y = r.yMax - Pad - (r.height - 2 * Pad) * Mathf.Clamp01(points[i] / (top * 1.1f));
                if (i == 0) p.MoveTo(new Vector2(x, y));
                else p.LineTo(new Vector2(x, y));
            }
            p.Stroke();
        }
    }
}
