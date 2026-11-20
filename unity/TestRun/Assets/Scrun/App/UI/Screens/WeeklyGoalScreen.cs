using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-16 주간 목표 설정(계획 0031, 모양은 우주 시안 886:125 계획 0061: −/+ 단계·7일 궤도). 새로 가입한 사람만 이어폰(A-09·A-10) 다음에 본다(Onboarding.Finish).
    // 주 1–7회(기본 3, 확정 11/06), "목표 정하기" = PUT /v1/users/me/weekly-goal → 온보딩 완료(A-17). "건너뛰기"도 A-17(홈에서 다시 유도).
    // 서버가 오늘을 설정일로 저장(계획 0054). 나중에 바꾸기는 마이 탭 주간 목표(H-07·H-08), 다음 주기부터 적용.
    public sealed class WeeklyGoalScreen : AppScreen
    {
        public const int Min = WeeklyGoalText.Min, Max = WeeklyGoalText.Max, Default = WeeklyGoalText.Default; // H-08 시트와 같은 범위

        Label count;
        Button save, minus, plus;
        bool saving, left;

        public int Count { get; private set; } = Default;

        protected override string Uxml => "UI/WeeklyGoal";
        public override bool HidesTabBar => true;
        public override string RootClass => SpaceArt.RootClass;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            Q<Button>("skip").clicked += GoDone;
            count = Q<Label>("count");
            save = Q<Button>("save");
            save.clicked += Save;
            minus = Q<Button>("minus");
            plus = Q<Button>("plus");
            minus.clicked += () => Set(Count - 1);
            plus.clicked += () => Set(Count + 1);
            SpaceArt.Fit(Q<VisualElement>("fit"));
            // 목표를 정한 날부터 7일 궤도(오늘 = 1일만 빛남, 나머지는 달린 날이 차는 자리)
            var days = Q<VisualElement>("days");
            for (int d = 1; d <= 7; d++)
            {
                var day = new VisualElement();
                day.AddToClassList("week-day");
                day.EnableInClassList("week-day--today", d == 1);
                var dot = new VisualElement();
                dot.AddToClassList("week-dot");
                var label = new Label(d + "일");
                label.AddToClassList("week-day-label");
                day.Add(dot);
                day.Add(label);
                days.Add(day);
            }
            Set(Default);
        }

        internal void Set(int n)
        {
            if (saving || n < Min || n > Max) return; // 보내는 중엔 화면 숫자가 보낸 값과 달라지지 않게
            Count = n;
            count.text = n.ToString();
            RefreshStepper();
        }

        void RefreshStepper()
        {
            minus.SetEnabled(!saving && Count > Min);
            plus.SetEnabled(!saving && Count < Max);
        }

        async void Save()
        {
            if (saving || Nav.Current != this) return;
            saving = true;
            save.SetEnabled(false);
            RefreshStepper();
            int target = Count;
            bool ok = await Feedback.Run(() => Api.Users.PutWeeklyGoalAsync(new WeeklyGoalUpdate { TargetCount = target }), retry: Save);
            saving = false;
            if (Root.panel == null) return; // 그사이 로그인 화면 등으로
            save.SetEnabled(true);
            RefreshStepper();
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
