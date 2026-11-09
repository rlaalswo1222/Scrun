using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-06 약관 동의. 서버의 약관 목록(GET /v1/agreements)을 그대로 보여 주고, 필수를 모두 고르면 다음(프로필).
    // 약관 본문은 각 항목의 "보기"로 브라우저에서 연다.
    public sealed class TermsScreen : AppScreen
    {
        List<Agreement> agreements;
        readonly Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();
        Toggle all;
        Button next;
        VisualElement list;
        bool loading;

        protected override string Uxml => "UI/Terms";

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => Nav.Pop();
            list = Q<VisualElement>("list");
            all = Q<Toggle>("all");
            all.RegisterValueChangedCallback(e =>
            {
                if (e.target != all) return;
                foreach (var t in toggles.Values) t.SetValueWithoutNotify(e.newValue);
                Refresh();
            });
            next = Q<Button>("next");
            next.clicked += () =>
            {
                if (!CanGoNext) return;
                Nav.Push(new ProfileScreen(toggles.Where(kv => kv.Value.value).Select(kv => kv.Key).ToList()));
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
            if (!ok || Root.panel == null) return;
            agreements = got ?? new List<Agreement>();
            Build();
        }

        void Build()
        {
            list.Clear();
            toggles.Clear();
            foreach (var a in agreements)
            {
                var row = new VisualElement();
                row.AddToClassList("terms-row");
                var t = new Toggle { text = (a.Required ? "(필수) " : "(선택) ") + a.Title };
                t.AddToClassList("terms-toggle");
                t.RegisterValueChangedCallback(_ => Refresh());
                row.Add(t);
                if (!string.IsNullOrEmpty(a.Url))
                {
                    var view = new Button(() => Application.OpenURL(a.Url)) { text = "보기" };
                    view.AddToClassList("link-button");
                    row.Add(view);
                }
                list.Add(row);
                toggles[a.AgreementId] = t;
            }
            Refresh();
        }

        bool CanGoNext =>
            agreements != null && agreements.Where(a => a.Required).All(a => toggles.TryGetValue(a.AgreementId, out var t) && t.value);

        void Refresh()
        {
            next.SetEnabled(CanGoNext);
            all.SetValueWithoutNotify(toggles.Count > 0 && toggles.Values.All(t => t.value));
        }
    }
}
