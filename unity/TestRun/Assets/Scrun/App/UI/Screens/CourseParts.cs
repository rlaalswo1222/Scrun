using UnityEngine.UIElements;

namespace Scrun.App
{
    // 코스 목록·상세가 같이 쓰는 조각(모양은 우주 시안 HomeSpace.uss, 계획 0062)
    static class CourseParts
    {
        // ---- 우주 시안(계획 0062) ----

        // 코스 카드 그림(모든 코스 같은 시안 그림, 정할 것 2) + 왼쪽 위 LIVE · 지금 N명(아무도 없으면 안 보임)
        public static VisualElement SpaceThumb(int runners)
        {
            var thumb = Ignore(new VisualElement());
            thumb.AddToClassList("cl-thumb");
            if (runners > 0)
            {
                var live = Ignore(new VisualElement());
                live.AddToClassList("cl-live");
                live.Add(UiExtensions.Text("LIVE", "bc-live"));
                live.Add(UiExtensions.Text(CourseText.LiveCount(runners), "cl-live-count"));
                thumb.Add(live);
            }
            return thumb;
        }

        // 달리는 사람 얼굴 둘 + "+N"(사람 얼굴이 아직 없어 기본 얼굴, 아무도 없으면 비움)
        public static VisualElement Runners(int runners)
        {
            var row = Ignore(new VisualElement());
            row.AddToClassList("cl-runners");
            int faces = System.Math.Min(2, System.Math.Max(0, runners));
            for (int i = 0; i < faces; i++)
            {
                var f = Ignore(new VisualElement());
                f.AddToClassList("bc-face");
                f.AddToClassList("cl-runner-face");
                row.Add(f);
            }
            if (runners > faces) row.Add(UiExtensions.Text("+" + (runners - faces), "cl-more"));
            return row;
        }

        // 구간 행성 5개(시안 그대로): 카드는 아이콘만, 상세는 아이콘 + 이름
        public static VisualElement Zones(string rowClass, string iconClass, bool labels = false)
        {
            var row = Ignore(new VisualElement());
            row.AddToClassList(rowClass);
            for (int i = 0; i < CourseText.Zones.Length; i++)
            {
                var icon = Ignore(new VisualElement());
                icon.AddToClassList(iconClass);
                icon.AddToClassList("zone-" + (i + 1));
                if (!labels)
                {
                    row.Add(icon);
                    continue;
                }
                var col = Ignore(new VisualElement());
                col.AddToClassList("cd-zone");
                col.Add(icon);
                col.Add(UiExtensions.Text(CourseText.Zones[i], "cd-zone-label"));
                row.Add(col);
            }
            return row;
        }

        // 누르지 않는 조각(카드 버튼이 눌림을 받게). UiExtensions.Text는 이미 Ignore
        internal static T Ignore<T>(T e) where T : VisualElement
        {
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        internal static VisualElement Part(string cls)
        {
            var e = Ignore(new VisualElement());
            e.AddToClassList(cls);
            return e;
        }
    }
}
