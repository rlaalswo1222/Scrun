using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 노치·다이내믹 아일랜드·홈 바에 글자와 버튼이 가리지 않게 Screen.safeArea만큼 안쪽 여백을 준다.
    // 배경은 화면 끝까지 칠하고(app-root), 내용(safe-area)만 안쪽으로 들인다.
    // 아래 탭이 보이면 아래 여백은 탭 바 안쪽에 준다: 탭 바 배경이 화면 끝까지 닿고 버튼만 홈 바 위에 놓인다
    // (safe-area에 주면 탭 바 밑에 배경색 띠가 따로 보임, 11/10 아이폰 확인)
    public static class SafeArea
    {
        // 적용했으면 true(패널이 아직 없으면 false → 다음 프레임에 다시)
        public static bool Apply(VisualElement target, VisualElement bottomBar = null)
        {
            if (target?.panel == null || Screen.width <= 0 || Screen.height <= 0) return false;
            var sa = Screen.safeArea;
            // 화면 좌표(픽셀, 아래가 0)를 패널 좌표(위가 0, 크기 맞춤 적용)로
            var topLeft = RuntimePanelUtils.ScreenToPanel(target.panel, new Vector2(sa.xMin, Screen.height - sa.yMax));
            var bottomRight = RuntimePanelUtils.ScreenToPanel(target.panel, new Vector2(sa.xMax, Screen.height - sa.yMin));
            var full = RuntimePanelUtils.ScreenToPanel(target.panel, new Vector2(Screen.width, Screen.height));

            target.style.paddingLeft = Mathf.Max(0, topLeft.x);
            target.style.paddingTop = Mathf.Max(0, topLeft.y);
            target.style.paddingRight = Mathf.Max(0, full.x - bottomRight.x);
            float bottom = Mathf.Max(0, full.y - bottomRight.y);
            bool barShown = bottomBar != null && BarShown(bottomBar);
            target.style.paddingBottom = barShown ? 0 : bottom;
            if (bottomBar != null) bottomBar.style.paddingBottom = barShown ? bottom : 0;
            return true;
        }

        // Navigator가 탭 바를 style.display로 켜고 끈다(값이 없으면 보임)
        public static bool BarShown(VisualElement bar) => bar.style.display.value != DisplayStyle.None;
    }

    public static class AppOrientation
    {
        // 세로 고정 또는 가로(왼쪽·오른쪽 둘 다, 기기를 돌린 대로)
        public static void Apply(bool landscape)
        {
            if (landscape)
            {
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
            else
            {
                Screen.orientation = ScreenOrientation.Portrait;
            }
        }
    }
}
