using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 우주 시안 Game/Mini Orbit Ring(계획 0062, 홈 이번 주 진행 32×32): 회색 고리 위에 채운 만큼 하늘색 호.
    // 12시에서 시계 방향. USS로는 호를 못 그려 Painter2D로 그린다
    public sealed class MiniRing : VisualElement
    {
        static readonly Color Track = new Color32(0x56, 0x66, 0x8F, 0xFF);
        static readonly Color Fill = new Color32(0x3D, 0xD9, 0xFF, 0xFF);
        const float Line = 3.5f;

        float progress;

        public MiniRing()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("mini-ring");
            generateVisualContent += Draw;
        }

        // 0–1(넘치면 자름)
        public float Progress
        {
            get => progress;
            set
            {
                float v = Mathf.Clamp01(float.IsNaN(value) ? 0f : value);
                if (Mathf.Approximately(v, progress)) return;
                progress = v;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float radius = Mathf.Min(r.width, r.height) / 2f - Line / 2f;
            if (radius <= 0) return;
            var c = r.center;
            var p = ctx.painter2D;
            p.lineWidth = Line;
            p.lineCap = LineCap.Round;
            p.strokeColor = Track;
            p.BeginPath();
            p.Arc(c, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
            if (progress <= 0f) return;
            p.strokeColor = Fill;
            p.BeginPath();
            p.Arc(c, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * Mathf.Min(progress, 0.9999f)));
            p.Stroke();
        }
    }
}
