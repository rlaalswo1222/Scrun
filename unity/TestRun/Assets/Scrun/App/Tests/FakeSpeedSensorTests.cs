using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 지환 센서 준비(D-07)·카운트다운(D-08·09) 화면이 기대는 속도 센서 규칙(계획 0020).
    // 시계를 손으로 움직이고 Tick을 직접 불러 프레임 없이 확인한다.
    public class FakeSpeedSensorTests
    {
        double now;
        FakeNative native;
        FakeNativeConfig cfg;
        FakeSpeedSensor s;
        List<Gait> gaits;
        int flowEvents;

        [SetUp]
        public void SetUp()
        {
            now = 100;
            cfg = new FakeNativeConfig { requestDelaySec = 0f };
            cfg.startStatus[(int)PermissionKind.HeadphoneMotion] = PermissionStatus.Granted;
            native = new FakeNative(cfg);
            s = new FakeSpeedSensor(cfg, native, native, () => now);
            gaits = new List<Gait>();
            flowEvents = 0;
            s.GaitChanged += g => gaits.Add(g);
            s.FlowChanged += () => flowEvents++;
        }

        // sec초를 0.1초 프레임으로 흘린다
        void Run(double sec)
        {
            int n = (int)System.Math.Round(sec / 0.1);
            for (int i = 0; i < n; i++)
            {
                now += 0.1;
                s.Tick();
            }
        }

        [Test]
        public void 켜기_전에는_꺼짐이고_속도가_없다()
        {
            Assert.IsFalse(s.IsRunning);
            Assert.AreEqual(SensorFlow.Off, s.Phone);
            Assert.AreEqual(SensorFlow.Off, s.AirPods);
            Assert.IsFalse(s.Current.HasSpeed);
        }

        [Test]
        public void 켜면_기다림을_거쳐_정상이_되고_이벤트는_바뀔_때만_온다()
        {
            s.Start();
            Assert.AreEqual(SensorFlow.Waiting, s.Phone);
            Assert.AreEqual(SensorFlow.Waiting, s.AirPods);
            Assert.AreEqual(1, flowEvents);

            Run(1);
            Assert.AreEqual(SensorFlow.Ok, s.Phone);
            Assert.AreEqual(SensorFlow.Ok, s.AirPods);
            Assert.AreEqual(2, flowEvents); // 둘이 같은 프레임에 바뀌면 한 번

            Run(5);
            Assert.AreEqual(2, flowEvents);
            CollectionAssert.IsEmpty(gaits); // 서 있으면 걸음 이벤트 없음
        }

        [Test]
        public void 폰_데이터가_안_오면_2초_뒤_끊김이고_걸음도_안_잡힌다()
        {
            cfg.phoneData = false;
            cfg.startKmh = 8f;
            s.Start();
            Run(1.9);
            Assert.AreEqual(SensorFlow.Waiting, s.Phone);
            Run(0.2);
            Assert.AreEqual(SensorFlow.Lost, s.Phone);
            Run(5);
            Assert.AreEqual(Gait.None, s.Current.Gait);
            Assert.AreEqual(0, s.Current.Steps);
        }

        [Test]
        public void 걷기_시작하면_걸음_수는_바로_늘고_Gait는_감지_지연_뒤에_한_번_바뀐다()
        {
            s.Start();
            Run(1);
            s.SetTargetKmh(4f);
            Run(1);
            Assert.Greater(s.Current.Steps, 0);
            Assert.AreEqual(Gait.None, s.Current.Gait);
            Assert.IsFalse(s.Current.HasSpeed);

            Run(2);
            Assert.AreEqual(Gait.Walking, s.Current.Gait);
            Assert.IsTrue(s.Current.HasSpeed);
            Assert.AreEqual(4f, s.Current.SpeedKmh, 1e-4);
            Assert.Greater(s.Current.CadenceSpm, 0f);
            CollectionAssert.AreEqual(new[] { Gait.Walking }, gaits);
        }

        [Test]
        public void 감지_지연보다_짧게_걸었다_멈추면_이벤트가_없다()
        {
            s.Start();
            Run(1);
            s.SetTargetKmh(4f);
            Run(1);
            s.SetTargetKmh(0f);
            Run(5);
            CollectionAssert.IsEmpty(gaits);
        }

        [Test]
        public void 달리다_내려서면_지연_뒤_걸음_없음으로_돌아간다()
        {
            cfg.startKmh = 9f;
            s.Start();
            Run(4);
            Assert.AreEqual(Gait.Running, s.Current.Gait);
            Assert.IsTrue(s.Current.UsingAirPods);

            s.SetTargetKmh(0f);
            Run(3);
            Assert.AreEqual(Gait.None, s.Current.Gait);
            Assert.IsFalse(s.Current.HasSpeed);
            CollectionAssert.AreEqual(new[] { Gait.Running, Gait.None }, gaits);
        }

        [Test]
        public void 에어팟을_빼면_꺼짐이고_다시_끼면_기다림을_거쳐_정상()
        {
            cfg.startKmh = 9f;
            s.Start();
            Run(4);
            Assert.AreEqual(SensorFlow.Ok, s.AirPods);

            native.SimulateHeadphones(HeadphoneStatus.NotConnected, null);
            Run(0.1);
            Assert.AreEqual(SensorFlow.Off, s.AirPods);
            Assert.IsFalse(s.Current.UsingAirPods);
            Assert.AreEqual(Gait.Running, s.Current.Gait); // 폰만으로 계속

            native.SimulateHeadphones(HeadphoneStatus.Connected, "AirPods Pro");
            Run(0.1);
            Assert.AreEqual(SensorFlow.Waiting, s.AirPods); // 낀 순간 '끊김'이 깜빡이지 않음
            Run(1);
            Assert.AreEqual(SensorFlow.Ok, s.AirPods);
        }

        [Test]
        public void 헤드폰_모션을_거부하면_에어팟은_꺼짐()
        {
            native.SimulateStatus(PermissionKind.HeadphoneMotion, PermissionStatus.Denied);
            s.Start();
            Run(3);
            Assert.AreEqual(SensorFlow.Off, s.AirPods);
            Assert.AreEqual(SensorFlow.Ok, s.Phone);
        }

        [Test]
        public void 끄면_이벤트_없이_꺼짐으로_돌아가고_다시_켜면_걸음_수가_0부터()
        {
            cfg.startKmh = 8f;
            s.Start();
            Run(4);
            Assert.Greater(s.Current.Steps, 0);
            int gaitEvents = gaits.Count, flows = flowEvents;

            s.Stop();
            Assert.AreEqual(SensorFlow.Off, s.Phone);
            Assert.AreEqual(Gait.None, s.Current.Gait);
            Assert.AreEqual(gaitEvents, gaits.Count); // 일부러 끈 것은 "걸음 없음"·"끊김"으로 알리지 않음
            Assert.AreEqual(flows, flowEvents);
            Run(1);
            Assert.AreEqual(SensorFlow.Off, s.Phone); // 꺼진 뒤엔 Tick이 아무것도 안 함

            cfg.startKmh = 0f;
            s.Start();
            Run(1);
            Assert.AreEqual(0, s.Current.Steps);
        }

        [Test]
        public void 시나리오는_서_있다가_걷고_달린다()
        {
            s.PlayScenario();
            Assert.IsTrue(s.IsRunning);
            Run(2.9);
            Assert.AreEqual(Gait.None, s.Current.Gait);
            Run(3);
            Assert.AreEqual(Gait.Walking, s.Current.Gait);
            Run(11);
            Assert.AreEqual(Gait.Running, s.Current.Gait);
            Assert.AreEqual(8f, s.Current.SpeedKmh, 1e-4);
            Assert.IsFalse(s.ScenarioPlaying);
        }

        [Test]
        public void 이미_켜져_있으면_Start는_아무것도_안_한다()
        {
            cfg.startKmh = 8f;
            s.Start();
            Run(4);
            int steps = s.Current.Steps;
            s.Start(); // 걸음 수·상태가 처음으로 돌아가지 않음
            Assert.AreEqual(Gait.Running, s.Current.Gait);
            Assert.GreaterOrEqual(s.Current.Steps, steps);
        }

        // ---- 보정(계획 0023) ----

        // 사람이 kmh로 달리는 동안 안내 속도 v 단계를 sec초 잰다
        CalStageResult Stage(float v, float kmh, double sec = CalibrationLadder.StageSec)
        {
            s.SetTargetKmh(kmh);
            s.BeginCalibrationStage(v);
            Run(sec);
            return s.EndCalibrationStage();
        }

        [Test]
        public void 보정값_있음_설정이면_사다리_보정으로_시작하고_불러오기_지우기는_이벤트를_낸다()
        {
            int changed = 0;
            s.CalibrationChanged += () => changed++;
            Assert.IsTrue(s.HasCalibration);
            var json = s.ExportCalibration();
            Assert.AreEqual(CalibrationLadder.SpeedsKmh.Length, CalibrationModel.Parse(json).Points.Count);

            s.ClearCalibration();
            Assert.IsFalse(s.HasCalibration);
            Assert.IsNull(s.ExportCalibration());
            Assert.IsTrue(s.LoadCalibration(json));
            Assert.IsTrue(s.HasCalibration);
            Assert.AreEqual(2, changed);

            Assert.IsFalse(s.LoadCalibration("{깨진"));
            Assert.IsFalse(s.LoadCalibration("{\"version\":2,\"points\":[{\"v\":8,\"limp\":1,\"cad\":170,\"running\":true}]}"));
            Assert.IsFalse(s.LoadCalibration("{\"version\":1,\"points\":[]}"));
            Assert.IsTrue(s.HasCalibration); // 틀린 값은 지금 보정을 안 건드림
            Assert.AreEqual(2, changed);

            var none = new FakeSpeedSensor(new FakeNativeConfig { hasCalibration = false }, native, native, () => now);
            Assert.IsFalse(none.HasCalibration);
        }

        [Test]
        public void 사다리를_안내대로_뛰면_7점이_모이고_확정된다()
        {
            cfg.hasCalibration = false;
            s = new FakeSpeedSensor(cfg, native, native, () => now);
            s.Start();
            Run(1);
            s.BeginCalibration();
            foreach (var v in CalibrationLadder.SpeedsKmh)
                Assert.AreEqual(CalStageResult.Ok, Stage(v, v), $"{v} km/h");
            Assert.AreEqual(7, s.CalibrationPointCount);

            var json = s.CommitCalibration();
            Assert.IsFalse(s.Calibrating);
            Assert.IsTrue(s.HasCalibration);
            Assert.AreEqual(json, s.ExportCalibration());
            var pts = CalibrationModel.Parse(json).Points;
            CollectionAssert.AreEqual(CalibrationLadder.SpeedsKmh.Select(v => (double)v), pts.Select(p => p.V));
            CollectionAssert.AreEqual(new[] { false, false, false, true, true, true, true }, pts.Select(p => p.Running));
            Assert.IsTrue(pts.All(p => p.AirAmp.HasValue)); // 에어팟 끼고 뜀
        }

        [Test]
        public void 속도를_안_올리면_속도_안_올림이고_점이_안_쌓이며_올리고_다시_하면_된다()
        {
            s.Start();
            Run(1);
            s.BeginCalibration();
            Assert.AreEqual(CalStageResult.Ok, Stage(4f, 4f));
            Assert.AreEqual(CalStageResult.NotFaster, Stage(6f, 4f));
            Assert.AreEqual(1, s.CalibrationPointCount);
            Assert.AreEqual(CalStageResult.Ok, Stage(6f, 6f));
            Assert.AreEqual(2, s.CalibrationPointCount);
        }

        [Test]
        public void 앞_5초_안에_속도를_맞추면_괜찮고_재는_중에_바꾸면_고르지_않음()
        {
            s.Start();
            Run(1);
            s.BeginCalibration();
            // 4로 걷다가 단계가 시작되고 3초 뒤 6으로 올림 → 버리는 5초 안이라 성공, 점은 6의 특징
            s.SetTargetKmh(4f);
            s.BeginCalibrationStage(6f);
            Run(3);
            s.SetTargetKmh(6f);
            Run(17);
            Assert.AreEqual(CalStageResult.Ok, s.EndCalibrationStage());
            Assert.AreEqual(FakeSpeedSensor.CadenceFor(6f), s.DraftPoints[0].Cadence, 1e-4);

            // 8로 재는 중 8초째에 10으로 바꿈
            s.SetTargetKmh(8f);
            s.BeginCalibrationStage(8f);
            Run(8);
            s.SetTargetKmh(10f);
            Run(12);
            Assert.AreEqual(CalStageResult.Irregular, s.EndCalibrationStage());
        }

        [Test]
        public void 서_있으면_걸음_없음_너무_빨리_끝내면_너무_짧음()
        {
            s.Start();
            Run(1);
            s.BeginCalibration();
            Assert.AreEqual(CalStageResult.NoSteps, Stage(2f, 0f));
            Assert.AreEqual(CalStageResult.TooShort, Stage(2f, 2f, sec: 7));
            Assert.AreEqual(0, s.CalibrationPointCount);
        }

        [Test]
        public void 단계_중_센서를_끄거나_폰_데이터가_끊기면_센서_끊김()
        {
            s.Start();
            Run(1);
            s.BeginCalibration();
            s.SetTargetKmh(4f);
            s.BeginCalibrationStage(4f);
            Run(10);
            s.Stop();
            Assert.IsFalse(s.StageOpen);
            Assert.AreEqual(CalStageResult.SensorLost, s.EndCalibrationStage());

            s.Start();
            Run(1);
            s.BeginCalibrationStage(4f);
            Run(8);
            cfg.phoneData = false;
            Run(12);
            Assert.AreEqual(CalStageResult.SensorLost, s.EndCalibrationStage());
            Assert.AreEqual(0, s.CalibrationPointCount);
        }

        [Test]
        public void 보정을_그만두면_지금_보정은_그대로다()
        {
            var before = s.ExportCalibration();
            int changed = 0;
            s.CalibrationChanged += () => changed++;
            s.Start();
            Run(1);
            s.BeginCalibration();
            Assert.AreEqual(CalStageResult.Ok, Stage(2f, 3f)); // 안내와 다른 속도로 뛰었어도 점은 쌓임(알 수 없음)
            s.SetTargetKmh(4f);
            s.BeginCalibrationStage(4f);
            Run(5);
            s.AbortCalibration();
            Assert.IsFalse(s.Calibrating);
            Assert.IsFalse(s.StageOpen);
            Assert.AreEqual(before, s.ExportCalibration());
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void 다음_단계_결과_강제는_한_번만_적용된다()
        {
            s.Start();
            Run(1);
            s.BeginCalibration();
            s.ForceNextStage = CalStageResult.Irregular;
            Assert.AreEqual(CalStageResult.Irregular, Stage(2f, 2f));
            Assert.IsNull(s.ForceNextStage);
            Assert.AreEqual(CalStageResult.Ok, Stage(2f, 2f));
        }

        [Test]
        public void 순서를_어기면_예외()
        {
            Assert.Throws<System.InvalidOperationException>(() => s.BeginCalibrationStage(2f)); // BeginCalibration 전
            s.BeginCalibration();
            Assert.Throws<System.InvalidOperationException>(() => s.BeginCalibrationStage(2f)); // 센서 꺼짐
            Assert.Throws<System.InvalidOperationException>(() => s.EndCalibrationStage());     // 시작한 단계 없음
            s.Start();
            s.BeginCalibrationStage(2f);
            Assert.Throws<System.InvalidOperationException>(() => s.BeginCalibrationStage(4f)); // 이미 재는 중
            s.CancelCalibrationStage();
            Assert.Throws<System.InvalidOperationException>(() => s.CommitCalibration());      // 점 없음
        }
    }
}
