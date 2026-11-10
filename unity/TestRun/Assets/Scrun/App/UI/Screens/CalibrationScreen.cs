using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-12–A-15 보정(가로). 순서는 CalibrationFlow가 정하고, 이 화면은 그 상태를 그린다.
    // A-12 거치 → (걸음을 기다림) → A-13 단계 재기 ⇄ A-14 단계 전환(버리는 5초 동안) → A-15 완료.
    // Figma에 없는 다시·도움·멈춤·그만두기 확인·저장 중은 같은 카드로 보여 준다(문구 CalibrationCopy).
    public sealed class CalibrationScreen : AppScreen
    {
        enum Card { None, Transition, Retry, Help, Stopped, Quit, Saving }

        readonly Action<bool> finish;
        readonly ISpeedSensor sensor;
        readonly Func<string, Task> save;

        CalibrationFlow flow;
        IVisualElementScheduledItem ticker;
        TimerRing ring;
        Label stageLabel, instruction, nextHint, badge, timer, timerSub;
        Label cardIcon, cardTitle, cardBody, cardChip;
        Button cancel, cardPrimary, cardSecondary;
        VisualElement segments, run, mount, cardLayer, cardButtons, done;
        Action primaryAction, secondaryAction;

        double now;
        double transitionUntil;
        bool transitionIsRetry;
        bool stopped, confirmQuit, finished;
        string nextHintText = "";
        int savedSleepTimeout;

        public CalibrationScreen(Action<bool> finish, ISpeedSensor sensor = null, Func<string, Task> save = null)
        {
            this.finish = finish ?? throw new ArgumentNullException(nameof(finish));
            this.sensor = sensor ?? Native.Speed;
            this.save = save ?? CalibrationStore.SaveAsync;
        }

        protected override string Uxml => "UI/Calibration";
        public override bool Landscape => true;
        public override bool HidesTabBar => true;

        static double Clock => Time.unscaledTimeAsDouble; // FakeSpeedSensor와 같은 시계(에디터 일시정지 중엔 멈춤)

        protected override void OnCreate()
        {
            segments = Q<VisualElement>("segments");
            for (int i = 0; i < CalibrationLadder.SpeedsKmh.Length; i++)
            {
                var seg = new VisualElement();
                seg.AddToClassList("calib-segment");
                segments.Add(seg);
            }
            stageLabel = Q<Label>("stage-label");
            cancel = Q<Button>("cancel");
            cancel.text = CalibrationCopy.Cancel;
            cancel.clicked += () =>
            {
                if (flow == null) Finish(false);
                else confirmQuit = true;
                Render();
            };

            run = Q<VisualElement>("run");
            instruction = Q<Label>("instruction");
            nextHint = Q<Label>("next-hint");
            badge = Q<Label>("badge");
            timer = Q<Label>("timer");
            timerSub = Q<Label>("timer-sub");
            ring = new TimerRing();
            Q<VisualElement>("ring-host").Insert(0, ring);

            mount = Q<VisualElement>("mount");
            Q<Label>("mount-title").text = CalibrationCopy.MountTitle;
            Q<Label>("mount-body").text = CalibrationCopy.MountBody;
            var mountOk = Q<Button>("mount-ok");
            mountOk.text = CalibrationCopy.MountOk;
            mountOk.clicked += StartFlow;
            var mountCancel = Q<Button>("mount-cancel");
            mountCancel.text = CalibrationCopy.Cancel;
            mountCancel.clicked += () => Finish(false);

            cardLayer = Q<VisualElement>("card-layer");
            cardIcon = Q<Label>("card-icon");
            cardTitle = Q<Label>("card-title");
            cardBody = Q<Label>("card-body");
            cardChip = Q<Label>("card-chip");
            cardButtons = Q<VisualElement>("card-buttons");
            cardPrimary = Q<Button>("card-primary");
            cardSecondary = Q<Button>("card-secondary");
            cardPrimary.clicked += () => primaryAction?.Invoke();
            cardSecondary.clicked += () => secondaryAction?.Invoke();

            done = Q<VisualElement>("done");
            Q<Label>("done-title").text = CalibrationCopy.DoneTitle;
            Q<Label>("sensor-label").text = CalibrationCopy.SensorLabel;
            Q<Label>("sensor-phone").text = CalibrationCopy.PhoneSensor;
            var next = Q<Button>("done-next");
            next.text = CalibrationCopy.DoneNext;
            next.clicked += () => Finish(true);

            Render();
        }

        protected internal override void OnShow()
        {
            savedSleepTimeout = Screen.sleepTimeout;
            Screen.sleepTimeout = SleepTimeout.NeverSleep; // 화면이 꺼지면 센서가 멈춰 단계가 끊김
            ticker ??= Root.schedule.Execute(Tick).Every(100);
            ticker.Resume();
#if !UNITY_EDITOR
            Application.focusChanged += OnFocus;
#endif
        }

        protected internal override void OnHide()
        {
            ticker?.Pause();
            Screen.sleepTimeout = savedSleepTimeout;
#if !UNITY_EDITOR
            Application.focusChanged -= OnFocus;
#endif
        }

        // 뒤로(에디터 Esc)로 닫혀도 모은 점은 버리고 센서를 돌려놓는다
        protected internal override void OnDestroy()
        {
            if (flow != null && flow.State != CalState.Done && flow.State != CalState.Saving) flow.Abort();
        }

        // 앱이 백그라운드로 가면 그 단계는 못 쓴다(센서가 멈춤)
        void OnFocus(bool focused)
        {
            if (!focused) flow?.Interrupt();
        }

        void StartFlow()
        {
            if (flow != null) return;
            flow = new CalibrationFlow(sensor, save);
            flow.Cue += OnCue;
            flow.StateChanged += Render;
            flow.Start(Clock);
            Render();
        }

        void Tick()
        {
            now = Clock;
            flow?.Tick(now);
            Render();
        }

        void OnCue(CalCue cue, float kmh)
        {
            CalibrationVoice.Play(cue, kmh);
            switch (cue)
            {
                case CalCue.NextSoon:
                    nextHintText = CalibrationCopy.NextSoon(kmh);
                    break;
                case CalCue.Go:
                case CalCue.Retry:
                    nextHintText = "";
                    transitionIsRetry = cue == CalCue.Retry;
                    transitionUntil = now + SpeedSensorBase.StageSkipSec;
                    break;
                case CalCue.Stopped:
                    stopped = true;
                    break;
                case CalCue.Prepare:
                case CalCue.Resume:
                    stopped = false;
                    break;
            }
            Render();
        }

        void Render()
        {
            if (Root?.panel == null && flow != null) return;
            var ladder = CalibrationLadder.SpeedsKmh;
            int index = flow?.StageIndex ?? 0;
            float kmh = ladder[index];

            for (int i = 0; i < segments.childCount; i++)
            {
                var seg = segments[i];
                bool isDone = flow != null && (i < index || flow.State == CalState.Saving || flow.State == CalState.Done);
                seg.EnableInClassList("calib-segment--done", isDone);
                seg.EnableInClassList("calib-segment--now", !isDone && i == index);
            }
            stageLabel.text = CalibrationCopy.StageLabel(index, ladder.Length);
            instruction.text = CalibrationCopy.Instruction(kmh);
            nextHint.text = nextHintText;

            bool measuring = flow?.State == CalState.Measuring;
            double remaining = measuring ? flow.StageRemaining : CalibrationLadder.StageSec;
            timer.text = $"0:{Math.Ceiling(remaining):00}";
            timerSub.text = $"/ 0:{CalibrationLadder.StageSec:00}";
            ring.Progress = measuring ? (float)(flow.StageElapsed / CalibrationLadder.StageSec) : 0f;

            var gait = measuring ? sensor.Current.Gait : Gait.None;
            badge.text = gait == Gait.Walking ? CalibrationCopy.BadgeWalking
                : gait == Gait.Running ? CalibrationCopy.BadgeRunning : CalibrationCopy.BadgeWaiting;
            badge.EnableInClassList("calib-badge--waiting", gait == Gait.None);

            Show(mount, flow == null);
            Show(done, flow?.State == CalState.Done);
            if (flow?.State == CalState.Done) FillDone();
            cancel.style.visibility = flow != null && (flow.State == CalState.Saving || flow.State == CalState.Done)
                ? Visibility.Hidden : Visibility.Visible;

            ShowCard(PickCard(), kmh, index);
        }

        Card PickCard()
        {
            if (flow == null) return Card.None;
            switch (flow.State)
            {
                case CalState.Saving: return Card.Saving;
                case CalState.Done:
                case CalState.Aborted: return Card.None;
                case CalState.Help: return confirmQuit ? Card.Quit : Card.Help;
            }
            if (confirmQuit) return Card.Quit;
            if (flow.State == CalState.WaitingSteps && stopped) return Card.Stopped;
            if (flow.State == CalState.Measuring && now < transitionUntil) return transitionIsRetry ? Card.Retry : Card.Transition;
            return Card.None;
        }

        void ShowCard(Card card, float kmh, int index)
        {
            Show(cardLayer, card != Card.None);
            if (card == Card.None) return;
            var ladder = CalibrationLadder.SpeedsKmh;
            int countdown = (int)Math.Ceiling(Math.Max(0, transitionUntil - now));
            var reason = flow.LastResult ?? CalStageResult.NoSteps;
            primaryAction = secondaryAction = null;
            string icon = "!", title = "", body = "", chip = null, primary = null, secondary = null;
            bool ok = false;

            switch (card)
            {
                case Card.Transition:
                    icon = "✓"; ok = true;
                    title = CalibrationCopy.StageDoneTitle(ladder[Math.Max(0, index - 1)]);
                    body = CalibrationCopy.StageDoneBody(kmh);
                    chip = CalibrationCopy.StartsIn(countdown, index, ladder.Length);
                    break;
                case Card.Retry:
                    title = CalibrationCopy.RetryTitle;
                    body = CalibrationCopy.Reason(reason, kmh);
                    chip = CalibrationCopy.StartsIn(countdown, index, ladder.Length);
                    break;
                case Card.Help:
                    title = CalibrationCopy.HelpTitle;
                    body = CalibrationCopy.Help(reason, kmh);
                    primary = CalibrationCopy.HelpAgain(kmh);
                    primaryAction = () => flow.RetryAfterHelp();
                    secondary = CalibrationCopy.Quit;
                    secondaryAction = Quit;
                    break;
                case Card.Stopped:
                    title = CalibrationCopy.StoppedTitle;
                    body = CalibrationCopy.StoppedBody(kmh);
                    secondary = CalibrationCopy.Quit;
                    secondaryAction = Quit;
                    break;
                case Card.Quit:
                    title = CalibrationCopy.QuitTitle;
                    body = CalibrationCopy.QuitBody;
                    primary = CalibrationCopy.Quit;
                    primaryAction = Quit;
                    secondary = CalibrationCopy.KeepGoing;
                    secondaryAction = () => { confirmQuit = false; Render(); };
                    break;
                case Card.Saving:
                    icon = "…";
                    title = CalibrationCopy.Saving;
                    break;
            }

            cardIcon.text = icon;
            cardIcon.EnableInClassList("calib-card-icon--ok", ok);
            cardIcon.EnableInClassList("calib-card-icon--warn", !ok && card != Card.Saving);
            cardTitle.text = title;
            cardBody.text = body;
            Show(cardBody, !string.IsNullOrEmpty(body));
            cardChip.text = chip ?? "";
            Show(cardChip, chip != null);
            cardPrimary.text = primary ?? "";
            cardSecondary.text = secondary ?? "";
            Show(cardPrimary, primary != null);
            Show(cardSecondary, secondary != null);
            Show(cardButtons, primary != null || secondary != null);
        }

        void FillDone()
        {
            var speeds = Q<VisualElement>("done-speeds");
            if (speeds.childCount == 0)
            {
                foreach (var v in CalibrationLadder.SpeedsKmh)
                {
                    var c = new Label("✓ " + CalibrationCopy.Kmh(v));
                    c.AddToClassList("calib-chip");
                    speeds.Add(c);
                }
            }
            var model = CalibrationModel.Parse(sensor.ExportCalibration());
            bool air = model != null && model.Points.All(p => p.AirAmp.HasValue);
            var airChip = Q<Label>("sensor-air");
            airChip.text = air ? CalibrationCopy.AirPodsOn : CalibrationCopy.AirPodsOff;
            airChip.EnableInClassList("calib-chip--gray", !air);
            var note = Q<Label>("save-note");
            note.text = flow.SavedToServer ? "" : CalibrationCopy.SavedLocalOnly;
            Show(note, !flow.SavedToServer);
        }

        void Quit()
        {
            flow?.Abort();
            Finish(false);
        }

        void Finish(bool ok)
        {
            if (finished) return;
            finished = true;
            ticker?.Pause();
            finish(ok);
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // A-13의 원형 타이머: 단계가 지나는 만큼 원이 채워진다
    sealed class TimerRing : VisualElement
    {
        static readonly Color Track = new Color(1f, 1f, 1f, 0.2f);
        static readonly Color Fill = new Color(0.843f, 0.969f, 0.357f); // #D7F75B
        float progress;

        public float Progress
        {
            get => progress;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, progress)) return;
                progress = value;
                MarkDirtyRepaint();
            }
        }

        public TimerRing()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - 8f;
            if (radius <= 0) return;
            var p = ctx.painter2D;
            p.lineWidth = 12f;
            p.lineCap = LineCap.Round;
            p.strokeColor = Track;
            p.BeginPath();
            p.Arc(r.center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
            if (progress <= 0f) return;
            p.strokeColor = Fill;
            p.BeginPath();
            p.Arc(r.center, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * progress));
            p.Stroke();
        }
    }
}
