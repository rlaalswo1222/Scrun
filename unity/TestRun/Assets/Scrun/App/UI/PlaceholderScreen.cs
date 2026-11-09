namespace Scrun.App
{
    // 아직 만들지 않은 탭의 첫 화면(제목 + 설명). 각 기능 담당이 진짜 화면으로 바꾼다.
    public sealed class PlaceholderScreen : AppScreen
    {
        readonly string title;
        readonly string body;

        public PlaceholderScreen(string title, string body)
        {
            this.title = title;
            this.body = body;
        }

        protected override string Uxml => "UI/Placeholder";

        protected override void OnCreate()
        {
            Q<UnityEngine.UIElements.Label>("title").text = title;
            Q<UnityEngine.UIElements.Label>("body").text = body;
        }
    }
}
