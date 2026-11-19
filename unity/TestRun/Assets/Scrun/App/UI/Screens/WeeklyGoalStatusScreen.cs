using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-07 주간 목표 현황·H-08 변경 시트(계획 0054, BL-30, Figma 610:2409·610:2485). 마이 탭 "주간 목표"에서 들어온다.
    // GET /v1/users/me/weekly-goal을 그대로 그림: 이번 주 N / M회(동그라미), 최근 8주 막대, 바꾼 목표가 다음 주기부터면 "다음 주부터 주 N회".
    // 목표가 없으면 빈 카드 + "목표 정하기"(정할 것 1 가) → 같은 시트, 저장하면 오늘부터 시작. 시트 저장 = PUT(응답이 GET과 같아 그대로 다시 그림)
    public sealed class WeeklyGoalStatusScreen : AppScreen
    {
        VisualElement content, weekCard, empty, barsCard, dots, bars;
        StateView state;
        Label done, of, next;
        Button action;
        WeeklyGoal goal;
        bool loading;
        WeeklyGoalSheet sheet;

        protected override string Uxml => "UI/WeeklyStatus";
        public override bool HidesTabBar => true;

        // 테스트: 시트에서 고른 값
        internal int Picked => sheet?.Picked ?? 0;
        internal bool SheetOpen => sheet?.Open == true;
        internal void Pick(int n) => sheet?.Pick(n);

        protected override void OnCreate()
        {
            content = Q<VisualElement>("content");
            weekCard = Q<VisualElement>("week-card");
            empty = Q<VisualElement>("empty");
            barsCard = Q<VisualElement>("bars-card");
            dots = Q<VisualElement>("dots");
            bars = Q<VisualElement>("bars");
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.Cards, "주간 목표를 불러오지 못했어요").Padded();
            done = Q<Label>("done");
            of = Q<Label>("of");
            next = Q<Label>("next");
            action = Q<Button>("action");
            Q<Button>("back").clicked += () => { if (Nav.Current == this && !SheetOpen) Nav.Pop(); };
            action.clicked += OpenSheet;
            content.SetVisible(false);
            action.SetVisible(false);
        }

        protected internal override void OnShow() => Load();

        protected internal override bool OnBack()
        {
            if (!SheetOpen) return false;
            sheet.Close(); // 저장 중엔 닫지 않음(실패하면 그 시트에서 다시 시도)
            return true;
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            WeeklyGoal got = null;
            bool ok = await Feedback.Load(state, goal != null, async () => got = await Api.Users.GetWeeklyGoalAsync(), Load);
            loading = false;
            if (Root.panel == null || !ok) return;
            if (got == null) // 빈 응답을 "목표 없음"으로 보이면 저장이 변경으로 처리돼 시트 안내와 어긋남
            {
                if (goal == null) state.Failed(null, Load);
                return;
            }
            Show(got);
        }

        internal void Show(WeeklyGoal g)
        {
            goal = g ?? new WeeklyGoal();
            state.Clear();
            content.SetVisible(true);
            action.SetVisible(true);
            bool has = WeeklyGoalText.HasGoal(goal);
            weekCard.SetVisible(has);
            barsCard.SetVisible(has);
            empty.SetVisible(!has);
            action.text = has ? WeeklyGoalText.Change : WeeklyGoalText.SetFirst;
            if (!has) return;

            done.text = WeeklyGoalText.Done(goal);
            of.text = WeeklyGoalText.Of(goal);
            dots.Clear();
            int filled = WeeklyGoalText.Filled(goal);
            for (int i = 0; i < WeeklyGoalText.Dots(goal); i++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("wg-dot");
                bool on = i < filled;
                dot.EnableInClassList("wg-dot--done", on);
                if (on) dot.Add(UiExtensions.Text("✓", "wg-dot-mark"));
                dots.Add(dot);
            }
            string line = WeeklyGoalText.NextLine(goal);
            next.SetVisible(line != null);
            next.text = line ?? "";

            bars.Clear();
            foreach (var b in WeeklyGoalText.Bars(goal))
            {
                var col = new VisualElement { pickingMode = PickingMode.Ignore };
                col.AddToClassList("wg-bar-col");
                var track = new VisualElement { pickingMode = PickingMode.Ignore };
                track.AddToClassList("wg-bar-track");
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.AddToClassList("wg-bar");
                bar.EnableInClassList("wg-bar--achieved", b.Kind == WeeklyGoalText.BarKind.Achieved);
                bar.EnableInClassList("wg-bar--current", b.Kind == WeeklyGoalText.BarKind.Current);
                bar.style.height = Length.Percent(b.Height * 100f);
                track.Add(bar);
                col.Add(track);
                col.Add(UiExtensions.Text(b.Label, "wg-bar-label"));
                bars.Add(col);
            }
        }

        // H-08 시트(WeeklyGoalSheet, 홈 B-02와 같이 씀). 목표가 없으면 같은 시트로 처음 정하기
        internal void OpenSheet()
        {
            if (Nav?.Current != this && Nav != null) return;
            if (SheetOpen || sheet?.Saving == true || goal == null) return; // 저장 중엔 새 시트를 열지 않음(늦게 끝난 저장이 새 시트를 닫지 않게)
            sheet = new WeeklyGoalSheet(Root, goal, got =>
            {
                if (Root.panel == null) return;
                if (got != null) Show(got);
                else Load(); // 빈 응답이면 다시 받아서(저장은 됨)
            });
        }
    }
}
