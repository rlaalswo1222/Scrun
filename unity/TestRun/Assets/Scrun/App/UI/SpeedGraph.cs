using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Scrun.App
{
    // 운동 결과(E-12)의 "속도 변화" 선 그래프(계획 0051). 축·숫자 없이 모양만(Figma E-12): 가장 빠른 칸이 위, 0 km/h가 아래.
    // 우주 디자인(계획 0068, 미리보기 speedChart): 하늘색 선(icon/brand) + 아래 옅은 채움(16%), 처음엔 왼쪽부터 그려짐(Reveal, chartReveal .9초)
    //   graph.Set(detail.SpeedSeries.SpeedsKmh);
    public sealed class SpeedGraph : VisualElement
    {
        const float Pad = 4f;
        const int MaxPoints = 120;
        static readonly Color Line = new Color32(125, 184, 255, 255);         // --icon-brand
        static readonly Color Fill = new Color32(125, 184, 255, 41);          // --icon-brand 16%

        List<float> points = new List<float>();
        float top;
        float shown = 1f; // 왼쪽부터 그린 비율(0–1)

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

        // 왼쪽에서 오른쪽으로 그려짐(.25초 뒤 시작, .9초, cubic-bezier(.2, 0, 0, 1))
        public void Reveal(long delayMs = 250, int ms = 900)
        {
            shown = 0f;
            MarkDirtyRepaint();
            schedule.Execute(() =>
                experimental.animation.Start(0f, 1f, ms, (e, t) =>
                {
                    shown = ScreenTransitions.CubicBezier(0.2f, 0f, 0f, 1f, t);
                    MarkDirtyRepaint();
                }).Ease(Easing.Linear).OnCompleted(() =>
                {
                    shown = 1f;
                    MarkDirtyRepaint();
                })).ExecuteLater(delayMs);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (!HasData || shown <= 0f || r.width <= 2 * Pad || r.height <= 2 * Pad) return;
            // 그린 비율까지의 점(마지막 점은 칸 사이를 이어 자름)
            float last = (points.Count - 1) * Mathf.Clamp01(shown);
            int whole = Mathf.FloorToInt(last);
            var pts = new List<Vector2>(whole + 2);
            for (int i = 0; i <= whole; i++) pts.Add(At(r, i));
            if (whole < points.Count - 1 && last > whole)
                pts.Add(Vector2.Lerp(At(r, whole), At(r, whole + 1), last - whole));
            if (pts.Count < 2) return;

            var p = ctx.painter2D;
            p.fillColor = Fill;
            p.BeginPath();
            p.MoveTo(new Vector2(pts[0].x, r.yMax));
            foreach (var pt in pts) p.LineTo(pt);
            p.LineTo(new Vector2(pts[pts.Count - 1].x, r.yMax));
            p.ClosePath();
            p.Fill();

            p.lineWidth = 2.5f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.strokeColor = Line;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]);
            p.Stroke();
        }

        Vector2 At(Rect r, int i) => new Vector2(
            r.xMin + Pad + (r.width - 2 * Pad) * i / (points.Count - 1),
            r.yMax - Pad - (r.height - 2 * Pad) * Mathf.Clamp01(points[i] / (top * 1.1f)));
    }
}
