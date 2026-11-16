using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 러닝 설정 뒤 달리기 시작까지(계획 0035, BL-10·11): D-07 센서 확인 → D-08 달림 인식 대기 → D-09 3·2·1, 폰 센서 실패면 I-01.
    // RunSetupView처럼 MapScreen 안의 층이다. 단계 판단은 RunReadyFlow(테스트), 여기는 입력 모으기·그리기·버튼만.
    // 흐림은 MapScreen이 Blur(true/false)를 받아 MapBackdrop으로 한다: D-07·D-08·I-01은 흐림, D-09는 맵이 보임.
    // 센서는 D-07에 들어올 때 켠다(섬 쪽 NativeSpeedSource가 이미 켰으면 빌려 씀). 여기서 켠 것만 취소·맵 나가기 때 끈다.
    public sealed class RunReadyView : IDisposable
    {
        public event Action Started;      // 3·2·1이 끝남 → MapScreen.StartRunning()
        public event Action Cancelled;    // X·뒤로 → D-01 러닝 설정(정한 것 3)
        public event Action NeedsCalibration; // 쓸 수 있는 보정이 없음 → 맵을 나가 보정 안내
        public event Action<bool> Blur;   // 흐린 배경을 켜고 끔

        readonly VisualElement root;
        readonly VisualElement checkStep, waitStep, countStep, failStep;
        readonly Row phoneRow, earphoneRow, calibrationRow, networkRow;
        readonly Label goalChip, countNumber;
        readonly Button ready;
        readonly Func<IRunTogether> together;
        readonly Func<CalibrationState> calibration;
        readonly Action retryCalibration;
        readonly Func<double> clock;
        readonly RunReadyFlow flow = new RunReadyFlow();
        // 걷는 중 판단: 섬 아바타(NativeSpeedSource)와 같은 규칙(AvatarSpeed, 계획 0034)으로 같은 센서 값을 따로 셈.
        // 카운트다운이 도는데 아바타는 서 있는(또는 그 반대) 일이 없게(PR #49 리뷰 3번)
        readonly AvatarSpeed walking = new AvatarSpeed();

        IVisualElementScheduledItem ticking;
        ReadyStep? shown;      // 마지막으로 그린 단계(처음엔 없음)
        bool sensorStartedHere, sensorBroken;
        ReadyInputs last;

        // together: 지금 실시간 방(없으면 null). calibration: 보정값 상태, retryCalibration: 보정 다시 불러오기(MapScreen)
        public RunReadyView(VisualElement host, string courseName, Func<IRunTogether> together,
                            Func<CalibrationState> calibration, Action retryCalibration, Func<double> clock = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            this.together = together ?? (() => null);
            this.calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
            this.retryCalibration = retryCalibration;
            this.clock = clock ?? RealtimeClock.Now;

            var tree = Resources.Load<VisualTreeAsset>("UI/RunReady")
                       ?? throw new InvalidOperationException("UXML을 찾지 못함: Resources/UI/RunReady.uxml");
            var container = tree.Instantiate();
            container.AddToClassList("run-ready");
            container.pickingMode = PickingMode.Ignore;
            root = container; // host에는 다 만든 뒤 붙인다(RunSetupView와 같음)

            checkStep = Q<VisualElement>("check-step");
            waitStep = Q<VisualElement>("wait-step");
            countStep = Q<VisualElement>("count-step");
            failStep = Q<VisualElement>("fail-step");
            phoneRow = new Row(Q<VisualElement>("row-phone"));
            earphoneRow = new Row(Q<VisualElement>("row-earphone"));
            calibrationRow = new Row(Q<VisualElement>("row-calibration"));
            networkRow = new Row(Q<VisualElement>("row-network"));
            goalChip = Q<Label>("goal-chip");
            countNumber = Q<Label>("count-number");
            ready = Q<Button>("ready");

            Q<Label>("course-chip").text = courseName ?? "";
            Q<Label>("count-chip-text").text = RunReadyText.CountdownChip;
            ready.clicked += () =>
            {
                if (flow.Skip(last)) Render();
            };
            calibrationRow.Chip.clicked += () =>
            {
                if (RunReadyFlow.CanRetryCalibration(calibration())) retryCalibration?.Invoke();
            };
            // 나가는 길: D-07·D-08·I-01 모두 X → D-01(아이폰엔 뒤로 키가 없음, 리뷰). D-09는 3초라 X 없음(뒤로 가기는 됨)
            Q<Button>("check-close").clicked += Cancel;
            Q<Button>("wait-close").clicked += Cancel;
            Q<Button>("fail-close").clicked += Cancel;
            Q<Button>("open-settings").clicked += () => Native.Permissions.OpenSettings();
            Q<Button>("retry").clicked += Retry;

            root.SetVisible(false);
            host.Add(root);
        }

        public bool Visible => root.IsVisible();

        // D-06 "시작할게요" 뒤. 맵 위에 D-07부터
        public void Start(RunGoal goal)
        {
            goalChip.text = RunReadyText.GoalChip(goal);
            StartSensor();
            walking.Reset(); // 센서를 새로 켜면 걸음 수도 0부터
            flow.Begin(clock());
            shown = null;
            root.SetVisible(true);
            Tick();
            ticking ??= root.schedule.Execute(Tick).Every(33); // 숫자가 1초마다 바뀌니 30번이면 충분
            ticking.Resume();
        }

        // 뒤로 가기(Esc·안드로이드 뒤로). 아직 시작 전이면 D-01로
        public void Back() => Cancel();

        public void Dispose()
        {
            ticking?.Pause();
            StopSensor();
            root.RemoveFromHierarchy();
        }

        void Cancel()
        {
            bool blurred = RunReadyFlow.Blurred(flow.Step);
            if (!flow.Cancel()) return;
            Close(blurOff: !blurred); // 흐린 배경은 D-01이 이어서 씀
            StopSensor();
            Cancelled?.Invoke();
        }

        // I-01 "다시 시도": 센서를 껐다 켜고 D-07부터. 섬 쪽이 켜서 빌려 쓰던 센서라도 다시 켜 둔 채로 끝나므로
        // "켠 쪽이 끈다"(계획 0020)와 어긋나지 않는다: 끈 채로 두지 않고, 끄는 일은 여전히 섬 쪽(NativeSpeedSource)이 함
        void Retry()
        {
            try
            {
                var s = Native.Speed;
                bool mine = sensorStartedHere || !s.IsRunning;
                s.Stop();
                s.Start();
                sensorStartedHere = mine;
                sensorBroken = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[센서 준비] 센서를 다시 켜지 못함: " + e.Message);
                sensorBroken = true;
            }
            walking.Reset(); // 센서를 새로 켜면 걸음 수도 0부터
            flow.Retry(clock());
            Tick();
        }

        void StartSensor()
        {
            sensorBroken = false;
            try
            {
                var s = Native.Speed;
                if (s.IsRunning) return; // 섬 쪽이 켬 → 빌려 씀
                s.Start();
                sensorStartedHere = true;
            }
            catch (Exception e)
            {
                // 진짜 센서가 없는 빌드 등 → I-01(5초 기다리지 않고 바로)
                Debug.LogWarning("[센서 준비] 센서를 켜지 못함: " + e.Message);
                sensorBroken = true;
            }
        }

        void StopSensor()
        {
            if (!sensorStartedHere) return;
            sensorStartedHere = false;
            try { Native.Speed.Stop(); }
            catch (Exception e) { Debug.LogWarning("[센서 준비] 센서를 끄지 못함: " + e.Message); }
        }

        void Close(bool blurOff = true)
        {
            ticking?.Pause();
            root.SetVisible(false);
            if (blurOff) Blur?.Invoke(false);
        }

        ReadyInputs Read()
        {
            var i = new ReadyInputs { Calibration = calibration(), Phone = SensorFlow.Lost };
            if (!sensorBroken)
            {
                try
                {
                    var s = Native.Speed;
                    i.Phone = s.Phone;
                    i.AirPods = s.AirPods;
                    walking.Update(s.Current, s.Phone == SensorFlow.Ok);
                    i.Stepping = walking.Stepping;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[센서 준비] 센서 값을 못 읽음: " + e.Message);
                    sensorBroken = true;
                }
            }
            if (flow.Step != ReadyStep.Checking) return i; // 이어폰·네트워크 줄은 D-07에만 보임
            try
            {
                i.Headphones = Native.Headphones.Status;
                i.HeadphoneMotionGranted = Native.Permissions.GetStatus(PermissionKind.HeadphoneMotion) == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                i.Headphones = HeadphoneStatus.NotConnected; // 이어폰은 막지 않음
            }
            i.Network = together()?.Status;
            return i;
        }

        void Tick()
        {
            last = Read();
            var before = flow.Step;
            flow.Update(clock(), last);
            Render();
            if (before == flow.Step) return;
            if (flow.Step == ReadyStep.Started)
            {
                Close();
                Started?.Invoke(); // 센서는 켜 둠(달리는 동안 씀). 맵을 나가면 Dispose가 끔
            }
            else if (flow.Step == ReadyStep.NeedsCalibration)
            {
                Close();
                StopSensor();
                NeedsCalibration?.Invoke();
            }
        }

        void Render()
        {
            var step = flow.Step;
            if (step != shown)
            {
                checkStep.SetVisible(step == ReadyStep.Checking);
                waitStep.SetVisible(step == ReadyStep.Waiting);
                countStep.SetVisible(step == ReadyStep.Countdown);
                failStep.SetVisible(step == ReadyStep.SensorFailed);
                bool blurNow = RunReadyFlow.Blurred(step);
                if (shown == null || blurNow != RunReadyFlow.Blurred(shown.Value)) Blur?.Invoke(blurNow);
                shown = step;
            }
            if (step == ReadyStep.Checking)
            {
                phoneRow.Show(RunReadyText.Phone(last.Phone));
                earphoneRow.Show(RunReadyText.Earphone(last.Headphones, last.HeadphoneMotionGranted, last.AirPods));
                calibrationRow.Show(RunReadyText.Calibration(last.Calibration), RunReadyFlow.CanRetryCalibration(last.Calibration));
                networkRow.Show(RunReadyText.Network(last.Network));
                ready.SetEnabled(RunReadyFlow.CanProceed(last));
            }
            else if (step == ReadyStep.Countdown)
            {
                countNumber.text = flow.CountdownNumber.ToString();
            }
        }

        T Q<T>(string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException($"RunReady.uxml에 '{name}'({typeof(T).Name})이 없음");

        // D-07 한 줄: 아래 작은 글자 + 상태 칩
        sealed class Row
        {
            static readonly string[] Tones = { "check-chip--ok", "check-chip--pending", "check-chip--fallback", "check-chip--failed" };
            readonly Label sub;
            public readonly Button Chip;

            public Row(VisualElement row)
            {
                sub = row.Q<Label>("sub") ?? throw new InvalidOperationException($"{row.name}에 sub가 없음");
                Chip = row.Q<Button>("chip") ?? throw new InvalidOperationException($"{row.name}에 chip이 없음");
            }

            public void Show(ReadyRow r, bool clickable = false)
            {
                sub.text = r.Sub;
                Chip.text = r.Chip;
                for (int t = 0; t < Tones.Length; t++) Chip.EnableInClassList(Tones[t], t == (int)r.Tone);
                Chip.pickingMode = clickable ? PickingMode.Position : PickingMode.Ignore;
            }
        }
    }
}
