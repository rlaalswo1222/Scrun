using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 우주 시안 온보딩(계획 0061)의 그림 칸. 안쪽(.sp-art)은 Figma 크기(pt) 그대로 그림·카드를 절대 위치로 두고,
    // 바깥(.sp-fit)이 차지한 자리 가운데에 두고, 자리보다 크면 통째로 줄인다(작은 아이폰에서 글·버튼이 밀려나지 않게). 키우지는 않음.
    public static class SpaceArt
    {
        public const string RootClass = "app-root--space";
        public const string HomeRootClass = "app-root--home";                  // B·C 시안(계획 0062): 홈·코스 목록
        public const string CourseDetailRootClass = "app-root--course-detail"; // C-02: 위 420이 맨 위 코스 그림 색

        public static void Fit(VisualElement fit)
        {
            if (fit == null || fit.childCount == 0) return;
            fit.RegisterCallback<GeometryChangedEvent>(_ => Apply(fit));
        }

        internal static float ScaleFor(Vector2 room, Vector2 art)
        {
            if (art.x <= 0 || art.y <= 0) return 1f;
            if (room.x <= 0 || room.y <= 0) return 0f; // 자리가 없으면(아주 작은 화면·아직 배치 전) 안 그림. 크게 그려 글·버튼을 덮지 않게
            return Mathf.Min(1f, room.x / art.x, room.y / art.y);
        }

        static void Apply(VisualElement fit)
        {
            var art = fit[0];
            float w = art.resolvedStyle.width, h = art.resolvedStyle.height;
            var room = fit.contentRect.size;
            float s = ScaleFor(room, new Vector2(w, h));
            // 가운데에 두고(줄이기는 가운데 기준) 줄임
            art.style.left = (room.x - w) / 2f;
            art.style.top = (room.y - h) / 2f;
            art.style.scale = new Scale(new Vector3(s, s, 1f));
        }
    }
}
