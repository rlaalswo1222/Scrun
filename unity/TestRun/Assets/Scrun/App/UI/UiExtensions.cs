using UnityEngine.UIElements;

namespace Scrun.App
{
    // 화면 코드가 같이 쓰는 작은 도우미
    public static class UiExtensions
    {
        // 보이기·숨기기(숨기면 자리도 차지하지 않음)
        public static void SetVisible(this VisualElement e, bool on) =>
            e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
