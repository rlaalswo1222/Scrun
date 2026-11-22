using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // E-12 운동 결과·E-14 미인증(계획 0051, Figma 607:1673·699:2563). 기록 ID로 GET /v1/runs/{id}를 받아 그림.
    // 피드 공유(PATCH), 대조 자세히(E-15 시트, 계기판 값이 있을 때만), 닫기·홈으로 → 홈 탭, "기록 보기" → 기록 탭(F-01, 계획 0053).
    // 기록을 서버에 못 올렸으면(오프라인 종료) 이 폰의 값으로 간이 결과(정할 것 1 가): 공유·그래프·러너·대조 없이 안내만.
    // 기록 상세(F-03·F-04, 계획 0053 정할 것 2 가)도 이 화면(Detail): 내용은 같고 위는 ‹ 기록 상세(뒤로 → 목록), 아래는
    // "이 기록으로 고스트 달리기"(→ 코스 목록 → 코스 상세 → 러닝 설정에 고스트를 켜고 이 기록, 정할 것 3 가)
    public sealed class RunResultScreen : AppScreen
    {
        readonly string runId;
        readonly bool offline;
        readonly bool asRecord; // 기록 탭에서 연 기록 상세(F-03·F-04)
        Task before; // 미인증 마무리(skip)를 서버에 알리는 중이면 끝난 뒤 불러옴(인증 대기로 보이지 않게)
        RunDetail detail;
        VisualElement content, chip, companions, heads, graphCard, shareSwitch;
        StateView state;
        Label date, chipText, km, unit, companionLine, time, pace, goal, shareSub, note;
        Button share, compare;
        SpeedGraph graph;
        FriendParts.Sheet sheet;
        bool loading, sharing, revealed;

        public RunResultScreen(string runId, Task before = null) : this(runId, before, asRecord: false) { }

        RunResultScreen(string runId, Task before, bool asRecord)
        {
            this.runId = runId;
            this.before = before;
            this.asRecord = asRecord;
        }

        // 기록 탭 카드 → F-03·F-04
        public static RunResultScreen Detail(string runId) =>
            new RunResultScreen(runId ?? throw new System.ArgumentNullException(nameof(runId)), null, asRecord: true);

        RunResultScreen(RunDetail local)
        {
            detail = local;
            offline = true;
        }

        // 기록을 서버에 아직 못 올림: 이 폰의 세션 값으로(정할 것 1 가)
        public static RunResultScreen Offline(RunSession s)
        {
            var body = s?.FinishBody;
            return Offline(s?.StartedAtUtc ?? System.DateTime.UtcNow, body?.Sensor?.DistanceM ?? 0, body?.Sensor?.DurationS ?? 0,
                           s?.Goal.ToRequest());
        }

        // 앱이 꺼졌다 켜져 마무리했는데 연결이 안 돼 폰에 남긴 러닝(계획 0052)도 같은 간이 결과
        public static RunResultScreen Offline(System.DateTime startedAtUtc, int m, int sec, RunGoalBody goal) =>
            new RunResultScreen(new RunDetail
            {
                StartedAt = startedAtUtc,
                DistanceM = m,
                DurationS = sec,
                AvgPaceSPerKm = RunResultText.AvgPace(m, sec),
                Verification = "UNVERIFIED",
                Goal = goal,
            });

        protected override string Uxml => "UI/RunResult";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            content = Q<VisualElement>("content");
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.Detail, "기록을 불러오지 못했어요").Padded();
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

            var back = Q<Button>("back");
            var close = Q<Button>("close");
            var home = Q<Button>("home");
            var records = Q<Button>("records");
            var ghost = Q<Button>("ghost");
            ghost.text = RecordsText.GhostButton;
            back.clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            close.clicked += GoHome;
            home.clicked += GoHome;
            records.clicked += GoRecords;
            ghost.clicked += RunWithGhost;
            Q<Label>("title").text = asRecord ? RecordsText.DetailTitle : "운동 결과";
            back.SetVisible(asRecord);
            close.SetVisible(!asRecord);
            home.SetVisible(!asRecord);
            ghost.SetVisible(asRecord);
            ghost.SetEnabled(false); // 기록을 받은 뒤에(Render) 켬. 그 전엔 넘길 기록이 없음
            records.SetVisible(!asRecord && !offline); // 서버에 못 올린 기록은 아직 목록에 없음
            share.clicked += ToggleShare;
            compare.clicked += OpenCompare;

            content.SetVisible(false);
            if (detail != null) Render();
        }

        protected internal override void OnShow()
        {
            if (!offline) Load();
        }

        // 뒤로(아이폰 밀기·Esc) = 닫기. 코스 상세로 돌아가 같은 기록을 다시 볼 일이 없음. 기록 상세면 그냥 목록으로
        protected internal override bool OnBack()
        {
            if (sheet?.Open == true) sheet.Close();
            else if (asRecord) return false;
            else GoHome();
            return true;
        }

        internal void GoHome()
        {
            var nav = Nav;
            if (nav == null || nav.Current != this) return;
            nav.ShowTabRoot(AppTab.Home); // 기록 탭에서 시작한 고스트 달리기여도 홈 탭 첫 화면(홈 탭에 옛 화면이 쌓여 있어도, #94 리뷰)
        }

        // E-12 "기록 보기": 결과 화면들을 내리고 기록 탭 첫 화면(목록은 보일 때 새로 받아 방금 기록이 맨 위).
        // 기록 탭에 전에 열어 둔 상세가 쌓여 있어도 목록으로(리뷰: 옛 상세가 보이던 것)
        internal void GoRecords()
        {
            var nav = Nav;
            if (nav == null || nav.Current != this) return;
            nav.ShowTabRoot(AppTab.Records);
        }

        // F-03 "이 기록으로 고스트 달리기": 코스를 고르면 러닝 설정에 고스트가 켜진 채 이 기록(기록 탭 안에 쌓음)
        void RunWithGhost()
        {
            if (!asRecord || detail == null || Nav == null || Nav.Current != this) return;
            Nav.Push(new CourseListScreen(ghost: detail));
        }

        async void Load()
        {
            if (loading || runId == null) return;
            loading = true;
            if (before != null)
            {
                if (detail == null) state.Loading(); // 미인증 마무리를 기다리는 동안도 같은 스켈레톤(덮개 대신, 계획 0056 리뷰)
                try { await before; }
                catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
                before = null;
            }
            RunDetail got = null;
            // 처음만 스켈레톤(실패하면 그 자리에), 다시 보일 때(시트·다른 화면에서 돌아옴)는 조용히
            bool ok = await Feedback.Load(state, detail != null, async () => got = await Api.Runs.GetAsync(runId), Load);
            loading = false;
            if (Root.panel == null || !ok) return;
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
            date.text = FriendText.RunDate(d.StartedAt);
            var (chipLabel, verified) = RunResultText.Chip(d.Verification);
            chipText.text = chipLabel;
            chip.EnableInClassList("fr-chip--success", verified);
            chip.EnableInClassList("rr-chip--warn", RunResultText.ChipWarn(d.Verification));
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

            Q<Button>("ghost").SetEnabled(asRecord && !offline);
            graph.Set(d.SpeedSeries?.SpeedsKmh);
            graphCard.SetVisible(graph.HasData);
            if (!revealed && graph.HasData) // 처음 그릴 때만 왼쪽부터 그려짐(미리보기 chartReveal). 다시 받아 그릴 땐 그대로
            {
                revealed = true;
                graph.Reveal();
            }

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
            var table = new VisualElement { pickingMode = PickingMode.Ignore }; // 표는 유리 카드 안(미리보기 compareTable)
            table.AddToClassList("ui-card");
            table.AddToClassList("rr-compare");
            table.Add(CompareRow("항목", "센서 추정", "계기판", "차이", head: true));
            bool first = true;
            foreach (var (item, s, c, diff) in RunResultText.Compare(detail.Sensor, detail.Console))
            {
                var row = CompareRow(item, s, c, diff, head: false);
                if (first) row.AddToClassList("rr-compare-row--first"); // 머리 바로 아래 줄은 구분선 없음
                first = false;
                table.Add(row);
            }
            sheet.Body.Add(table);
            sheet.Info(RunResultText.IsVerified(detail.Verification) ? RunResultText.CompareOk : RunResultText.CompareBad);
            sheet.Button("닫기", "btn-secondary", sheet.Close, "compare-close");
        }

        static VisualElement CompareRow(string a, string b, string c, string d, bool head)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("rr-compare-row");
            if (head) row.AddToClassList("rr-compare-row--head");
            foreach (var t in new[] { a, b, c, d }) row.Add(UiExtensions.Text(t, "rr-compare-cell"));
            if (!head) row[0].AddToClassList("rr-compare-cell--item");
            return row;
        }
    }
}
