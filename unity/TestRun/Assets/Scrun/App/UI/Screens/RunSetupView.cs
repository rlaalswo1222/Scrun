using System;
using System.Collections.Generic;
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

        // 고스트러너(계획 0050, D-01–D-04). 켜면 가장 최근 기록을 바로 고르고(정할 것 1 가) "변경"으로 바꿈. 미인증도 고를 수 있음(2 가)
        readonly VisualElement ghostSwitch, ghostPicked, ghostPick, ghostEmpty;
        readonly ScrollView ghostList;
        readonly Label ghostDate, ghostSummary, ghostNote;
        readonly Button ghostPickOk;
        bool ghostOn, ghostLoading;
        RunSummary ghostRun, pickSel;
        List<RunSummary> runs;   // 내 끝난 기록(최신순, 받은 만큼). null이면 아직 안 받음. 맵을 열 때마다 이 화면이 새로 만들어져 새로 받음
        string runsCursor;
        int ghostRequest;

        // ghost: 기록 상세 "이 기록으로 고스트 달리기"(계획 0053)로 왔으면 그 기록을 고른 채 고스트 카드를 켜 둠(바꾸기·끄기는 그대로)
        public RunSetupView(VisualElement host, CourseDetail course, RunSetupPrefs prefs, RunSummary ghost = null)
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
            ghostSwitch = Q<VisualElement>("ghost-switch");
            ghostPicked = Q<VisualElement>("ghost-picked");
            ghostPick = Q<VisualElement>("ghost-pick");
            ghostEmpty = Q<VisualElement>("ghost-empty");
            ghostList = Q<ScrollView>("ghost-list");
            ghostDate = Q<Label>("ghost-date");
            ghostSummary = Q<Label>("ghost-summary");
            ghostNote = Q<Label>("ghost-note");
            ghostPickOk = Q<Button>("ghost-pick-ok");
            Q<Label>("ghost-lead").text = GhostText.CardLead;
            Q<Label>("ghost-pick-title").text = GhostText.PickTitle;
            ghostPickOk.text = GhostText.PickDone;
            Q<Label>("ghost-empty-title").text = GhostText.EmptyTitle;
            Q<Label>("ghost-empty-body").text = GhostText.EmptyBody;
            Q<Button>("ghost-empty-enter").text = GhostText.EmptyEnter;

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
            Q<Button>("ghost-toggle").clicked += ToggleGhost;
            Q<Button>("ghost-change").clicked += OpenPick;
            Q<Button>("ghost-pick-cancel").clicked += () => ghostPick.SetVisible(false);
            ghostPickOk.clicked += ConfirmPick;
            Q<Button>("ghost-empty-back").clicked += () => ghostEmpty.SetVisible(false);
            Q<Button>("ghost-empty-enter").clicked += () =>
            {
                ghostEmpty.SetVisible(false);
                if (steps.Current == SetupStep.Goal) OnGoalNext(); // D-04 → D-05
            };

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

            ghostPick.SetVisible(false);
            ghostEmpty.SetVisible(false);
            if (ghost != null)
            {
                ghostRun = ghost;
                ghostOn = true;
            }
            RenderGhost();
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
            // 고스트 선택은 그대로 둠: 센서 준비에서 X로 돌아와도(목표처럼) 고른 기록이 남게. Hide가 끊은 목록 요청만 정리
            ghostRequest++;
            ghostLoading = false;
            ghostPick.SetVisible(false);
            ghostEmpty.SetVisible(false);
            RenderGhost();
            RenderGoal();
            LoadWeekly();
            root.SetVisible(true);
            Go(steps.Current);
        }

        public void Hide()
        {
            weeklyRequest++;
            ghostRequest++;
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
            if (ghostPick.IsVisible() || ghostEmpty.IsVisible())
            {
                ghostPick.SetVisible(false);
                ghostEmpty.SetVisible(false);
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
                Done?.Invoke(new RunSetup(Current, ghostOn ? ghostRun?.RunId : null));
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

        // ---- 고스트러너(계획 0050) ----
        void ToggleGhost()
        {
            if (ghostOn)
            {
                ghostOn = false;
                RenderGhost();
                return;
            }
            if (runs == null) LoadRuns(turnOn: true);
            else TurnOn();
        }

        void TurnOn()
        {
            if (runs.Count == 0)
            {
                ghostEmpty.SetVisible(true); // D-04
                return;
            }
            ghostRun ??= runs[0]; // 가장 최근(정할 것 1 가)
            ghostOn = true;
            RenderGhost();
        }

        // 곁들이는 기능이라 로딩 덮개·오류창으로 설정을 막지 않음(주간 목표 줄과 같은 이유). 못 받으면 카드에 안내.
        // 기다리는 동안 다음 단계로 넘어갔으면 늦게 온 목록으로 D-04를 띄우거나 켜지 않음(그 단계 위에 시트가 뜨고 "고스트 없이 입장"이 엉뚱한 단계를 넘김)
        async void LoadRuns(bool turnOn, bool more = false, bool openPick = false)
        {
            if (ghostLoading) return;
            ghostLoading = true;
            int request = ++ghostRequest;
            ghostNote.text = "기록을 불러오는 중이에요";
            string failed = null;
            try
            {
                var page = await Api.Runs.ListAsync(more ? runsCursor : null);
                if (request != ghostRequest) return;
                if (!more || runs == null) runs = new List<RunSummary>();
                foreach (var r in page.Items ?? new List<RunSummary>())
                    if (!runs.Exists(x => x.RunId == r.RunId)) runs.Add(r); // 맨 위에 넣어 둔 미리 고른 기록과 겹치지 않게
                // 기록 상세에서 고른 기록이 첫 쪽 밖(옛 기록)이면 맨 위에 넣어 D-03에서 고른 줄이 보이게(계획 0053 리뷰)
                if (!more && ghostRun != null && !runs.Exists(x => x.RunId == ghostRun.RunId)) runs.Insert(0, ghostRun);
                runsCursor = page.NextCursor;
                ghostLoading = false;
                if (turnOn && steps.Current == SetupStep.Goal) TurnOn();
                if (openPick && steps.Current == SetupStep.Goal) OpenPick();
                if (more && ghostPick.IsVisible()) BuildPick();
            }
            catch (ApiException e)
            {
                if (request != ghostRequest) return;
                if (e.Failure == ApiFailure.NotLoggedIn) Feedback.Handle(e);
                else
                {
                    Debug.LogWarning("[고스트] 기록 목록을 못 받음: " + e);
                    failed = "기록을 불러오지 못했어요. 다시 눌러 주세요"; // 임시
                }
            }
            catch (Exception e)
            {
                if (request != ghostRequest) return;
                Debug.LogException(e);
                failed = "기록을 불러오지 못했어요. 다시 눌러 주세요"; // 임시
            }
            finally
            {
                if (request == ghostRequest) ghostLoading = false;
            }
            RenderGhost(); // "불러오는 중"을 실패·로그아웃에서도 지움
            if (failed != null && !ghostOn) ghostNote.text = failed;
            else if (failed != null && openPick) Feedback.ShowError("기록을 불러오지 못했어요", "잠시 후 \"변경\"을 다시 눌러 주세요."); // 고른 기록이 보여 안내 줄이 숨은 때(임시 문구)
        }

        void RenderGhost()
        {
            ghostSwitch.EnableInClassList("ghost-switch--on", ghostOn);
            bool picked = ghostOn && ghostRun != null;
            ghostPicked.SetVisible(picked);
            ghostNote.SetVisible(!picked);
            if (!ghostLoading) ghostNote.text = GhostText.OffNote;
            if (!picked) return;
            ghostDate.text = FriendText.RunDate(ghostRun.StartedAt);
            ghostSummary.text = GhostText.Summary(ghostRun);
        }

        // D-03: 내 기록 줄(날짜·거리·인증 칩·시간·평균 페이스), 고르면 테두리, "선택 완료"
        void OpenPick()
        {
            if (runs == null) // 기록 상세에서 고스트를 골라 왔으면(계획 0053) 목록을 아직 안 받음 → 받고 열기
            {
                LoadRuns(turnOn: false, openPick: true);
                return;
            }
            if (runs.Count == 0) return;
            pickSel = ghostRun ?? runs[0];
            BuildPick();
            ghostPick.SetVisible(true);
        }

        void BuildPick()
        {
            ghostList.Clear();
            foreach (var r in runs)
            {
                var run = r;
                var row = new Button(() => Select(run)) { name = "ghost-run-" + r.RunId };
                row.AddToClassList("ghost-row");
                row.EnableInClassList("ghost-row--selected", pickSel?.RunId == r.RunId);
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("ghost-row-text");
                text.Add(Label(FriendText.RunDate(r.StartedAt), "ghost-row-top"));
                text.Add(Label(FriendText.KmShort(r.DistanceM), "ghost-row-km"));
                text.Add(Label(GhostText.Detail(r), "ghost-row-sub"));
                row.Add(text);
                var chip = FriendText.VerificationChip(r.Verification);
                if (chip != null)
                {
                    var c = Label(chip.Value.text, "ghost-row-chip");
                    c.EnableInClassList("ghost-row-chip--ok", chip.Value.verified);
                    row.Add(c);
                }
                ghostList.Add(row);
            }
            if (runsCursor != null)
            {
                var more = new Button(() => LoadRuns(turnOn: false, more: true)) { text = "더 보기", name = "ghost-more" };
                more.AddToClassList("btn");
                more.AddToClassList("btn-ghost");
                ghostList.Add(more);
            }
            ghostPickOk.SetEnabled(pickSel != null);
        }

        // 고른 줄만 테두리를 옮김(줄을 다 다시 만들면 "더 보기" 몇 번 뒤 누를 때마다 끊기고 스크롤이 처음으로 감)
        void Select(RunSummary run)
        {
            if (pickSel != null) ghostList.Q("ghost-run-" + pickSel.RunId)?.RemoveFromClassList("ghost-row--selected");
            pickSel = run;
            ghostList.Q("ghost-run-" + run.RunId)?.AddToClassList("ghost-row--selected");
            ghostPickOk.SetEnabled(true);
        }

        void ConfirmPick()
        {
            if (pickSel == null) return;
            ghostRun = pickSel;
            ghostOn = true;
            ghostPick.SetVisible(false);
            RenderGhost();
        }

        static Label Label(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            return l;
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
