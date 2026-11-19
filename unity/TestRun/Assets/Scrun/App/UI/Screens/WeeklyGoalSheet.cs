using System;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-08 주간 목표 정하기·바꾸기 시트(계획 0054, Figma 610:2485): −/+ · 1–7 칩 · 안내 · 저장·취소.
    // H-07 현황(WeeklyGoalStatusScreen)과 홈 B-02 "목표 정하기"(계획 0057)가 같이 쓴다. 저장 = PUT(응답이 GET과 같음).
    // 모양은 WeeklyStatus.uss + Friends.uss(시트) — 띄우는 화면 UXML이 둘 다 불러야 함
    public sealed class WeeklyGoalSheet
    {
        readonly FriendParts.Sheet sheet;
        readonly Action<WeeklyGoal> onSaved;
        readonly Label number;
        readonly Button minus, plus, saveButton, cancelButton;
        readonly VisualElement chips;
        int picked;

        public bool Open => sheet.Open;
        public bool Saving { get; private set; }
        public int Picked => picked;

        // goal: 지금 목표(없으면 TargetCount null = 처음 정하기). onSaved: 저장한 뒤 서버 응답(빈 응답이면 null → 부른 쪽이 다시 받음)
        public WeeklyGoalSheet(VisualElement screenRoot, WeeklyGoal goal, Action<WeeklyGoal> onSaved)
        {
            this.onSaved = onSaved;
            sheet = new FriendParts.Sheet(screenRoot);
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
            number = UiExtensions.Text("", "wg-step-number");
            number.name = "goal-number";
            value.Add(number);
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

        // 뒤로 가기: 저장 중엔 닫지 않음(실패하면 그 시트에서 다시 시도)
        public void Close()
        {
            if (!Saving) sheet.Close();
        }

        internal void Pick(int n)
        {
            picked = WeeklyGoalText.Clamp(n);
            number.text = picked.ToString();
            minus.SetEnabled(picked > WeeklyGoalText.Min);
            plus.SetEnabled(picked < WeeklyGoalText.Max);
            for (int i = 0; i < chips.childCount; i++) chips[i].EnableInClassList("wg-chip--on", i + WeeklyGoalText.Min == picked);
        }

        async void Save()
        {
            if (Saving || !Open) return;
            Saving = true;
            // 저장 중엔 바깥·뒤로·취소로 닫히지 않게(닫힌 뒤 실패하면 오류창의 "다시 시도"가 아무것도 안 하던 것, #95 리뷰)
            sheet.Locked = true;
            saveButton.SetEnabled(false);
            cancelButton.SetEnabled(false);
            int target = picked;
            WeeklyGoal got = null;
            bool ok = await Feedback.Run(async () => got = await Api.Users.PutWeeklyGoalAsync(new WeeklyGoalUpdate { TargetCount = target }),
                                         Save);
            Saving = false;
            sheet.Locked = false;
            saveButton.SetEnabled(true);
            cancelButton.SetEnabled(true);
            if (!ok || sheet.Dim.panel == null) return; // 실패(오류창에서 다시 시도) 또는 그새 화면을 떠남
            sheet.Close();
            onSaved?.Invoke(got);
        }
    }
}
