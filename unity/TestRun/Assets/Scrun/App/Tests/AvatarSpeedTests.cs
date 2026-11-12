using System.Collections.Generic;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 계획 0034: 걸음으로 바로 멈추고·출발하고, 위치 속도는 부드럽게. 시각은 센서 시각, 프레임 0.02초.
    public class AvatarSpeedTests
    {
        const double Dt = 0.02;

        AvatarSpeed a;
        readonly List<AvatarDelay> delays = new List<AvatarDelay>();
        double t;
        double stepAcc;
        int steps;

        [SetUp]
        public void SetUp()
        {
            a = new AvatarSpeed();
            a.DelayMeasured += delays.Add;
            delays.Clear();
            t = 0;
            stepAcc = 0;
            steps = 0;
        }

        // spm으로 걸으며(0이면 서 있음) 추정은 estimate(null이면 추정 없음)로 sec초 동안
        void Run(double sec, float spm, float? estimate, bool phoneOk = true)
        {
            for (double end = t + sec; t < end - 1e-9;)
            {
                t += Dt;
                stepAcc += spm / 60.0 * Dt;
                steps = (int)stepAcc;
                var r = new SpeedReading
                {
                    Time = t,
                    Steps = steps,
                    HasSpeed = estimate.HasValue,
                    SpeedKmh = estimate ?? 0f,
                    Gait = estimate.HasValue ? Gait.Running : Gait.None,
                };
                a.Update(r, phoneOk);
            }
        }

        [Test]
        public void 걷는_동안은_추정을_따라간다()
        {
            Run(5, 170, 10f);
            Assert.AreEqual(10f, a.SpeedKmh, 0.01f);
            Run(3, 175, 11f);
            Assert.AreEqual(11f, a.SpeedKmh, 0.01f);
        }

        [Test]
        public void 걸음이_끊기면_추정이_남아_있어도_2초_안에_0()
        {
            Run(5, 170, 10f);
            Run(1.0, 0, 10f); // 내려왔는데 추정은 아직 10(걸음 상태가 바뀌기 전)
            Assert.Greater(a.SpeedKmh, 9f, "1.2초 전엔 그대로");
            Run(1.0, 0, 10f);
            Assert.AreEqual(0f, a.SpeedKmh, "1.2초 + 감속 0.7초 안에 0");
            Assert.IsFalse(a.Stepping);
        }

        [Test]
        public void 멈출_때_순간_0이_아니라_줄어든다()
        {
            Run(5, 170, 10f);
            var seen = new List<float>();
            for (int i = 0; i < 100; i++)
            {
                Run(Dt, 0, null);
                seen.Add(a.SpeedKmh);
            }
            int between = seen.FindAll(v => v > 0.5f && v < 9.5f).Count;
            Assert.Greater(between, 10, "중간 속도를 여러 프레임 거침");
        }

        [Test]
        public void 서_있다가_걸으면_추정_전엔_3으로_출발하고_추정이_오면_올라간다()
        {
            Run(3, 0, null);
            Assert.AreEqual(0f, a.SpeedKmh);
            Run(2.0, 110, null); // 걸음 사이 0.55초 → 1.1초에 출발, 0.75초 만에 3 km/h. 추정은 아직
            Assert.IsTrue(a.Stepping);
            Assert.AreEqual(AvatarSpeed.RestartKmh, a.SpeedKmh, 0.01f);
            Run(0.5, 110, 5f);
            Assert.Greater(a.SpeedKmh, AvatarSpeed.RestartKmh);
            Run(1.0, 110, 5f);
            Assert.AreEqual(5f, a.SpeedKmh, 0.01f);
        }

        [Test]
        public void 한_번_툭_친_걸음으로는_출발하지_않는다()
        {
            Run(2, 0, null);
            stepAcc += 1; // 걸음 하나
            Run(2, 0, null);
            Assert.IsFalse(a.Stepping);
            Assert.AreEqual(0f, a.SpeedKmh);
        }

        [Test]
        public void 달리는_중_추정이_잠깐_비어도_속도를_지킨다()
        {
            Run(5, 170, 10f);
            Run(1.5, 170, null);
            Assert.AreEqual(10f, a.SpeedKmh, 0.01f);
        }

        [Test]
        public void 추정이_뛰어도_한_프레임에_점프하지_않는다()
        {
            Run(5, 170, 8f);
            Run(Dt, 170, 14f);
            Assert.AreEqual(8f + AvatarSpeed.AccelKmhPerSec * (float)Dt, a.SpeedKmh, 0.01f);
        }

        [Test]
        public void 벨트_진동처럼_빠른_걸음은_걸음이_아니다()
        {
            Run(3, 0, null);
            Run(4, 222, null); // 가만히 둔 폰이 222 spm(11/11 맥 책상)
            Assert.IsFalse(a.Stepping);
            Assert.AreEqual(0f, a.SpeedKmh);
        }

        [Test]
        public void 폰_데이터가_끊기면_멈춘다()
        {
            Run(5, 170, 10f);
            Run(1.0, 170, 10f, phoneOk: false);
            Assert.AreEqual(0f, a.SpeedKmh);
        }

        [Test]
        public void 반영_지연을_기록한다()
        {
            Run(5, 170, 10f);
            Assert.AreEqual(1, delays.Count, "처음 출발도 기록");
            Assert.AreEqual(AvatarDelayKind.Start, delays[0].Kind);
            delays.Clear();
            Run(3, 0, 10f);
            Run(2, 110, null);
            Assert.AreEqual(2, delays.Count);
            Assert.AreEqual(AvatarDelayKind.Stop, delays[0].Kind);
            Assert.That(delays[0].Sec, Is.InRange(1.2, 2.2), "마지막 걸음 → 0: 1.2초 + 감속");
            Assert.AreEqual(AvatarDelayKind.Start, delays[1].Kind);
            Assert.That(delays[1].Sec, Is.InRange(0.3, 0.8), "첫 걸음 → 출발: 두 번째 걸음까지(110 spm이면 0.55초)");
        }

        [Test]
        public void 센서를_다시_켜면_걸음은_새로_세고_속도는_부드럽게_줄어든다()
        {
            Run(5, 170, 10f);
            t = 0;
            stepAcc = 0;
            Run(Dt, 0, null);
            Assert.Greater(a.SpeedKmh, 9f, "한 프레임에 0으로 떨어지지 않음");
            Run(1.0, 0, null);
            Assert.AreEqual(0f, a.SpeedKmh);
            Assert.IsFalse(a.Stepping);
        }

        // ---- 리뷰(11/12)에서 나온 경우 ----

        [Test]
        public void 띄엄띄엄_두_번_툭_친_걸음으로는_출발하지_않는다()
        {
            Run(2, 0, null);
            stepAcc += 1;
            Run(30, 0, null);
            stepAcc += 1;
            Run(0.5, 0, null);
            Assert.IsFalse(a.Stepping);
            Assert.AreEqual(0f, a.SpeedKmh);
        }

        [Test]
        public void 처음부터_달리는_중이어도_멈칫하지_않는다()
        {
            bool started = false, dropped = false;
            for (int i = 0; i < 250; i++) // 5초, 정수 걸음 수로 짧은 창의 빠르기가 부풀던 문제
            {
                Run(Dt, 180, 10f);
                if (a.Stepping) started = true;
                else if (started) dropped = true;
            }
            Assert.IsTrue(started);
            Assert.IsFalse(dropped, "한 번 출발하면 계속 걸음");
            Assert.AreEqual(10f, a.SpeedKmh, 0.01f);
        }

        [Test]
        public void 짧게_멈췄다_다시_걸으면_멈추기_전_추정을_바로_믿지_않는다()
        {
            Run(5, 170, 10f);
            Run(2.0, 0, 10f);   // 멈춤, 추정기는 아직 10을 들고 있음(holdS 안)
            Assert.AreEqual(0f, a.SpeedKmh);
            Run(1.5, 110, 10f); // 다시 걷는데 추정은 멈추기 전 값
            Assert.IsTrue(a.Stepping);
            Assert.LessOrEqual(a.SpeedKmh, AvatarSpeed.RestartKmh + 0.01f);
            Run(3.0, 110, 10f); // FreshAfterSec 뒤부터 추정을 따라감
            Assert.Greater(a.SpeedKmh, AvatarSpeed.RestartKmh + 0.5f);
        }

        [Test]
        public void 추정이_없음이_된_뒤_다시_걸으면_새_추정을_바로_따른다()
        {
            Run(5, 170, 10f);
            Run(4, 0, null);
            Run(2.5, 110, 6f);
            Assert.Greater(a.SpeedKmh, AvatarSpeed.RestartKmh + 0.5f);
        }

        [Test]
        public void 한_프레임에_걸음_두_개가_와도_출발한다()
        {
            Run(3, 0, null);
            stepAcc += 2;
            Run(Dt, 0, null);
            Run(1.5, 110, null);
            Assert.IsTrue(a.Stepping);
        }

        [Test]
        public void 폰_끊김이나_진동으로_꺼진_건_멈춤_지연으로_안_잰다()
        {
            Run(5, 170, 10f);
            delays.Clear();
            Run(1.0, 170, 10f, phoneOk: false);
            Assert.AreEqual(0f, a.SpeedKmh);
            Assert.IsFalse(delays.Exists(d => d.Kind == AvatarDelayKind.Stop), "폰 끊김");

            SetUp();
            Run(5, 170, 10f);
            delays.Clear();
            Run(4, 222, null); // 내려왔는데 벨트 진동이 "걸음"으로 잡힘
            Assert.IsFalse(a.Stepping);
            Assert.AreEqual(0f, a.SpeedKmh);
            Assert.IsFalse(delays.Exists(d => d.Kind == AvatarDelayKind.Stop), "진동");
        }
    }
}
