using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-05 로그인. 카카오 로그인 → 이미 가입했으면 홈, 처음이면 약관(A-06).
    // Apple 로그인은 출시 전에 추가(다른 소셜 로그인이 있으면 App Store 심사에서 필수, 백로그 BL-1 이후).
    public sealed class LoginScreen : AppScreen
    {
        readonly string notice;
        Button kakao;

        // notice: 로그인 화면으로 돌아온 이유(예: "다시 로그인해 주세요.")
        public LoginScreen(string notice = null) => this.notice = notice;

        protected override string Uxml => "UI/Login";

        protected override void OnCreate()
        {
            kakao = Q<Button>("kakao");
            kakao.clicked += Login;
            var n = Q<Label>("notice");
            n.text = notice ?? "";
            n.style.display = string.IsNullOrEmpty(notice) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        async void Login()
        {
            if (!kakao.enabledSelf) return; // 두 번 누름
            kakao.SetEnabled(false);
            try
            {
                var token = await Native.Kakao.LoginAsync();
                if (token == null) return; // 카카오 화면에서 취소
                LoginResult result = null;
                if (!await Feedback.Run(async () => result = await Api.Auth.KakaoLoginAsync(token), Login)) return;
                if (result.IsNewUser) Nav.Push(new TermsScreen());
                else Nav.ShowTabs();
            }
            finally
            {
                if (Root.panel != null) kakao.SetEnabled(true);
            }
        }
    }
}
