using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-16 주간 목표 설정(Figma 599:351, 계획 0031). 새로 가입한 사람만 이어폰(A-09·A-10) 다음에 본다(Onboarding.Finish).
    // 주 1–7회(기본 3, 확정 11/06), "목표 정하기" = PUT /v1/users/me/weekly-goal → 온보딩 완료(A-17). "건너뛰기"도 A-17(홈에서 다시 유도).
    // 서버 저장(DB)은 지환 BL-30 — 그 전엔 서버가 예시 응답만 준다.
    public sealed class WeeklyGoalScreen : AppScreen
    {
        public const int Min = 1, Max = 7, Default = 3;

        readonly List<Button> chips = new List<Button>();
        Label count;
        Button save;
        bool saving, left;

        public int Count { get; private set; } = Default;

        protected override string Uxml => "UI/WeeklyGoal";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            Q<Button>("skip").clicked += GoDone;
            count = Q<Label>("count");
            save = Q<Button>("save");
            save.clicked += Save;
            var chipRow = Q<VisualElement>("chips");
            for (int n = Min; n <= Max; n++)
            {
                int value = n;
                var chip = new Button(() => Set(value)) { text = n.ToString() };
                chip.AddToClassList("goal-chip");
                chipRow.Add(chip);
                chips.Add(chip);
            }
            // 목표를 정한 날부터 7일(Figma: 1일–7일 빈 동그라미, 달린 날이 차는 자리)
            var days = Q<VisualElement>("days");
            for (int d = 1; d <= 7; d++)
            {
                var day = new VisualElement();
                day.AddToClassList("goal-day");
                var label = new Label(d + "일");
                label.AddToClassList("goal-day-label");
                var dot = new VisualElement();
                dot.AddToClassList("goal-day-dot");
                day.Add(label);
                day.Add(dot);
                days.Add(day);
            }
            Set(Default);
        }

        internal void Set(int n)
        {
            if (n < Min || n > Max) return;
            Count = n;
            count.text = n.ToString();
            for (int i = 0; i < chips.Count; i++) chips[i].EnableInClassList("goal-chip--on", i + Min == n);
        }

        async void Save()
        {
            if (saving || Nav.Current != this) return;
            saving = true;
            save.SetEnabled(false);
            int target = Count;
            bool ok = await Feedback.Run(() => Api.Users.PutWeeklyGoalAsync(new WeeklyGoalUpdate { TargetCount = target }), retry: Save);
            saving = false;
            if (Root.panel == null) return; // 그사이 로그인 화면 등으로
            save.SetEnabled(true);
            if (ok) GoDone();
        }

        void GoDone()
        {
            if (left || saving || Nav.Current != this) return;
            left = true;
            Nav.Push(new OnboardingDoneScreen());
        }

        protected internal override void OnShow()
        {
            left = false; // 완료 화면에서 뒤로 돌아오면 다시 고를 수 있게
            // 흐름의 첫 화면이면(앱을 다시 켜 A-16부터 이어짐) 뒤로 갈 곳이 없어 뒤로 버튼을 숨김
            Q<Button>("back").style.visibility = Nav.CanPop ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
