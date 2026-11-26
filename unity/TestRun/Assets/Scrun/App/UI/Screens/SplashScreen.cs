using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-01 스플래시. 앱을 켜면 ShowMs 동안 보이고 next로 넘어간다
    // (AppRoot.AfterSplash: 로그인됨 → Onboarding.Begin(권한 안내·주간 목표 또는 홈), 아니면 로그인).
    // 모양은 로그인(A-05)에서 카카오 버튼·안내 글만 뺀 것: Login.uxml을 그대로 불러 글·버튼은 자리만 남기고 숨긴다.
    // 그래서 로고·행성이 로그인 화면과 같은 자리·크기라 로그인으로 넘어갈 때 그림이 움직이지 않는다.
    // 바탕도 로그인과 같은 bg_login이고, 아이폰 실행 화면(Unity가 뜨기 전)도 이 그림(AppSceneBuilder.EnsureLaunchScreen).
    // 로그인 확인은 저장된 토큰만 봐서 바로 끝나므로 기다릴 일이 없다. 서버 확인(토큰 재발급)은 첫 서버 호출 때 —
    // 여기서 서버를 기다리면 망이 느릴 때 앱이 안 열린다. Figma 노트는 "1–2초"였으나 11/25 민재 결정으로 1초 늘림 → 페이드인 0.4초 + 다 보이는 2초.
    public sealed class SplashScreen : AppScreen
    {
        public const int FadeMs = 400;   // Splash.uss transition-duration과 같게
        public const int ShowMs = 2400;  // 페이드인 시작부터 다음 화면까지

        internal const string ShownClass = "splash--shown";
        internal const string ArtClass = "splash-art";       // 페이드인하는 로고·행성
        internal const string HoldClass = "splash-hold";     // 자리만 남기고 숨기는 로그인 글·버튼

        readonly Action next;
        bool done;

        public SplashScreen(Action next) => this.next = next;

        protected override string Uxml => "UI/Login";
        public override bool HidesTabBar => true;
        public override string RootClass => SpaceArt.LoginRootClass;

        protected override void OnCreate()
        {
            Root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Splash")
                                 ?? throw new InvalidOperationException("USS를 찾지 못함: Resources/UI/Splash.uss"));
            SpaceArt.Fit(Q<VisualElement>("fit")); // 로그인과 같은 맞춤
            Q<VisualElement>("fit").AddToClassList(ArtClass);
            // 안내 글·버튼 칸은 숨기되 높이는 그대로(display 대신 visibility). 알림 줄·개발 버튼은 로그인 화면과 같은 규칙으로
            Root.Q(className: "ob-login-text").AddToClassList(HoldClass);
            Root.Q(className: "ob-login-foot").AddToClassList(HoldClass);
            Q<Label>("notice").style.display = DisplayStyle.None;
            if (!Debug.isDebugBuild) Q<Button>("dev-native").style.display = DisplayStyle.None;
            Q<Button>("kakao").SetEnabled(false); // 숨겨도 누를 일 없게

            // 투명한 상태로 스타일·배치가 한 번 끝난 뒤(첫 GeometryChanged)에 클래스를 붙여야 USS transition(페이드인)이 돈다.
            // 같은 패널 갱신 안에서 붙이면 처음부터 불투명으로 계산돼 페이드 없이 뜸. 시간은 페이드 시작부터 잰다
            EventCallback<GeometryChangedEvent> onFirstLayout = null;
            onFirstLayout = _ =>
            {
                Root.UnregisterCallback(onFirstLayout);
                Root.AddToClassList(ShownClass);
                Root.schedule.Execute(Finish).StartingIn(ShowMs);
            };
            Root.RegisterCallback(onFirstLayout);
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
