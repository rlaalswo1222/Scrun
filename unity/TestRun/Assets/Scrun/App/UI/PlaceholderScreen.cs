namespace Scrun.App
{
    // 아직 만들지 않은 탭의 첫 화면(제목 + 설명). 각 기능 담당이 진짜 화면으로 바꾼다.
    // devTools면 개발 빌드에서 "네이티브 점검" 버튼을 보인다(로그인이 유지되면 로그인 화면의 버튼으로 못 가므로).
    // 마이 탭을 진짜 화면으로 바꿀 때 이 버튼도 그 화면(설정 아래)으로 옮긴다.
    public sealed class PlaceholderScreen : AppScreen
    {
        readonly string title;
        readonly string body;
        readonly bool devTools;

        public PlaceholderScreen(string title, string body, bool devTools = false)
        {
            this.title = title;
            this.body = body;
            this.devTools = devTools;
        }

        protected override string Uxml => "UI/Placeholder";

        protected override void OnCreate()
        {
            Q<UnityEngine.UIElements.Label>("title").text = title;
            Q<UnityEngine.UIElements.Label>("body").text = body;
            if (devTools && UnityEngine.Debug.isDebugBuild)
            {
                var dev = new UnityEngine.UIElements.Button(() => Nav.Push(new NativeCheckScreen())) { text = "네이티브 점검 (개발 빌드)" };
                dev.AddToClassList("link-button");
                Q<UnityEngine.UIElements.ScrollView>(null).Add(dev);
            }
        }
    }
}
