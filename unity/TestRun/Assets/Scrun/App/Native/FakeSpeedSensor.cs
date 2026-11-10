using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Scrun.App
{
    // 에디터에서 쓰는 가짜 속도 센서(빌드에서는 쓰지 않음, Native.cs).
    // 사람이 러닝머신 위에서 실제로 하는 것(TargetKmh: 0이면 서 있음, 걷기, 달리기)을 창이나 키보드로 정하면,
    // 진짜 센서처럼 걸음 수는 바로 늘고 Gait는 감지 지연만큼 늦게 바뀐다.
    // 폰·에어팟 데이터가 들어오는지, 보정값이 있는지는 메뉴 Scrun → Fake Native 창의 "속도 센서" 칸에서 정한다.
    // 에어팟은 FakeNative의 이어폰이 "에어팟(모션 있음)"이고 헤드폰 모션 권한이 허용일 때만 쓴다.
    public sealed class FakeSpeedSensor : SpeedSensorBase
    {
        public static readonly FakeSpeedSensor Instance = new FakeSpeedSensor(useKeyboard: true);

        // SpeedEstimator.swift와 같은 판단 기준(계획 0020)
        public const float LostAfterSec = 2f;

        readonly FakeNativeConfig ownConfig;
        FakeNativeConfig Cfg => ownConfig ?? FakeNative.Config;
        readonly IHeadphones headphones;
        readonly IPermissions permissions;
        readonly Func<double> clock;
        readonly bool useKeyboard;

        double startedAt, lastTick;
        double lastPhone, lastAir; // 마지막으로 데이터가 들어온 시각(센서를 켠 뒤 초), 없으면 음수
        double airSince;           // 에어팟을 쓸 수 있게 된 시각, 아니면 음수
        Gait detected, pending;
        double pendingSince;
        double stepAcc;
        double scenarioStart = -1; // 시나리오 버튼을 누른 시각(센서 시각), 아니면 음수
        int keyboardFrame = -1;

        // 사람이 지금 실제로 달리는 속도(0 = 벨트 옆 발판에 서 있음). 창·키보드·시나리오가 바꾼다.
        public float TargetKmh { get; private set; }

        public FakeSpeedSensor(FakeNativeConfig config = null, IHeadphones headphones = null,
            IPermissions permissions = null, Func<double> clock = null, bool useKeyboard = false)
        {
            ownConfig = config;
            this.headphones = headphones;
            this.permissions = permissions;
            // 프레임 시각: 에디터 일시정지 중엔 멈춰서 재개할 때 걸음 수가 한 번에 뛰지 않는다
            this.clock = clock ?? (() => Time.unscaledTimeAsDouble);
            this.useKeyboard = useKeyboard;
            LoadStartCalibration();
        }

        IHeadphones Headphones => headphones ?? FakeNative.Instance;
        IPermissions Permissions => permissions ?? FakeNative.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Instance.ResetForPlay();

        protected override void OnResetForPlay()
        {
            scenarioStart = -1;
            TargetKmh = 0f;
            keyboardFrame = -1;
            ForceNextStage = null;
            stageOpen = false;
            LoadStartCalibration();
        }

        // 설정의 "보정값 있음"이면 사다리를 다 뛴 가짜 보정으로 시작한다(이벤트 없음)
        public void LoadStartCalibration() => SetCalibrationSilently(Cfg.hasCalibration ? SampleModel() : null);

        // 걷기·달리기 경계와 케이던스는 화면을 만들기 위한 흉내다(사람마다 다름, B는 7 km/h에서 걸음).
        public static Gait GaitFor(float kmh) =>
            kmh <= 0f ? Gait.None : kmh < 7f ? Gait.Walking : Gait.Running;

        public static float CadenceFor(float kmh) => GaitFor(kmh) switch
        {
            Gait.Walking => 85f + 6f * kmh,   // 4 km/h ≈ 109 spm
            Gait.Running => 140f + 3.5f * kmh, // 10 km/h = 175 spm
            _ => 0f,
        };

        // 가짜 사람의 ln(충격). 속도가 오르면 늘고, 걷다 뛰면 크게 뛴다(값 자체는 흉내)
        public static double LnImpactFor(float kmh) => GaitFor(kmh) switch
        {
            Gait.Walking => 0.2 + 0.15 * kmh,
            Gait.Running => 0.5 + 0.2 * kmh,
            _ => 0.0,
        };

        // 안내 속도 v에서 사람이 실제로 actualKmh로 달렸을 때 잰 보정점
        public static CalibrationPoint PointFor(float v, float actualKmh, bool airPods) => new CalibrationPoint
        {
            V = v,
            LnImpact = LnImpactFor(actualKmh),
            Cadence = CadenceFor(actualKmh),
            AirAmp = airPods ? 0.3 + 0.05 * actualKmh : (double?)null,
            Running = GaitFor(actualKmh) == Gait.Running,
        };

        // 사다리를 안내대로 다 뛴 보정
        public static CalibrationModel SampleModel() =>
            new CalibrationModel(CalibrationLadder.SpeedsKmh.Select(v => PointFor(v, v, true)));

        // ---- 보정 단계 흉내 ----

        // 다음 단계 하나를 이 결과로 끝낸다(창에서 고름). Ok가 아니면 그 결과, null이면 사람 속도로 판정
        public CalStageResult? ForceNextStage { get; set; }

        // 이번 단계가 이미 몇 초 쟀는지(버리는 시간 뒤), 창에 보여 주기용
        public double StageMeasuredSec => stageOpen && stageTicks > 0 ? stageLast - stageFirst : 0;

        bool stageOpen;
        double stageStart, stageFirst, stageLast;
        int stageTicks, stageNone, stageAirOk;
        bool stageLost;
        readonly List<float> stageKmh = new List<float>();

        protected override void OnBeginStage(float speedKmh)
        {
            stageOpen = true;
            stageStart = Now();
            stageTicks = stageNone = stageAirOk = 0;
            stageLost = false;
            stageKmh.Clear();
        }

        protected override void OnCancelStage() => stageOpen = false;

        protected override StageMeasurement OnEndStage(float speedKmh)
        {
            stageOpen = false;
            if (ForceNextStage is CalStageResult forced)
            {
                ForceNextStage = null;
                if (forced != CalStageResult.Ok) return new StageMeasurement { Result = forced };
            }
            if (stageLost) return new StageMeasurement { Result = CalStageResult.SensorLost };
            if (stageTicks == 0 || stageLast - stageFirst < 3.0) return new StageMeasurement { Result = CalStageResult.TooShort };
            if (stageNone * 2 > stageTicks) return new StageMeasurement { Result = CalStageResult.NoSteps };
            // 재는 동안 속도가 1 km/h 넘게 바뀌면 걸음이 고르지 않다(진짜는 걸음 규칙성 0.3 미만)
            if (stageKmh.Max() - stageKmh.Min() > 1f) return new StageMeasurement { Result = CalStageResult.Irregular };
            var sorted = stageKmh.OrderBy(x => x).ToList();
            float actual = sorted[sorted.Count / 2];
            bool air = stageAirOk >= 0.7 * stageTicks; // SpeedEstimator airMinCover
            return new StageMeasurement { Result = CalStageResult.Ok, Point = PointFor(speedKmh, actual, air) };
        }

        protected override void OnCalibrationApplied(CalibrationModel m) { } // 가짜 속도는 사람 속도 그대로라 쓸 데가 없음

        // ---- 창·키보드에서 부르는 흉내 ----

        public void SetTargetKmh(float kmh)
        {
            scenarioStart = -1;
            TargetKmh = Mathf.Clamp(Mathf.Round(kmh * 10f) / 10f, 0f, 20f);
        }

        // 벨트 옆에 서 있다가 3초 뒤 4 km/h로 걷고, 그 10초 뒤 8 km/h로 달린다(D-08 대기 → D-09 카운트다운 → 달리기).
        public void PlayScenario()
        {
            if (!IsRunning) Start();
            TargetKmh = 0f;
            scenarioStart = Now();
        }

        public bool ScenarioPlaying => scenarioStart >= 0;

        // ---- SpeedSensorBase ----

        double Now() => clock() - startedAt;

        protected override void OnStart()
        {
            startedAt = clock();
            lastTick = 0;
            lastPhone = lastAir = airSince = -1;
            detected = pending = Gait.None;
            pendingSince = 0;
            stepAcc = 0;
            TargetKmh = Cfg.startKmh;
            scenarioStart = -1;
        }

        protected override void OnStop() => scenarioStart = -1;

        protected override void Sample(out SpeedReading reading, out SensorFlow phone, out SensorFlow airPods)
        {
            var cfg = Cfg;
            double t = Now();
            double dt = Math.Max(0, t - lastTick);
            lastTick = t;

            if (useKeyboard) ReadKeyboard();
            if (scenarioStart >= 0)
            {
                double s = t - scenarioStart;
                TargetKmh = s < 3 ? 0f : s < 13 ? 4f : 8f;
                if (s >= 13) scenarioStart = -1;
            }

            // 폰: 켜고 sensorStartSec 뒤부터 들어옴
            if (cfg.phoneData && t >= cfg.sensorStartSec) lastPhone = t;
            phone = Flow(t, lastPhone);

            // 에어팟: 모션 있는 이어폰 + 헤드폰 모션 허용일 때만 쓴다. 아니면 꺼짐(이어폰 없이 진행).
            // 도중에 끼면 그때부터 폰처럼 sensorStartSec 뒤에 들어온다.
            bool airUsable = Headphones.Status == HeadphoneStatus.Connected
                && Permissions.GetStatus(PermissionKind.HeadphoneMotion) == PermissionStatus.Granted;
            if (!airUsable)
            {
                airSince = lastAir = -1;
                airPods = SensorFlow.Off;
            }
            else
            {
                if (airSince < 0) airSince = t;
                if (cfg.airPodsData && t - airSince >= cfg.sensorStartSec) lastAir = t;
                airPods = Flow(t - airSince, lastAir < 0 ? -1 : lastAir - airSince);
            }

            // 걸음: 폰 데이터가 없으면 아무것도 모른다
            var actual = phone == SensorFlow.Ok ? GaitFor(TargetKmh) : Gait.None;
            if (phone != SensorFlow.Ok)
            {
                detected = pending = Gait.None;
            }
            else if (actual == detected)
            {
                pending = detected;
            }
            else
            {
                if (actual != pending) { pending = actual; pendingSince = t; }
                if (t - pendingSince >= cfg.detectDelaySec) detected = actual;
            }
            if (actual != Gait.None) stepAcc += CadenceFor(TargetKmh) / 60.0 * dt;

            // 보정 단계: 앞 StageSkipSec초 뒤부터 잰다
            if (stageOpen && t - stageStart >= StageSkipSec)
            {
                if (stageTicks == 0) stageFirst = t;
                stageLast = t;
                stageTicks++;
                if (phone != SensorFlow.Ok) stageLost = true;
                if (actual == Gait.None) stageNone++;
                if (airPods == SensorFlow.Ok) stageAirOk++;
                stageKmh.Add(TargetKmh);
            }

            bool has = detected != Gait.None;
            reading = new SpeedReading
            {
                Time = t,
                HasSpeed = has,
                SpeedKmh = has ? TargetKmh : 0f,
                Gait = detected,
                CadenceSpm = has ? CadenceFor(TargetKmh) : 0f,
                Steps = (int)stepAcc,
                UsingAirPods = has && airPods == SensorFlow.Ok,
            };
        }

        // 마지막 데이터가 LostAfterSec 안이면 Ok. 한 번도 안 왔으면 LostAfterSec까지 Waiting, 그 뒤 Lost
        static SensorFlow Flow(double t, double last)
        {
            if (last < 0) return t < LostAfterSec ? SensorFlow.Waiting : SensorFlow.Lost;
            return t - last <= LostAfterSec ? SensorFlow.Ok : SensorFlow.Lost;
        }

        // FakeSpeedSource와 같은 키: ↑/↓ = ±0.5 km/h, Space = 정지. 한 씬에 FakeSpeedSource와 같이 두지 않는다(키가 둘 다 먹음).
        // 같은 프레임에 Tick이 두 번 돌아도(펌프 + Start) 키는 한 번만 읽는다.
        void ReadKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null || keyboardFrame == Time.frameCount) return;
            keyboardFrame = Time.frameCount;
            if (kb.upArrowKey.wasPressedThisFrame) SetTargetKmh(TargetKmh + 0.5f);
            if (kb.downArrowKey.wasPressedThisFrame) SetTargetKmh(TargetKmh - 0.5f);
            if (kb.spaceKey.wasPressedThisFrame) SetTargetKmh(0f);
        }
    }
}
