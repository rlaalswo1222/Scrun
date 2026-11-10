using UnityEngine.UIElements;

namespace Scrun.App
{
    // 화면 코드가 같이 쓰는 작은 도우미
    public static class UiExtensions
    {
        // 보이기·숨기기(숨기면 자리도 차지하지 않음)
        public static void SetVisible(this VisualElement e, bool on) =>
            e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        // SetVisible로 숨기지 않았는지
        public static bool IsVisible(this VisualElement e) => e.style.display != DisplayStyle.None;

        // 누르지 않는 글자 하나(클래스 붙여서). 카드·줄을 코드로 만들 때
        public static Label Text(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            return l;
        }
    }
}
