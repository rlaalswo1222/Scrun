using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Scrun.App
{
    public enum CalState
    {
        Idle,         // 시작 전
        WaitingSteps, // 걸음을 기다림(처음, 멈췄다가, 도움 화면 뒤). 걸음이 잡히면 지금 단계를 잼
        Measuring,    // 단계를 재는 중
        Help,         // 같은 단계가 연달아 실패 → 원인 안내, 사용자가 "다시"
        Saving,       // 7점이 다 모여 확정하고 서버에 올리는 중
        Done,
        Aborted,      // 사용자가 그만둠(저장 안 함, 지금 보정은 그대로)
    }

    // 화면 글자·소리를 바꿀 때(kmh = 그때 맞출 속도)
    public enum CalCue
    {
        Prepare,  // "러닝머신을 N km/h로 맞추고 걸으세요"(처음, 도움 뒤)
        NextSoon, // 단계 끝 몇 초 전 "곧 N km/h로 올립니다"
        Go,       // "지금 N km/h로 올리세요"(다음 단계 시작)
        Retry,    // 그 단계 실패 → "다시 잴게요"(이유는 LastResult)
        Help,     // 연달아 실패 → 도움 화면(이유는 LastResult)
        Stopped,  // 재는 중에 걸음이 멈춤 → 다시 걸으면 이어서
        Resume,   // 기다리다 걸음이 잡혀 지금 단계를 다시 잼
        Done,
    }

    // 처음 보정 순서(계획 0023). 화면과 떨어져 있어 에디터 테스트로 확인한다.
    // 모두에게 같은 사다리(CalibrationLadder)를 오름으로 한 단계씩 재고, 실패한 단계는 제대로 잴 때까지 다시 한다(건너뛰지 않음).
    // 화면은 매 프레임 Tick(시각)을 부르고, State·StageRemaining을 그리고, Cue로 소리·문구를 바꾼다.
    //   var flow = new CalibrationFlow(Native.Speed, model => Api.Calibration.SaveAsync(model));
    //   flow.Cue += (cue, kmh) => ...;  flow.Start(Time.realtimeSinceStartupAsDouble);
    public sealed class CalibrationFlow
    {
        public const float NextSoonSec = 3f;   // 단계 끝 이만큼 전에 다음 속도 예고
        public const float StoppedSec = 6f;    // 재는 중 걸음이 이만큼 없으면 멈춘 것(실패로 안 셈)
        public const int HelpAfterFails = 2;   // 같은 단계가 이만큼 연달아 실패하면 도움 화면

        readonly ISpeedSensor sensor;
        readonly Func<string, Task> save;
        readonly IReadOnlyList<float> ladder;
        readonly float stageSec;

        bool startedSensor;
        double now, startedAt, stageStart, noStepsSince = -1;
        bool nextSoonSent;

        public CalState State { get; private set; } = CalState.Idle;
        public int StageIndex { get; private set; }
        public int StageCount => ladder.Count;
        public float StageKmh => ladder[StageIndex];
        public int Fails { get; private set; }               // 지금 단계에서 연달아 실패한 수
        public CalStageResult? LastResult { get; private set; }
        public bool SavedToServer { get; private set; }      // Done일 때: 서버 저장 성공(실패면 다음에 다시 올림, 4번)
        public Task SaveTask { get; private set; }
        public double StageElapsed => State == CalState.Measuring ? now - stageStart : 0;
        public double StageRemaining => State == CalState.Measuring ? Math.Max(0, stageSec - (now - stageStart)) : stageSec;
        public double TotalElapsed => State == CalState.Idle ? 0 : now - startedAt;

        public event Action StateChanged;
        public event Action<CalCue, float> Cue;

        public CalibrationFlow(ISpeedSensor sensor, Func<string, Task> save,
            IReadOnlyList<float> ladder = null, float stageSec = CalibrationLadder.StageSec)
        {
            this.sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
            this.save = save ?? throw new ArgumentNullException(nameof(save));
            this.ladder = ladder ?? CalibrationLadder.SpeedsKmh;
            this.stageSec = stageSec;
            if (this.ladder.Count == 0) throw new ArgumentException("사다리가 비었습니다.", nameof(ladder));
        }

        public void Start(double time)
        {
            if (State != CalState.Idle) throw new InvalidOperationException("보정 흐름은 한 번만 시작합니다.");
            now = startedAt = time;
            if (!sensor.IsRunning)
            {
                sensor.Start();
                startedSensor = true;
            }
            sensor.BeginCalibration();
            StageIndex = 0;
            Fails = 0;
            Wait(CalCue.Prepare);
        }

        public void Tick(double time)
        {
            now = time;
            switch (State)
            {
                case CalState.WaitingSteps:
                    if (sensor.Phone == SensorFlow.Ok && sensor.Current.Gait != Gait.None)
                    {
                        BeginStage();
                        Cue?.Invoke(CalCue.Resume, StageKmh);
                    }
                    break;

                case CalState.Measuring:
                    if (sensor.Current.Gait == Gait.None)
                    {
                        if (noStepsSince < 0) noStepsSince = now;
                        else if (now - noStepsSince >= StoppedSec)
                        {
                            sensor.CancelCalibrationStage();
                            Wait(CalCue.Stopped);
                            return;
                        }
                    }
                    else noStepsSince = -1;

                    double elapsed = now - stageStart;
                    if (!nextSoonSent && StageIndex < ladder.Count - 1 && elapsed >= stageSec - NextSoonSec)
                    {
                        nextSoonSent = true;
                        Cue?.Invoke(CalCue.NextSoon, ladder[StageIndex + 1]);
                    }
                    if (elapsed >= stageSec) FinishStage();
                    break;
            }
        }

        // 도움 화면에서 "다시": 걸음을 기다렸다가 그 단계부터
        public void RetryAfterHelp()
        {
            if (State != CalState.Help) return;
            Fails = 0;
            Wait(CalCue.Prepare);
        }

        // 앱이 백그라운드로 가는 등 잴 수 없게 됨: 그 단계는 버리고 걸음을 기다린다(실패로 안 셈)
        public void Interrupt()
        {
            if (State != CalState.Measuring) return;
            sensor.CancelCalibrationStage();
            Wait(CalCue.Stopped);
        }

        // 그만두기: 모은 점은 버리고 지금 보정은 그대로
        public void Abort()
        {
            if (State == CalState.Saving || State == CalState.Done || State == CalState.Aborted) return;
            if (State != CalState.Idle) sensor.AbortCalibration();
            StopSensorIfOurs();
            Set(CalState.Aborted);
        }

        void FinishStage()
        {
            var r = sensor.EndCalibrationStage();
            LastResult = r;
            if (r == CalStageResult.Ok)
            {
                Fails = 0;
                if (StageIndex == ladder.Count - 1)
                {
                    SaveTask = Save();
                    return;
                }
                StageIndex++;
                BeginStage();
                Cue?.Invoke(CalCue.Go, StageKmh);
                return;
            }
            Fails++;
            if (Fails >= HelpAfterFails)
            {
                Set(CalState.Help);
                Cue?.Invoke(CalCue.Help, StageKmh);
                return;
            }
            BeginStage();
            Cue?.Invoke(CalCue.Retry, StageKmh);
        }

        void BeginStage()
        {
            sensor.BeginCalibrationStage(StageKmh);
            stageStart = now;
            noStepsSince = -1;
            nextSoonSent = false;
            Set(CalState.Measuring);
        }

        void Wait(CalCue cue)
        {
            Set(CalState.WaitingSteps);
            Cue?.Invoke(cue, StageKmh);
        }

        async Task Save()
        {
            Set(CalState.Saving);
            string model = sensor.CommitCalibration(); // 서버 저장과 상관없이 이 폰에서는 바로 씀
            StopSensorIfOurs();
            try
            {
                await save(model);
                SavedToServer = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[보정] 서버 저장 실패, 다음에 다시 올림: {e.Message}");
                SavedToServer = false;
            }
            Set(CalState.Done);
            Cue?.Invoke(CalCue.Done, StageKmh);
        }

        void StopSensorIfOurs()
        {
            if (!startedSensor) return;
            startedSensor = false;
            sensor.Stop();
        }

        void Set(CalState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke();
        }
    }
}
