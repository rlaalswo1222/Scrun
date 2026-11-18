using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-07 주간 목표 현황·H-08 변경 시트(계획 0054, BL-30, Figma 610:2409·610:2485). 마이 탭 "주간 목표"에서 들어온다.
    // GET /v1/users/me/weekly-goal을 그대로 그림: 이번 주 N / M회(동그라미), 최근 8주 막대, 바꾼 목표가 다음 주기부터면 "다음 주부터 주 N회".
    // 목표가 없으면 빈 카드 + "목표 정하기"(정할 것 1 가) → 같은 시트, 저장하면 오늘부터 시작. 시트 저장 = PUT(응답이 GET과 같아 그대로 다시 그림)
    public sealed class WeeklyGoalStatusScreen : AppScreen
    {
        VisualElement content, weekCard, empty, barsCard, dots, bars, loadFailed;
        Label done, of, next;
        Button action;
        WeeklyGoal goal;
        bool loading, saving;
        FriendParts.Sheet sheet;
        Label sheetNumber;
        Button minus, plus, saveButton, cancelButton;
        VisualElement chips;
        int picked;

        protected override string Uxml => "UI/WeeklyStatus";
        public override bool HidesTabBar => true;

        // 테스트: 시트에서 고른 값
        internal int Picked => picked;
        internal bool SheetOpen => sheet?.Open == true;

        protected override void OnCreate()
        {
            content = Q<VisualElement>("content");
            weekCard = Q<VisualElement>("week-card");
            empty = Q<VisualElement>("empty");
            barsCard = Q<VisualElement>("bars-card");
            dots = Q<VisualElement>("dots");
            bars = Q<VisualElement>("bars");
            loadFailed = Q<VisualElement>("load-failed");
            done = Q<Label>("done");
            of = Q<Label>("of");
            next = Q<Label>("next");
            action = Q<Button>("action");
            Q<Button>("back").clicked += () => { if (Nav.Current == this && !SheetOpen) Nav.Pop(); };
            Q<Button>("reload").clicked += Load;
            action.clicked += OpenSheet;
            content.SetVisible(false);
            action.SetVisible(false);
            loadFailed.SetVisible(false);
        }

        protected internal override void OnShow() => Load();

        protected internal override bool OnBack()
        {
            if (!SheetOpen) return false;
            if (!saving) sheet.Close(); // 저장 중엔 닫지 않음(실패하면 그 시트에서 다시 시도)
            return true;
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            WeeklyGoal got = null;
            bool ok = await Feedback.Load(goal != null, async () => got = await Api.Users.GetWeeklyGoalAsync(), Load);
            loading = false;
            if (Root.panel == null) return;
            if (!ok || got == null) // 빈 응답을 "목표 없음"으로 보이면 저장이 변경으로 처리돼 시트 안내와 어긋남
            {
                if (goal == null) loadFailed.SetVisible(true);
                return;
            }
            Show(got);
        }

        internal void Show(WeeklyGoal g)
        {
            goal = g ?? new WeeklyGoal();
            loadFailed.SetVisible(false);
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

        // H-08: −/+ · 1–7 칩 · 안내 · 저장·취소. 목표가 없으면 같은 시트로 처음 정하기
        internal void OpenSheet()
        {
            if (Nav?.Current != this && Nav != null) return;
            if (SheetOpen || saving || goal == null) return; // 저장 중엔 새 시트를 열지 않음(늦게 끝난 저장이 새 시트를 닫지 않게)
            sheet = new FriendParts.Sheet(Root);
            sheet.Dim.AddToClassList("wg");
            var title = UiExtensions.Text(WeeklyGoalText.SheetTitle, "fr-sheet-title");
            title.AddToClassList("wg-sheet-title");
            sheet.Body.Add(title);

            var stepper = new VisualElement();
            stepper.AddToClassList("wg-stepper");
            minus = new Button(() => Pick(picked - 1)) { text = "−", name = "goal-minus" };
            minus.AddToClassList("wg-step");
            plus = new Button(() => Pick(picked + 1)) { text = "+", name = "goal-plus" };
            plus.AddToClassList("wg-step");
            var value = new VisualElement();
            value.AddToClassList("wg-step-value");
            sheetNumber = UiExtensions.Text("", "wg-step-number");
            sheetNumber.name = "goal-number";
            value.Add(sheetNumber);
            value.Add(UiExtensions.Text("회", "wg-step-unit"));
            stepper.Add(minus);
            stepper.Add(value);
            stepper.Add(plus);
            sheet.Body.Add(stepper);

            chips = new VisualElement();
            chips.AddToClassList("wg-chips");
            for (int n = WeeklyGoalText.Min; n <= WeeklyGoalText.Max; n++)
            {
                int v = n;
                var chip = new Button(() => Pick(v)) { text = n.ToString(), name = "goal-chip-" + n };
                chip.AddToClassList("wg-chip");
                chips.Add(chip);
            }
            sheet.Body.Add(chips);
            sheet.Body.Add(UiExtensions.Text(WeeklyGoalText.SheetNote(goal), "wg-sheet-note"));
            saveButton = sheet.Button(WeeklyGoalText.Save, "btn-primary", Save, "goal-save");
            saveButton.AddToClassList("wg-sheet-button");
            cancelButton = sheet.Button(WeeklyGoalText.Cancel, "btn-ghost", sheet.Close, "goal-cancel");
            cancelButton.AddToClassList("wg-sheet-button");
            Pick(WeeklyGoalText.SheetStart(goal));
        }

        internal void Pick(int n)
        {
            picked = WeeklyGoalText.Clamp(n);
            sheetNumber.text = picked.ToString();
            minus.SetEnabled(picked > WeeklyGoalText.Min);
            plus.SetEnabled(picked < WeeklyGoalText.Max);
            for (int i = 0; i < chips.childCount; i++) chips[i].EnableInClassList("wg-chip--on", i + WeeklyGoalText.Min == picked);
        }

        async void Save()
        {
            if (saving || !SheetOpen) return;
            saving = true;
            // 저장 중엔 바깥·뒤로·취소로 닫히지 않게(닫힌 뒤 실패하면 오류창의 "다시 시도"가 아무것도 안 하던 것, #95 리뷰)
            var mine = sheet;
            mine.Locked = true;
            var button = saveButton;
            var cancel = cancelButton;
            button.SetEnabled(false);
            cancel.SetEnabled(false);
            int target = picked;
            WeeklyGoal got = null;
            bool ok = await Feedback.Run(async () => got = await Api.Users.PutWeeklyGoalAsync(new WeeklyGoalUpdate { TargetCount = target }),
                                         Save);
            saving = false;
            mine.Locked = false;
            if (Root.panel == null) return;
            button.SetEnabled(true);
            cancel.SetEnabled(true);
            if (!ok) return;
            mine.Close();
            if (got != null) Show(got);
            else Load(); // 빈 응답이면 다시 받아서(저장은 됨)
        }
    }
}
