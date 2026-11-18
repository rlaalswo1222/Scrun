using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-10 설정(계획 0049, Figma 610:2647). 알림·공개·연결·데이터 줄 + 아래 로그아웃·계정 삭제.
    // 되는 것: 친구에게 러닝 피드 공개(feedShareDefault, PATCH /users/me/settings), 이어폰 지금 상태, 약관 문서 열기, 로그아웃.
    // 아직 없는 것(정할 것 1 나): 알림 두 줄(BL-46)·공동 맵 인증 마크 보이기(API 없음)는 잠긴 스위치 + "준비 중", 계정 삭제(BL-48)는 "준비 중".
    // 개발 빌드면 맨 아래 "네이티브 점검"(예전 마이 자리에서 옮김)
    public sealed class SettingsScreen : AppScreen
    {
        VisualElement content, feedSwitch;
        Label headphones;
        UserSettings settings;
        List<Agreement> agreements;
        FriendParts.Sheet sheet;
        bool loading, saving, loggingOut;

        protected override string Uxml => "UI/Settings";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            content = Q<VisualElement>("content");

            var alerts = MyParts.Group(content, "알림");
            alerts.Add(MyParts.SwitchRow("친구가 달릴 때", "친구가 달리기 시작하면 알려줘요", false, null, locked: true, onLocked: Soon));
            alerts.Add(MyParts.SwitchRow("주간 목표 리마인더", "목표까지 남은 횟수를 알려줘요", false, null, locked: true, onLocked: Soon));
            MyParts.MarkLast(alerts);

            var open = MyParts.Group(content, "공개 설정");
            var feed = MyParts.SwitchRow("친구에게 러닝 피드 공개", "새로 달린 기록을 친구 피드에 보여줘요", true, SetFeedShare);
            feed.name = "feed-share";
            feedSwitch = feed.Q<VisualElement>("switch");
            open.Add(feed);
            open.Add(MyParts.SwitchRow("공동 맵에서 인증 마크 보이기", null, false, null, locked: true, onLocked: Soon));
            MyParts.MarkLast(open);

            var link = MyParts.Group(content, "연결");
            var ear = MyParts.Row("이어폰", MyText.Headphones(HeadphoneStatus.NotConnected, null), null, null, chevron: false);
            headphones = ear.Q<Label>(className: "my-row-sub");
            link.Add(ear);
            link.Add(MyParts.Row("카카오 계정", null, "연결됨", null, chevron: false));
            MyParts.MarkLast(link);

            var data = MyParts.Group(content, "데이터");
            data.Add(MyParts.Row("계기판 사진", null, "인증 직후 삭제", null, chevron: false)); // 확정 11/06(BL-40 ①): OCR 직후 폐기
            data.Add(MyParts.Row("약관·개인정보처리방침", null, null, OpenTerms));
            MyParts.MarkLast(data);

            var foot = new VisualElement();
            foot.AddToClassList("my-foot");
            var logout = new Button(AskLogout) { text = "로그아웃", name = "logout" };
            logout.AddToClassList("my-foot-btn");
            var delete = new Button(Soon) { text = "계정 삭제", name = "delete-account" };
            delete.AddToClassList("my-foot-btn");
            foot.Add(logout);
            foot.Add(delete);
            content.Add(foot);

            if (Debug.isDebugBuild)
            {
                var dev = new Button(() => { if (Nav.Current == this) Nav.Push(new NativeCheckScreen()); }) { text = "네이티브 점검 (개발 빌드)" };
                dev.AddToClassList("link-button");
                content.Add(dev);
            }
        }

        // 이어폰 상태는 구독하는 동안에만 정확(IHeadphones 주석) → 보이는 동안만 구독
        protected internal override void OnShow()
        {
            try
            {
                Native.Headphones.Changed += OnHeadphones;
                OnHeadphones(Native.Headphones.Status);
            }
            catch (System.Exception e) { Debug.LogWarning("[설정] 이어폰 상태를 못 읽음: " + e.Message); }
            Load();
        }

        protected internal override void OnHide()
        {
            try { Native.Headphones.Changed -= OnHeadphones; }
            catch (System.Exception e) { Debug.LogWarning("[설정] 이어폰 구독을 못 뺌: " + e.Message); }
        }

        protected internal override bool OnBack()
        {
            if (sheet?.Open != true) return false;
            sheet.Close();
            return true;
        }

        void OnHeadphones(HeadphoneStatus s)
        {
            if (Root.panel == null) return;
            string device = null;
            try { device = Native.Headphones.DeviceName; } catch (System.Exception) { }
            headphones.text = MyText.Headphones(s, device);
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            UserSettings got = null;
            bool ok = await Feedback.Run(async () => got = await Api.Users.SettingsAsync(), Load);
            loading = false;
            if (!ok || Root.panel == null) return;
            settings = got;
            MyParts.SetSwitch(feedSwitch, settings.FeedShareDefault);
        }

        async void SetFeedShare(bool on)
        {
            if (saving || settings == null) return;
            saving = true;
            MyParts.SetSwitch(feedSwitch, on); // 바로 바꿔 보이고, 실패하면 되돌림
            UserSettings got = null;
            bool ok = await Feedback.Run(async () => got = await Api.Users.UpdateSettingsAsync(on));
            saving = false;
            if (Root.panel == null) return;
            if (ok) settings = got;
            MyParts.SetSwitch(feedSwitch, settings.FeedShareDefault);
        }

        async void OpenTerms()
        {
            if (agreements == null)
            {
                List<Agreement> got = null;
                if (!await Feedback.Run(async () => got = await Api.Auth.AgreementsAsync(), OpenTerms) || Root.panel == null) return;
                agreements = got;
            }
            if (sheet?.Open == true || Nav.Current != this) return;
            sheet = new FriendParts.Sheet(Root);
            sheet.Body.Add(UiExtensions.Text("약관·개인정보처리방침", "fr-sheet-title"));
            foreach (var a in agreements)
            {
                if (string.IsNullOrEmpty(a.Url)) continue;
                string url = a.Url;
                sheet.Button(a.Title, "btn-ghost", () => Application.OpenURL(url), "terms-" + a.AgreementId);
            }
            sheet.Button("닫기", "btn-ghost", sheet.Close, "terms-close");
        }

        void AskLogout()
        {
            if (sheet?.Open == true || Nav.Current != this) return;
            sheet = new FriendParts.Sheet(Root);
            sheet.Body.Add(UiExtensions.Text("로그아웃할까요?", "fr-sheet-title"));
            sheet.Info("이 기기에서만 로그아웃돼요. 기록은 그대로 남아요.");
            sheet.Button("로그아웃", "btn-danger", Logout, "logout-confirm");
            sheet.Button("취소", "btn-ghost", sheet.Close, "logout-cancel");
        }

        async void Logout()
        {
            if (loggingOut) return;
            loggingOut = true;
            sheet?.Close();
            Feedback.ShowLoading(true);
            try { await Api.Auth.LogoutAsync(); } // 서버에 못 알려도 이 기기 토큰은 지움(AuthApi.LogoutAsync)
            finally { Feedback.ShowLoading(false); }
            Onboarding.ForgetNewUser();
            FriendDirectory.Clear();
            if (Nav != null) Nav.StartFlow(new LoginScreen());
        }

        void Soon()
        {
            if (sheet?.Open == true) return;
            sheet = MyParts.Soon(this);
        }
    }
}
