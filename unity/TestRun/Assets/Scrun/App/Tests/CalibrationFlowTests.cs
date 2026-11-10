using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 보정 순서(계획 0023): 사다리 7단계, 실패하면 그 단계 다시, 연달아 실패하면 도움, 멈추면 기다림, 그만두기, 저장.
    // 가짜 사람이 안내(Cue)를 듣고 ReactSec초 뒤 러닝머신 속도를 바꾼다. 시계는 손으로 0.1초씩.
    public class CalibrationFlowTests
    {
        const double ReactSec = 2.0;

        SynchronizationContext savedContext;
        double now;
        FakeNativeConfig cfg;
        FakeNative native;
        FakeSpeedSensor s;
        CalibrationFlow flow;
        List<string> saved;
        Func<string, Task> saveImpl;
        List<(CalCue cue, float kmh)> cues;

        // 사람: 안내를 들으면 ReactSec 뒤 그 속도로. obey가 false를 주면 그 안내는 무시
        Func<CalCue, float, bool> obey;
        double changeAt = -1;
        float changeTo;

        [SetUp]
        public void SetUp()
        {
            savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null); // await 뒤가 바로 이어지게
            now = 100;
            cfg = new FakeNativeConfig { requestDelaySec = 0f, hasCalibration = false };
            cfg.startStatus[(int)PermissionKind.HeadphoneMotion] = PermissionStatus.Granted;
            native = new FakeNative(cfg);
            s = new FakeSpeedSensor(cfg, native, native, () => now);
            saved = new List<string>();
            saveImpl = m => { saved.Add(m); return Task.CompletedTask; };
            flow = new CalibrationFlow(s, m => saveImpl(m));
            cues = new List<(CalCue, float)>();
            obey = (c, k) => true;
            flow.Cue += (c, k) =>
            {
                cues.Add((c, k));
                if ((c == CalCue.Prepare || c == CalCue.Go || c == CalCue.Retry) && obey(c, k))
                {
                    changeAt = now + ReactSec;
                    changeTo = k;
                }
            };
        }

        [TearDown]
        public void TearDown() => SynchronizationContext.SetSynchronizationContext(savedContext);

        void Run(double sec)
        {
            int n = (int)Math.Round(sec / 0.1);
            for (int i = 0; i < n; i++)
            {
                now += 0.1;
                if (changeAt >= 0 && now >= changeAt)
                {
                    s.SetTargetKmh(changeTo);
                    changeAt = -1;
                }
                s.Tick();
                flow.Tick(now);
            }
        }

        void RunUntil(CalState state, double maxSec = 600)
        {
            for (double t = 0; t < maxSec && flow.State != state; t += 0.1) Run(0.1);
            Assert.AreEqual(state, flow.State);
        }

        [Test]
        public void 안내대로_뛰면_7단계를_재고_저장하며_3분_안에_끝난다()
        {
            flow.Start(now);
            Assert.AreEqual(CalState.WaitingSteps, flow.State);
            Assert.IsTrue(s.IsRunning);
            RunUntil(CalState.Done);

            Assert.IsTrue(flow.SavedToServer);
            Assert.AreEqual(1, saved.Count);
            var pts = CalibrationModel.Parse(saved[0]).Points;
            CollectionAssert.AreEqual(CalibrationLadder.SpeedsKmh.Select(v => (double)v), pts.Select(p => p.V));
            Assert.IsTrue(s.HasCalibration);
            Assert.IsFalse(s.IsRunning, "흐름이 켠 센서는 끝나면 끈다");
            Assert.Less(flow.TotalElapsed, 180, "실패 없으면 3분 안");
            // 다음 속도 예고는 마지막 단계를 빼고 한 번씩
            CollectionAssert.AreEqual(CalibrationLadder.SpeedsKmh.Skip(1), cues.Where(c => c.cue == CalCue.NextSoon).Select(c => c.kmh));
            Assert.AreEqual(CalCue.Done, cues.Last().cue);
        }

        [Test]
        public void 속도를_안_올리면_그_단계를_다시_재고_건너뛰지_않는다()
        {
            bool ignoredOnce = false;
            obey = (c, k) =>
            {
                if (c == CalCue.Go && k == 8f && !ignoredOnce) { ignoredOnce = true; return false; } // 8로 올리라는 걸 한 번 놓침
                return true;
            };
            flow.Start(now);
            RunUntil(CalState.Done);

            Assert.IsTrue(cues.Any(c => c.cue == CalCue.Retry && c.kmh == 8f));
            Assert.AreEqual(7, CalibrationModel.Parse(saved[0]).Points.Count);
            Assert.IsFalse(cues.Any(c => c.cue == CalCue.Help));
        }

        [Test]
        public void 같은_단계가_연달아_실패하면_도움_화면이고_다시_누르면_그_단계부터()
        {
            flow.Start(now);
            RunUntil(CalState.Measuring);
            s.ForceNextStage = CalStageResult.Irregular;
            Run(CalibrationLadder.StageSec + 0.5);
            Assert.AreEqual(CalState.Measuring, flow.State); // 한 번은 바로 다시
            Assert.AreEqual(CalStageResult.Irregular, flow.LastResult);
            s.ForceNextStage = CalStageResult.Irregular;
            Run(CalibrationLadder.StageSec + 0.5);
            Assert.AreEqual(CalState.Help, flow.State);
            Assert.AreEqual(0, flow.StageIndex);

            Run(30); // 도움 화면에선 사용자가 누를 때까지 그대로
            Assert.AreEqual(CalState.Help, flow.State);
            flow.RetryAfterHelp();
            Assert.AreEqual(CalState.WaitingSteps, flow.State);
            RunUntil(CalState.Done);
            Assert.AreEqual(7, CalibrationModel.Parse(saved[0]).Points.Count);
        }

        [Test]
        public void 속도_안_올림이_연달아면_앞_단계부터_다시_잴_수_있고_그_점은_버린다()
        {
            bool ignore = true;
            obey = (c, k) => !(ignore && c != CalCue.Prepare && k == 4f); // 4 km/h로 올리라는 안내를 무시
            flow.Start(now);
            RunUntil(CalState.Help);
            Assert.AreEqual(1, flow.StageIndex);
            Assert.AreEqual(CalStageResult.NotFaster, flow.LastResult);
            Assert.IsTrue(flow.CanRetryPrevious);
            Assert.AreEqual(1, s.CalibrationPointCount);

            ignore = false;
            flow.RetryPreviousStage();
            Assert.AreEqual(0, flow.StageIndex);
            Assert.AreEqual(0, s.CalibrationPointCount); // 앞 단계 점을 버림
            Assert.AreEqual(CalState.WaitingSteps, flow.State);
            Assert.AreEqual((CalCue.Prepare, 2f), cues.Last());
            RunUntil(CalState.Done);
            var points = CalibrationModel.Parse(saved[0]).Points;
            Assert.AreEqual(7, points.Count);
            CollectionAssert.AreEqual(CalibrationLadder.SpeedsKmh.Select(v => (double)v), points.Select(p => p.V));
        }

        [Test]
        public void 다른_이유의_도움_화면에선_앞_단계로_못_간다()
        {
            flow.Start(now);
            RunUntil(CalState.Measuring);
            s.ForceNextStage = CalStageResult.Irregular;
            Run(CalibrationLadder.StageSec + 0.5);
            s.ForceNextStage = CalStageResult.Irregular;
            Run(CalibrationLadder.StageSec + 0.5);
            Assert.AreEqual(CalState.Help, flow.State);
            Assert.IsFalse(flow.CanRetryPrevious); // 첫 단계이고, 이유도 NotFaster가 아님
            flow.RetryPreviousStage();
            Assert.AreEqual(CalState.Help, flow.State);
        }

        [TestCase(8, 2.0, 160, true)]
        [TestCase(8, 2.0, 0, false)]      // 걸음 수 0
        [TestCase(8, double.NaN, 160, false)]
        [TestCase(0, 2.0, 160, false)]
        public void 보정점은_속도_걸음수가_양수이고_충격이_숫자여야_쓴다(double v, double limp, double cad, bool valid) =>
            Assert.AreEqual(valid, new CalibrationPoint { V = v, LnImpact = limp, Cadence = cad }.IsValid);

        [Test]
        public void 재는_중에_멈추면_실패로_안_세고_기다렸다가_그_단계부터_이어서()
        {
            flow.Start(now);
            RunUntil(CalState.Measuring);
            Run(CalibrationLadder.StageSec + 3); // 2 km/h 단계 성공, 4 km/h 단계 3초째(멈춤을 알아채기까지 감지 지연 + StoppedSec이 남은 시간 안에 들어옴)
            Assert.AreEqual(1, flow.StageIndex);
            changeAt = -1;
            s.SetTargetKmh(0f); // 벨트에서 내려옴
            RunUntil(CalState.WaitingSteps, CalibrationLadder.StageSec);
            Assert.AreEqual(CalCue.Stopped, cues.Last().cue);
            Assert.AreEqual(1, flow.StageIndex);
            Assert.AreEqual(0, flow.Fails);
            Assert.AreEqual(1, s.CalibrationPointCount, "모은 점은 그대로");

            Run(60); // 오래 쉬어도 그대로 기다림
            Assert.AreEqual(CalState.WaitingSteps, flow.State);
            s.SetTargetKmh(4f);
            RunUntil(CalState.Measuring, 10);
            Assert.AreEqual(CalCue.Resume, cues.Last().cue);
            RunUntil(CalState.Done);
        }

        [Test]
        public void 앱이_백그라운드로_가면_그_단계를_버리고_기다린다()
        {
            flow.Start(now);
            RunUntil(CalState.Measuring);
            Run(10);
            flow.Interrupt();
            Assert.AreEqual(CalState.WaitingSteps, flow.State);
            Assert.IsFalse(s.StageOpen);
            Assert.AreEqual(0, flow.Fails);
            RunUntil(CalState.Done);
        }

        [Test]
        public void 그만두면_저장하지_않고_지금_보정은_그대로며_센서를_끈다()
        {
            s.LoadCalibration(FakeSpeedSensor.SampleModel().ToJson());
            var before = s.ExportCalibration();
            flow.Start(now);
            RunUntil(CalState.Measuring);
            Run(50);
            flow.Abort();
            Assert.AreEqual(CalState.Aborted, flow.State);
            Assert.IsFalse(s.Calibrating);
            Assert.IsFalse(s.IsRunning);
            Assert.AreEqual(before, s.ExportCalibration());
            CollectionAssert.IsEmpty(saved);
            Run(30);
            Assert.AreEqual(CalState.Aborted, flow.State);
        }

        [Test]
        public void 서버_저장이_실패해도_이_폰에서는_보정이_적용된다()
        {
            saveImpl = m => Task.FromException(new Exception("네트워크 없음"));
            flow.Start(now);
            RunUntil(CalState.Done);
            Assert.IsFalse(flow.SavedToServer);
            Assert.IsTrue(s.HasCalibration);
        }

        [Test]
        public void 이미_켜져_있던_센서는_끝나도_끄지_않는다()
        {
            s.Start();
            flow.Start(now);
            RunUntil(CalState.Done);
            Assert.IsTrue(s.IsRunning);
        }

        [Test]
        public void 한_번만_시작한다()
        {
            flow.Start(now);
            Assert.Throws<InvalidOperationException>(() => flow.Start(now));
        }
    }
}
