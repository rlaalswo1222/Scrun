using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 계획 0039: 측정 끊김(D-15)·에어팟만 끊김·측정값 이상(I-03)·배터리·발열(I-12)을 언제 보이는지
    public class RunMeasureWatchTests
    {
        static MeasureInputs Ok(bool running = true) => new MeasureInputs
        {
            Running = running,
            Phone = SensorFlow.Ok,
            AirPods = SensorFlow.Off,
            Thermal = ThermalLevel.Nominal,
            Battery = 0.8f,
        };

        [Test]
        public void 폰이_끊기면_D15를_띄우고_다시_들어오면_저절로_닫는다()
        {
            var w = new RunMeasureWatch();
            Assert.IsFalse(w.Update(Ok()).ShowLost);
            var lost = Ok();
            lost.Phone = SensorFlow.Lost;
            Assert.IsTrue(w.Update(lost).ShowLost);
            Assert.IsFalse(w.Update(lost).ShowLost, "한 번만");

            var starting = Ok(); // 다시 측정으로 센서를 켜는 중
            starting.Phone = SensorFlow.Waiting;
            var c = w.Update(starting);
            Assert.IsFalse(c.HideLost, "켜지는 중엔 그대로(깜박이지 않게)");
            Assert.IsTrue(w.LostShown);
            Assert.IsTrue(w.Update(Ok()).HideLost);
            Assert.IsFalse(w.LostShown);
        }

        [Test]
        public void 패널이_떠_있거나_멈춘_동안엔_새로_띄우지_않지만_닫기는_한다()
        {
            var w = new RunMeasureWatch();
            var lostPaused = Ok(running: false);
            lostPaused.Phone = SensorFlow.Lost;
            Assert.IsFalse(w.Update(lostPaused).ShowLost);

            var lost = Ok();
            lost.Phone = SensorFlow.Lost;
            w.Update(lost);
            Assert.IsTrue(w.Update(Ok(running: false)).HideLost, "패널이 있어도 닫기는 함");
        }

        [Test]
        public void 일시정지로_내린_D15는_다시_달릴_때_폰이_아직_끊겼으면_또_뜬다()
        {
            var w = new RunMeasureWatch();
            var lost = Ok();
            lost.Phone = SensorFlow.Lost;
            w.Update(lost);
            w.DismissLost();
            Assert.IsTrue(w.Update(lost).ShowLost);
        }

        [Test]
        public void 에어팟만_끊기면_한_번_알리고_다시_붙으면_다음에_또()
        {
            var w = new RunMeasureWatch();
            var on = Ok();
            on.AirPods = SensorFlow.Ok;
            w.Update(on);
            var off = Ok();
            off.AirPods = SensorFlow.Lost;
            Assert.IsTrue(w.Update(off).AirPodsDropped);
            Assert.IsFalse(w.Update(off).AirPodsDropped, "한 번만");
            w.Update(on);
            Assert.IsTrue(w.Update(off).AirPodsDropped);
            Assert.IsFalse(new RunMeasureWatch().Update(off).AirPodsDropped, "처음부터 에어팟 없이 달리면 알리지 않음");
        }

        [Test]
        public void 폰까지_끊기면_에어팟_알림_대신_D15()
        {
            var w = new RunMeasureWatch();
            var on = Ok();
            on.AirPods = SensorFlow.Ok;
            w.Update(on);
            var both = Ok();
            both.Phone = SensorFlow.Lost;
            both.AirPods = SensorFlow.Lost;
            var c = w.Update(both);
            Assert.IsTrue(c.ShowLost);
            Assert.IsFalse(c.AirPodsDropped);
        }

        [Test]
        public void 측정값_이상은_달리는_중_폰이_들어올_때만_띠()
        {
            var w = new RunMeasureWatch();
            var u = Ok();
            u.Unsteady = true;
            w.Update(u);
            Assert.IsTrue(w.Unsteady);
            var paused = Ok(running: false);
            paused.Unsteady = true;
            w.Update(paused);
            Assert.IsFalse(w.Unsteady);
        }

        [TestCase(ThermalLevel.Serious, 0.8f, false, true)]
        [TestCase(ThermalLevel.Critical, 0.8f, false, true)]
        [TestCase(ThermalLevel.Fair, 0.8f, false, false)]
        [TestCase(ThermalLevel.Nominal, 0.15f, false, true)]
        [TestCase(ThermalLevel.Nominal, 0.16f, false, false)]
        [TestCase(ThermalLevel.Nominal, 0.10f, true, false)]
        [TestCase(ThermalLevel.Nominal, -1f, false, false)]
        public void 발열_serious_이상_또는_충전_안_하는_15퍼센트_이하(ThermalLevel t, float battery, bool charging, bool hot)
        {
            var i = Ok();
            i.Thermal = t;
            i.Battery = battery;
            i.Charging = charging;
            Assert.AreEqual(hot, RunMeasureWatch.TooHot(i));
        }

        [Test]
        public void 발열_안내는_이번_달리기에_한_번만_측정_끊김이_먼저()
        {
            var w = new RunMeasureWatch();
            var hot = Ok();
            hot.Thermal = ThermalLevel.Serious;
            hot.Phone = SensorFlow.Lost;
            var c = w.Update(hot);
            Assert.IsTrue(c.ShowLost);
            Assert.IsFalse(c.AskHeat, "D-15가 떠 있으면 미룸");
            hot.Phone = SensorFlow.Ok;
            var closed = w.Update(hot);
            Assert.IsTrue(closed.HideLost && closed.AskHeat, "D-15가 닫히는 때 이어서 I-12");
            Assert.IsFalse(w.Update(hot).AskHeat, "한 번만");
        }
            [Test]
        public void 다시_측정으로_에어팟이_켜지는_중이면_끊김으로_안_센다()
        {
            var w = new RunMeasureWatch();
            var on = Ok();
            on.AirPods = SensorFlow.Ok;
            w.Update(on);
            var starting = Ok();
            starting.AirPods = SensorFlow.Waiting;
            Assert.IsFalse(w.Update(starting).AirPodsDropped, "켜지는 중");
            w.SensorRestarted();
            var off = Ok();
            off.AirPods = SensorFlow.Off;
            Assert.IsFalse(w.Update(off).AirPodsDropped, "다시 측정 직후 꺼짐은 끊김이 아님");
        }
    }
}
