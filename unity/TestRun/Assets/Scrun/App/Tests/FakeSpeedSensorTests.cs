using System.Collections.Generic;
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

        [Test]
        public void 보정값은_설정을_그대로_읽는다()
        {
            Assert.IsTrue(s.HasCalibration);
            cfg.hasCalibration = false;
            Assert.IsFalse(s.HasCalibration);
        }
    }
}
