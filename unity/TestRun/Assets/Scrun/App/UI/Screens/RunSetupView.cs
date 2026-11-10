using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 맵 입장 뒤 흐린 맵 위 안내(계획 0026): D-01 러닝 설정 → D-05 거치 안내 → D-06 달리는 매뉴얼.
    // 새 화면(Navigator)이 아니라 MapScreen 안의 층이다: 섬 씬과 입장 자리를 그대로 쓰고, 뒤로 가면 한 단계씩 앞으로.
    // 끝나면 Done(고른 목표), D-01에서 뒤로 가거나 X면 Exit(맵 나가기). 서버에는 아직 아무것도 보내지 않는다(POST /runs는 S3 카운트다운 끝).
    public sealed class RunSetupView
    {
        public event Action<RunSetup> Done;
        public event Action Exit;

        readonly VisualElement root;
        readonly RunSetupPrefs prefs;
        readonly VisualElement goalStep, mountStep, manualStep, inputSheet, quickRow;
        readonly Button kindDistance, kindTime, minus, plus, inputOk;
        readonly Label value, unit, weekly, inputTitle, inputUnit, inputError;
        readonly TextField inputField;
        readonly Toggle skipManual;
        readonly Button[] quickChips;

        RunSetupSteps steps;
        RunGoal distance, time; // 탭을 오가도 각자 고친 값을 기억
        GoalKind kind;
        RunGoal? typed;         // 직접 입력에서 읽은 값
        int weeklyRequest;      // 늦게 온 응답이 새 안내를 덮지 않게

        public RunSetupView(VisualElement host, CourseDetail course, RunSetupPrefs prefs)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (course == null) throw new ArgumentNullException(nameof(course));
            this.prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));

            var tree = Resources.Load<VisualTreeAsset>("UI/RunSetup")
                       ?? throw new InvalidOperationException("UXML을 찾지 못함: Resources/UI/RunSetup.uxml");
            var container = tree.Instantiate();
            container.AddToClassList("run-setup");
            container.pickingMode = PickingMode.Ignore;
            root = container; // host에는 다 만든 뒤 붙인다(중간에 예외면 반쯤 만든 층이 맵 화면에 남지 않게)

            goalStep = Q<VisualElement>("goal-step");
            mountStep = Q<VisualElement>("mount-step");
            manualStep = Q<VisualElement>("manual-step");
            inputSheet = Q<VisualElement>("goal-input");
            quickRow = Q<VisualElement>("quick");
            kindDistance = Q<Button>("kind-distance");
            kindTime = Q<Button>("kind-time");
            minus = Q<Button>("minus");
            plus = Q<Button>("plus");
            value = Q<Label>("goal-value");
            unit = Q<Label>("goal-unit");
            weekly = Q<Label>("weekly-left");
            inputTitle = Q<Label>("input-title");
            inputUnit = Q<Label>("input-unit");
            inputError = Q<Label>("input-error");
            inputField = Q<TextField>("input-field");
            inputOk = Q<Button>("input-ok");
            skipManual = Q<Toggle>("skip-manual");

            Q<Label>("course-chip").text = RunSetupText.CourseChip(course.Name, course.RunnersNow);
            Q<Button>("close").clicked += () => Exit?.Invoke();
            kindDistance.clicked += () => SelectKind(GoalKind.Distance);
            kindTime.clicked += () => SelectKind(GoalKind.Time);
            minus.clicked += () => SetGoal(Current.Minus());
            plus.clicked += () => SetGoal(Current.Plus());
            Q<Button>("goal-next").clicked += OnGoalNext;
            Q<Button>("mount-back").clicked += () => Back();
            Q<Button>("mount-next").clicked += () => Go(steps.Next());
            Q<Button>("manual-back").clicked += () => Back();
            Q<Button>("manual-next").clicked += OnManualNext;

            inputField.keyboardType = TouchScreenKeyboardType.DecimalPad;
            inputField.RegisterValueChangedCallback(_ => ReadTyped());
            inputField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) ConfirmInput();
            });
            Q<Button>("input-cancel").clicked += CloseInput;
            inputOk.clicked += ConfirmInput;

            // 퀵 칩과 "직접 입력"은 한 번만 만들고, 종류·값이 바뀌면 글자와 chip--active만 바꾼다
            int quickCount = Math.Max(RunGoal.QuickUnits(GoalKind.Distance).Count, RunGoal.QuickUnits(GoalKind.Time).Count);
            quickChips = new Button[quickCount];
            for (int i = 0; i < quickCount; i++)
            {
                int index = i;
                var chip = new Button(() => OnQuick(index));
                chip.AddToClassList("chip");
                quickRow.Add(chip);
                quickChips[i] = chip;
            }
            var custom = new Button(OpenInput) { text = "직접 입력" };
            custom.AddToClassList("chip");
            quickRow.Add(custom);

            root.SetVisible(false);
            host.Add(root);
        }

        public bool Visible => root.IsVisible();

        RunGoal Current => kind == GoalKind.Distance ? distance : time;

        // 맵이 열리면 D-01부터. 마지막에 고른 목표로 시작
        public void Start()
        {
            var last = prefs.LastGoal();
            distance = prefs.LastGoal(GoalKind.Distance);
            time = prefs.LastGoal(GoalKind.Time);
            kind = last.Kind;
            steps = new RunSetupSteps(prefs.SkipManual);
            skipManual.SetValueWithoutNotify(false);
            inputSheet.SetVisible(false);
            RenderGoal();
            LoadWeekly();
            root.SetVisible(true);
            Go(steps.Current);
        }

        public void Hide()
        {
            weeklyRequest++;
            root.SetVisible(false);
        }

        // 뒤로 가기(버튼·Esc). 직접 입력이 열려 있으면 그것만 닫는다
        public void Back()
        {
            if (inputSheet.IsVisible())
            {
                CloseInput();
                return;
            }
            Go(steps.Back());
        }

        void Go(SetupStep step)
        {
            goalStep.SetVisible(step == SetupStep.Goal);
            mountStep.SetVisible(step == SetupStep.Mount);
            manualStep.SetVisible(step == SetupStep.Manual);
            if (step == SetupStep.Done)
            {
                Hide();
                Done?.Invoke(new RunSetup(Current));
            }
            else if (step == SetupStep.Exit)
            {
                Hide();
                Exit?.Invoke();
            }
        }

        void OnGoalNext()
        {
            prefs.Remember(Current);
            Go(steps.Next());
        }

        void OnManualNext()
        {
            if (skipManual.value) prefs.SkipManual = true;
            Go(steps.Next());
        }

        void SelectKind(GoalKind k)
        {
            if (kind == k) return;
            kind = k;
            RenderGoal();
        }

        void SetGoal(RunGoal g)
        {
            if (g.Kind == GoalKind.Distance) distance = g; else time = g;
            kind = g.Kind;
            RenderGoal();
        }

        void RenderGoal()
        {
            var g = Current;
            kindDistance.EnableInClassList("segment-item--selected", kind == GoalKind.Distance);
            kindTime.EnableInClassList("segment-item--selected", kind == GoalKind.Time);
            value.text = g.ValueText;
            unit.text = g.UnitText;
            minus.SetEnabled(g.CanMinus);
            plus.SetEnabled(g.CanPlus);

            var quick = RunGoal.QuickUnits(kind);
            for (int i = 0; i < quickChips.Length; i++)
            {
                var chip = quickChips[i];
                chip.SetVisible(i < quick.Count);
                if (i >= quick.Count) continue;
                chip.text = RunGoal.QuickText(kind, quick[i]);
                chip.EnableInClassList("chip--active", g.Units == quick[i]);
            }
        }

        void OnQuick(int index)
        {
            var quick = RunGoal.QuickUnits(kind);
            if (index < quick.Count) SetGoal(new RunGoal(kind, quick[index]));
        }

        // Feedback.Run으로 감싸지 않는 예외(docs/unity/unity_map.md): 이 줄은 곁들이는 안내라 로딩 덮개·오류창으로 설정을 막지 않고,
        // 못 불러오면 줄만 숨긴다. 로그인 만료만 공통 처리(맵 입장과 같음)
        async void LoadWeekly()
        {
            int request = ++weeklyRequest;
            weekly.SetVisible(false);
            try
            {
                var g = await Api.Users.GetWeeklyGoalAsync();
                if (request != weeklyRequest) return;
                var line = RunSetupText.WeeklyLeft(g);
                weekly.text = line ?? "";
                weekly.SetVisible(line != null);
            }
            catch (ApiException e)
            {
                if (request != weeklyRequest) return;
                if (e.Failure == ApiFailure.NotLoggedIn) { Feedback.Handle(e); return; } // 로그인 화면으로
                Debug.LogWarning("[Api] 주간 목표를 못 불러옴(러닝 설정은 계속): " + e);
            }
            catch (Exception e)
            {
                // async void라 여기서 놓치면 줄이 숨은 채 원인을 모름. 기록만 하고 설정은 계속
                if (request == weeklyRequest) Debug.LogException(e);
            }
        }

        // ---- 직접 입력 ----
        void OpenInput()
        {
            inputTitle.text = RunSetupText.InputTitle(kind);
            inputUnit.text = RunGoal.UnitOf(kind);
            inputField.keyboardType = kind == GoalKind.Distance ? TouchScreenKeyboardType.DecimalPad : TouchScreenKeyboardType.NumberPad;
            inputField.SetValueWithoutNotify(Current.ValueText);
            ReadTyped();
            inputSheet.SetVisible(true);
            inputField.schedule.Execute(() =>
            {
                inputField.Focus();
                inputField.SelectAll();
            });
        }

        void ReadTyped()
        {
            typed = RunGoal.TryParse(kind, inputField.value, out var g, out var problem) ? g : (RunGoal?)null;
            inputError.text = problem ?? "";
            inputOk.SetEnabled(typed.HasValue);
        }

        void ConfirmInput()
        {
            if (!typed.HasValue) return;
            SetGoal(typed.Value);
            CloseInput();
        }

        void CloseInput()
        {
            inputField.Blur();
            inputSheet.SetVisible(false);
        }

        T Q<T>(string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException($"RunSetup.uxml에 '{name}'({typeof(T).Name})이 없음");
    }
}
