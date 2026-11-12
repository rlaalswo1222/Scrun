using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-18 약관·개인정보 동의(Figma 698:2233, 계획 0031). 서버의 약관 목록(GET /v1/agreements)을 그대로 보여 준다
    // (필수 이용약관·개인정보·만 14세, 선택 알림). 필수를 모두 고르면 "동의하고 계속하기" → 프로필(A-06).
    // 약관 본문은 "보기"로 브라우저에서 연다(주소가 없는 항목은 "보기" 없음, 예: 만 14세).
    public sealed class TermsScreen : AppScreen
    {
        List<Agreement> agreements;
        readonly HashSet<string> agreed = new HashSet<string>();
        readonly Dictionary<string, VisualElement> checks = new Dictionary<string, VisualElement>();
        VisualElement allCheck, list, loadFailed;
        Button next;
        bool loading;

        protected override string Uxml => "UI/Terms";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            list = Q<VisualElement>("list");
            loadFailed = Q<VisualElement>("load-failed");
            loadFailed.style.display = DisplayStyle.None;
            list.style.display = DisplayStyle.None;
            Q<Button>("reload").clicked += Load;
            allCheck = Q<VisualElement>("all-check");
            Q<VisualElement>("all").RegisterCallback<ClickEvent>(_ => ToggleAll());
            next = Q<Button>("next");
            next.clicked += () =>
            {
                if (!CanGoNext || Nav.Current != this) return;
                Nav.Push(new ProfileScreen(agreements.Where(a => agreed.Contains(a.AgreementId)).Select(a => a.AgreementId).ToList()));
            };
            Refresh();
        }

        protected internal override void OnShow()
        {
            if (agreements == null) Load();
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            List<Agreement> got = null;
            bool ok = await Feedback.Run(async () => got = await Api.Auth.AgreementsAsync(), Load);
            loading = false;
            if (Root.panel == null) return;
            loadFailed.style.display = ok ? DisplayStyle.None : DisplayStyle.Flex;
            if (!ok) return;
            Show(got);
        }

        // 약관 목록으로 줄을 만든다(테스트는 서버 없이 이걸 부름)
        internal void Show(List<Agreement> items)
        {
            agreements = items ?? new List<Agreement>();
            agreed.IntersectWith(agreements.Select(a => a.AgreementId));
            list.Clear();
            checks.Clear();
            for (int i = 0; i < agreements.Count; i++)
            {
                var a = agreements[i];
                var row = new VisualElement();
                row.AddToClassList("agree-row");
                if (i > 0) row.AddToClassList("agree-row--next");
                var check = MakeCheck();
                row.Add(check);
                var text = new VisualElement();
                text.AddToClassList("agree-text");
                var title = new Label(TermsText.Title(a));
                title.AddToClassList("agree-title");
                text.Add(title);
                var detail = TermsText.Detail(a.AgreementId);
                if (detail != null)
                {
                    var d = new Label(detail);
                    d.AddToClassList("agree-detail");
                    text.Add(d);
                }
                row.Add(text);
                if (!string.IsNullOrEmpty(a.Url))
                {
                    var view = new Button { text = "보기" };
                    view.AddToClassList("agree-view");
                    var url = a.Url;
                    // 줄을 누르면 체크가 바뀌므로 "보기"는 그 클릭이 줄로 올라가지 않게
                    view.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); Application.OpenURL(url); });
                    row.Add(view);
                }
                var id = a.AgreementId;
                row.RegisterCallback<ClickEvent>(_ => Toggle(id));
                list.Add(row);
                checks[id] = check;
            }
            list.style.display = agreements.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh();
        }

        static VisualElement MakeCheck()
        {
            var check = new VisualElement();
            check.AddToClassList("check");
            var mark = new Label("✓");
            mark.AddToClassList("check-mark");
            check.Add(mark);
            return check;
        }

        internal void Toggle(string id)
        {
            if (!agreed.Remove(id)) agreed.Add(id);
            Refresh();
        }

        internal void ToggleAll()
        {
            if (agreements == null || agreements.Count == 0) return;
            bool allOn = agreements.All(a => agreed.Contains(a.AgreementId));
            agreed.Clear();
            if (!allOn) foreach (var a in agreements) agreed.Add(a.AgreementId);
            Refresh();
        }

        internal bool CanGoNext => TermsText.CanContinue(agreements, agreed);

        void Refresh()
        {
            foreach (var kv in checks) SetCheck(kv.Value, agreed.Contains(kv.Key));
            SetCheck(allCheck, agreements != null && agreements.Count > 0 && agreements.All(a => agreed.Contains(a.AgreementId)));
            next.SetEnabled(CanGoNext);
        }

        static void SetCheck(VisualElement check, bool on)
        {
            check.EnableInClassList("check--on", on);
            check[0].style.visibility = on ? Visibility.Visible : Visibility.Hidden;
        }
    }

    // 약관 줄 문구·판정(화면 없이 테스트)
    public static class TermsText
    {
        // "[필수] 서비스 이용약관 동의"(서버 제목이 화면 문구 그대로, 계획 0031 마이그레이션)
        public static string Title(Agreement a) => (a.Required ? "[필수] " : "[선택] ") + a.Title;

        // 제목 아래 설명(Figma A-18). 개인정보처리방침(BL-49)이 확정되면 같이 맞춘다
        public static string Detail(string agreementId) => agreementId switch
        {
            "privacy" => "센서 데이터, 계기판 인증 결과, 프로필(키·몸무게)",
            _ => null,
        };

        // 필수 항목을 모두 골랐을 때만 다음
        public static bool CanContinue(IEnumerable<Agreement> agreements, ICollection<string> agreed) =>
            agreements != null && agreements.Any() && agreements.Where(a => a.Required).All(a => agreed.Contains(a.AgreementId));
    }
}
