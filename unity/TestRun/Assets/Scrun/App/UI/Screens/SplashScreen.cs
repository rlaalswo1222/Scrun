using System;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-01 스플래시(Figma 596:26, 계획 0030). 앱을 켜면 ShowMs 동안 보이고 next로 넘어간다
    // (AppRoot: 로그인됨 → Onboarding.Begin(권한 안내 또는 홈), 아니면 로그인. 서비스 소개 A-02–A-04가 생기면 첫 이용은 그쪽).
    // 로그인 확인은 저장된 토큰만 봐서 바로 끝나므로 기다릴 일이 없다. 서버 확인(토큰 재발급)은 첫 서버 호출 때 —
    // 여기서 서버를 기다리면 망이 느릴 때 앱이 안 열린다. Figma 노트 "1–2초" → 페이드인 0.4초 + 다 보이는 1초.
    public sealed class SplashScreen : AppScreen
    {
        public const int FadeMs = 400;   // Splash.uss transition-duration과 같게
        public const int ShowMs = 1400;  // 페이드인 시작부터 다음 화면까지

        readonly Action next;
        bool done;

        public SplashScreen(Action next) => this.next = next;

        protected override string Uxml => "UI/Splash";
        public override bool HidesTabBar => true;
        public override string RootClass => "app-root--splash";

        protected override void OnCreate()
        {
            var splash = Q<VisualElement>("splash");
            // 투명한 상태로 스타일·배치가 한 번 끝난 뒤(첫 GeometryChanged)에 클래스를 붙여야 USS transition(페이드인)이 돈다.
            // 같은 패널 갱신 안에서 붙이면 처음부터 불투명으로 계산돼 페이드 없이 뜸. 시간은 페이드 시작부터 잰다
            EventCallback<GeometryChangedEvent> onFirstLayout = null;
            onFirstLayout = _ =>
            {
                splash.UnregisterCallback(onFirstLayout);
                splash.AddToClassList("splash--shown");
                splash.schedule.Execute(Finish).StartingIn(ShowMs);
            };
            splash.RegisterCallback(onFirstLayout);
        }

        // 시간이 다 됨. 그 사이 다른 화면이 떴으면(로그인 만료로 로그인 화면 등) 아무것도 안 한다. 두 번 불려도 한 번만
        internal void Finish()
        {
            if (done || Nav == null || Nav.Current != this) return;
            done = true;
            next?.Invoke();
        }
    }
}
