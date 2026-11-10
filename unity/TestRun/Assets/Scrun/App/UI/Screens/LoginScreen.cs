using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-05 로그인. 카카오 로그인 → 이미 가입했으면 홈, 처음이면 약관(A-06).
    // Apple 로그인은 출시 전에 추가(다른 소셜 로그인이 있으면 App Store 심사에서 필수, 백로그 BL-1 이후).
    public sealed class LoginScreen : AppScreen
    {
        readonly string notice;
        Button kakao;
        bool busy;

        // notice: 로그인 화면으로 돌아온 이유(예: "다시 로그인해 주세요.")
        public LoginScreen(string notice = null) => this.notice = notice;

        protected override string Uxml => "UI/Login";

        protected override void OnCreate()
        {
            kakao = Q<Button>("kakao");
            kakao.clicked += Login;
            var dev = Q<Button>("dev-native");
            if (Debug.isDebugBuild) dev.clicked += () => Nav.Push(new NativeCheckScreen());
            else dev.style.display = DisplayStyle.None;
            var n = Q<Label>("notice");
            n.text = notice ?? "";
            n.style.display = string.IsNullOrEmpty(notice) ? DisplayStyle.None : DisplayStyle.Flex;
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
                    // 카카오 SDK 오류(앱 키 설정 등)·아직 기기 기능이 없는 빌드
                    Debug.LogException(e);
                    Feedback.ShowError("카카오 로그인을 하지 못했어요", "잠시 후 다시 시도해 주세요.", null, Login);
                    return;
                }
                if (token == null) return; // 카카오 화면에서 취소
                await SendToken(token);
            }
            finally
            {
                SetBusy(false);
            }
        }

        // 카카오 토큰을 우리 서버에 확인. 다시 시도는 이 단계만(카카오 로그인을 또 시키지 않음, 토큰은 그대로 유효)
        async System.Threading.Tasks.Task SendToken(string token)
        {
            LoginResult result = null;
            if (!await Feedback.Run(async () => result = await Api.Auth.KakaoLoginAsync(token),
                                    retry: () => _ = Retry(token)))
                return;
            if (Root.panel == null) return; // 그사이 다른 화면으로
            if (result.IsNewUser) Nav.Push(new TermsScreen());
            else Nav.ShowTabs();
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
