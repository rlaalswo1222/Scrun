using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 노치·다이내믹 아일랜드·홈 바에 글자와 버튼이 가리지 않게 Screen.safeArea만큼 안쪽 여백을 준다.
    // 배경은 화면 끝까지 칠하고(app-root), 내용(safe-area)만 안쪽으로 들인다.
    public static class SafeArea
    {
        // 적용했으면 true(패널이 아직 없으면 false → 다음 프레임에 다시)
        public static bool Apply(VisualElement target)
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
            target.style.paddingBottom = Mathf.Max(0, full.y - bottomRight.y);
            return true;
        }
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
