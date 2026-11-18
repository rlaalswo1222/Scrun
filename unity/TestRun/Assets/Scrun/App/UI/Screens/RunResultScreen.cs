using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // E-12 운동 결과·E-14 미인증(계획 0051, Figma 607:1673·699:2563). 기록 ID로 GET /v1/runs/{id}를 받아 그림.
    // 피드 공유(PATCH), 대조 자세히(E-15 시트, 계기판 값이 있을 때만), 닫기·홈으로 → 홈 탭. "기록 보기"(F-01)는 다음 계획 전까지 숨김(정할 것 3 가).
    // 기록을 서버에 못 올렸으면(오프라인 종료) 이 폰의 값으로 간이 결과(정할 것 1 가): 공유·그래프·러너·대조 없이 안내만.
    // 기록 상세(F-03, 다음 계획 BL-20)에서 같은 화면을 다시 쓴다
    public sealed class RunResultScreen : AppScreen
    {
        readonly string runId;
        readonly bool offline;
        Task before; // 미인증 마무리(skip)를 서버에 알리는 중이면 끝난 뒤 불러옴(인증 대기로 보이지 않게)
        RunDetail detail;
        VisualElement content, loadFailed, chip, companions, heads, graphCard, shareSwitch;
        Label date, chipText, km, unit, companionLine, time, pace, goal, shareSub, note;
        Button share, compare;
        SpeedGraph graph;
        FriendParts.Sheet sheet;
        bool loading, sharing;

        public RunResultScreen(string runId, Task before = null)
        {
            this.runId = runId;
            this.before = before;
        }

        RunResultScreen(RunDetail local)
        {
            detail = local;
            offline = true;
        }

        // 기록을 서버에 아직 못 올림: 이 폰의 세션 값으로(정할 것 1 가)
        public static RunResultScreen Offline(RunSession s)
        {
            var body = s?.FinishBody;
            int m = body?.Sensor?.DistanceM ?? 0, sec = body?.Sensor?.DurationS ?? 0;
            return new RunResultScreen(new RunDetail
            {
                StartedAt = s?.StartedAtUtc ?? System.DateTime.UtcNow,
                DistanceM = m,
                DurationS = sec,
                AvgPaceSPerKm = m > 0 ? (int?)(sec * 1000L / m) : null,
                Verification = "UNVERIFIED",
                Goal = s?.Goal.ToRequest(),
            });
        }

        protected override string Uxml => "UI/RunResult";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            content = Q<VisualElement>("content");
            loadFailed = Q<VisualElement>("load-failed");
            date = Q<Label>("date");
            chip = Q<VisualElement>("chip");
            chipText = Q<Label>("chip-text");
            km = Q<Label>("km");
            unit = Q<Label>("unit");
            companions = Q<VisualElement>("companions");
            heads = Q<VisualElement>("companion-heads");
            companionLine = Q<Label>("companion-line");
            time = Q<Label>("time");
            pace = Q<Label>("pace");
            goal = Q<Label>("goal");
            graphCard = Q<VisualElement>("graph-card");
            share = Q<Button>("share");
            shareSub = Q<Label>("share-sub");
            shareSwitch = Q<VisualElement>("share-switch");
            note = Q<Label>("note");
            compare = Q<Button>("compare");
            graph = new SpeedGraph();
            graph.style.flexGrow = 1;
            Q<VisualElement>("graph").Add(graph);

            Q<Button>("close").clicked += GoHome;
            Q<Button>("home").clicked += GoHome;
            Q<Button>("records").SetVisible(false); // F-01은 다음 계획(BL-20)
            Q<Button>("reload").clicked += Load;
            share.clicked += ToggleShare;
            compare.clicked += OpenCompare;

            content.SetVisible(false);
            loadFailed.SetVisible(false);
            if (detail != null) Render();
        }

        protected internal override void OnShow()
        {
            if (!offline) Load();
        }

        // 뒤로(아이폰 밀기·Esc) = 닫기. 코스 상세로 돌아가 같은 기록을 다시 볼 일이 없음
        protected internal override bool OnBack()
        {
            if (sheet?.Open == true) sheet.Close();
            else GoHome();
            return true;
        }

        void GoHome()
        {
            var nav = Nav;
            if (nav == null || nav.Current != this) return;
            nav.PopToRoot();
            if (nav.CurrentTab != AppTab.Home) nav.SelectTab(AppTab.Home);
        }

        async void Load()
        {
            if (loading || runId == null) return;
            loading = true;
            if (before != null)
            {
                Feedback.ShowLoading(true); // 미인증 마무리를 기다리는 동안 빈 화면 대신 덮개
                try { await before; }
                catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
                finally { Feedback.ShowLoading(false); }
                before = null;
            }
            RunDetail got = null;
            // 처음만 덮개, 다시 보일 때(시트·다른 화면에서 돌아옴)는 조용히
            bool ok = await Feedback.Load(detail != null, async () => got = await Api.Runs.GetAsync(runId), Load);
            loading = false;
            if (Root.panel == null) return;
            if (!ok)
            {
                if (detail == null) loadFailed.SetVisible(true);
                return;
            }
            Show(got);
        }

        internal void Show(RunDetail d)
        {
            detail = d;
            Render();
        }

        void Render()
        {
            var d = detail;
            content.SetVisible(true);
            loadFailed.SetVisible(false);
            date.text = FriendText.RunDate(d.StartedAt);
            var (chipLabel, verified) = RunResultText.Chip(d.Verification);
            chipText.text = chipLabel;
            chip.EnableInClassList("fr-chip--success", verified);
            chip.EnableInClassList("rr-chip--warn", !verified);
            km.text = RunResultText.BigKm(d.DistanceM);
            unit.text = RunResultText.Unit(d.Verification);

            string line = RunResultText.CompanionLine(d.Companions);
            companions.SetVisible(line != null);
            companionLine.text = line ?? "";
            heads.Clear();
            int i = 0;
            foreach (var c in RunResultText.Ordered(d.Companions).Take(3))
            {
                var head = FriendParts.Head(c.Nickname, 40);
                if (i++ > 0) head.AddToClassList("rr-head--overlap");
                heads.Add(head);
            }

            time.text = FriendText.Duration(d.DurationS);
            pace.text = RunResultText.Pace(d.AvgPaceSPerKm);
            goal.text = RunResultText.Goal(d.Goal);

            graph.Set(d.SpeedSeries?.SpeedsKmh);
            graphCard.SetVisible(graph.HasData);

            share.SetVisible(!offline);
            shareSub.text = RunResultText.ShareSub(d.Verification);
            shareSwitch.EnableInClassList("my-switch--on", d.Shared);

            string text = offline ? RunResultText.OfflineNote : verified ? null : RunResultText.UnverifiedNote;
            note.SetVisible(text != null);
            note.text = text ?? "";

            compare.SetVisible(!offline && d.Console != null && d.Sensor != null);
        }

        // 바로 바꿔 보이고, 실패하면 되돌리고 안내
        async void ToggleShare()
        {
            if (sharing || detail == null || offline) return;
            sharing = true;
            bool want = !detail.Shared;
            shareSwitch.EnableInClassList("my-switch--on", want);
            RunSummary got = null;
            bool ok = await Feedback.Run(async () => got = await Api.Runs.SetSharedAsync(runId, want), overlay: false);
            sharing = false;
            if (Root.panel == null) return;
            if (ok && got != null) detail.Shared = got.Shared;
            shareSwitch.EnableInClassList("my-switch--on", detail.Shared);
        }

        // E-15 센서·계기판 대조
        void OpenCompare()
        {
            if (detail?.Console == null || sheet?.Open == true) return;
            sheet = new FriendParts.Sheet(Root);
            sheet.Body.AddToClassList("rr");
            sheet.Body.Add(UiExtensions.Text(RunResultText.CompareTitle, "fr-sheet-title"));
            sheet.Body.Add(CompareRow("항목", "센서 추정", "계기판", "차이", head: true));
            foreach (var (item, s, c, diff) in RunResultText.Compare(detail.Sensor, detail.Console))
                sheet.Body.Add(CompareRow(item, s, c, diff, head: false));
            sheet.Info(RunResultText.IsVerified(detail.Verification) ? RunResultText.CompareOk : RunResultText.CompareBad);
            sheet.Button("닫기", "btn-ghost", sheet.Close, "compare-close");
        }

        static VisualElement CompareRow(string a, string b, string c, string d, bool head)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("rr-compare-row");
            if (head) row.AddToClassList("rr-compare-row--head");
            foreach (var t in new[] { a, b, c, d }) row.Add(UiExtensions.Text(t, "rr-compare-cell"));
            return row;
        }
    }
}
