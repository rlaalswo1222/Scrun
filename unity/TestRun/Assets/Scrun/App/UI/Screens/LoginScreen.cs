using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-05 로그인(Figma 596:190)·A-19 로그인 실패(698:2350), 계획 0031. 카카오 로그인 → 이미 가입했으면 온보딩·홈, 처음이면 약관(A-18).
    // 카카오에서 취소하거나 오류가 나면 A-19(failed: 같은 클래스, 다른 UXML)로 가고, 거기서 "카카오로 다시 시도".
    // 카카오톡이 없으면 카카오 계정 웹 로그인으로 넘어가는 건 Swift(ScrunKakao.swift)가 한다.
    // 우리 서버 확인이 실패한 건(망·서버) 지금처럼 오류창(Feedback)에서 다시 시도 — 카카오 로그인을 또 시키지 않음.
    // Apple 로그인은 출시 전에 추가(다른 소셜 로그인이 있으면 App Store 심사에서 필수, Figma 노트 Q-39).
    public sealed class LoginScreen : AppScreen
    {
        readonly string notice;
        readonly bool failed;
        Button kakao;
        bool busy;

        // notice: 로그인 화면으로 돌아온 이유(예: "다시 로그인해 주세요."). failed: A-19 로그인 실패 화면
        public LoginScreen(string notice = null, bool failed = false)
        {
            this.notice = notice;
            this.failed = failed;
        }

        public bool Failed => failed;
        protected override string Uxml => failed ? "UI/LoginFailed" : "UI/Login";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            kakao = Q<Button>("kakao");
            kakao.clicked += Login;
            var dev = Root.Q<Button>("dev-native"); // A-05에만
            if (dev != null)
            {
                if (Debug.isDebugBuild) dev.clicked += () => Nav.Push(new NativeCheckScreen());
                else dev.style.display = DisplayStyle.None;
            }
            var n = Root.Q<Label>("notice"); // A-05에만
            if (n != null)
            {
                n.text = notice ?? "";
                n.style.display = string.IsNullOrEmpty(notice) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        async void Login()
        {
            if (busy) return; // 두 번 누름
            SetBusy(true);
            try
            {
                string token;
                try
                {
                    token = await Native.Kakao.LoginAsync();
                }
                catch (Exception e)
                {
                    // 카카오 SDK 오류(앱 키 설정·연결 등)·아직 기기 기능이 없는 빌드
                    Debug.LogException(e);
                    token = null;
                }
                if (Root.panel == null) return; // 그사이 다른 화면으로
                if (token == null)
                {
                    ShowFailed(); // 취소·오류 → A-19(Figma 노트: 둘 다 같은 화면에서 다시 시도)
                    return;
                }
                await SendToken(token);
            }
            finally
            {
                SetBusy(false);
            }
        }

        void ShowFailed()
        {
            if (!failed)
            {
                if (Nav.Current == this) Nav.Push(new LoginScreen(failed: true));
                return;
            }
            // A-19에서 또 실패: 안내는 이미 떠 있으니 배너를 잠깐 진하게 해 "다시 해 봤는데 또 안 됨"을 알림
            var banner = Root.Q(className: "err-banner");
            if (banner == null) return;
            banner.AddToClassList("err-banner--again");
            banner.schedule.Execute(() => banner.RemoveFromClassList("err-banner--again")).StartingIn(AgainFlashMs);
        }

        const long AgainFlashMs = 600;

        // 카카오 토큰을 우리 서버에 확인. 다시 시도는 이 단계만(카카오 로그인을 또 시키지 않음, 토큰은 그대로 유효)
        async System.Threading.Tasks.Task SendToken(string token)
        {
            LoginResult result = null;
            if (!await Feedback.Run(async () => result = await Api.Auth.KakaoLoginAsync(token),
                                    retry: () => _ = Retry(token)))
                return;
            if (Root.panel == null) return; // 그사이 다른 화면으로
            var nav = Nav;
            if (failed) nav.Pop(); // A-19에서 성공: 약관에서 뒤로 가면 실패 화면이 아니라 로그인으로
            if (result.IsNewUser) nav.Push(new TermsScreen());
            else
            {
                Onboarding.ForgetNewUser(); // 이 기기에서 앞사람이 가입하다 만 표시를 이어받지 않게(주간 목표는 새 가입만)
                Onboarding.Begin(nav); // 기존 회원(재설치·새 폰): 이 기기에서 권한 안내를 안 끝냈으면 거기부터(계획 0025)
            }
        }

        async System.Threading.Tasks.Task Retry(string token)
        {
            if (busy) return;
            SetBusy(true);
            try { await SendToken(token); }
            finally { SetBusy(false); }
        }

        void SetBusy(bool on)
        {
            busy = on;
            if (Root.panel != null) kakao.SetEnabled(!on);
        }
    }
}
