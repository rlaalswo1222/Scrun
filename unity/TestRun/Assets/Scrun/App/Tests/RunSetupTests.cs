using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 러닝 설정·거치 안내(계획 0026): 목표 값 계산, 직접 입력, 폰에 남기기, 안내 순서, 주간 목표 문구
    public class RunSetupTests
    {
        static RunGoal Km(int tenths) => new RunGoal(GoalKind.Distance, tenths);
        static RunGoal Min(int minutes) => new RunGoal(GoalKind.Time, minutes);

        // ---------- 목표 값 ----------
        [Test]
        public void DefaultsMatchFigma()
        {
            Assert.AreEqual("3.0", RunGoal.Default(GoalKind.Distance).ValueText);
            Assert.AreEqual("km", RunGoal.Default(GoalKind.Distance).UnitText);
            Assert.AreEqual("30", RunGoal.Default(GoalKind.Time).ValueText);
            Assert.AreEqual("분", RunGoal.Default(GoalKind.Time).UnitText);
        }

        [Test]
        public void PlusMinusMoveOneStepOnTheGrid()
        {
            Assert.AreEqual(Km(35), Km(30).Plus());
            Assert.AreEqual(Km(25), Km(30).Minus());
            Assert.AreEqual(Km(35), Km(33).Plus(), "직접 입력한 3.3에서 +는 다음 칸 3.5");
            Assert.AreEqual(Km(30), Km(33).Minus(), "−는 아래 칸 3.0");
            Assert.AreEqual(Min(35), Min(30).Plus());
            Assert.AreEqual(Min(30), Min(32).Minus());
        }

        [Test]
        public void StopsAtTheEnds()
        {
            Assert.IsFalse(Km(5).CanMinus);
            Assert.AreEqual(Km(5), Km(5).Minus());
            Assert.IsFalse(Km(422).CanPlus);
            Assert.AreEqual(Km(422), Km(420).Plus(), "42.0에서 +는 42.5가 아니라 끝 42.2");
            Assert.AreEqual(Min(180), Min(180).Plus());
            Assert.AreEqual(Min(5), Min(5).Minus());
            Assert.AreEqual(Km(5), Km(1), "범위 밖은 끝으로");
        }

        [Test]
        public void QuickChipsMatchPlan()
        {
            CollectionAssert.AreEqual(new[] { 20, 30, 50 }, RunGoal.QuickUnits(GoalKind.Distance));
            CollectionAssert.AreEqual(new[] { 20, 30, 45 }, RunGoal.QuickUnits(GoalKind.Time));
            Assert.AreEqual("2km", RunGoal.QuickText(GoalKind.Distance, 20));
            Assert.AreEqual("0.5km", RunGoal.QuickText(GoalKind.Distance, 5));
            Assert.AreEqual("45분", RunGoal.QuickText(GoalKind.Time, 45));
        }

        [Test]
        public void RequestUsesMetersAndSeconds()
        {
            var d = Km(35).ToRequest();
            Assert.AreEqual("DISTANCE", d.Type);
            Assert.AreEqual(3500, d.Value);
            var t = Min(45).ToRequest();
            Assert.AreEqual("TIME", t.Type);
            Assert.AreEqual(2700, t.Value);
        }

        // ---------- 직접 입력 ----------
        [TestCase("3", 30)]
        [TestCase("3.5", 35)]
        [TestCase("3,5", 35)]
        [TestCase(" 10 ", 100)]
        [TestCase(".5", 5)]
        [TestCase("42.2", 422)]
        public void ParsesDistance(string text, int tenths)
        {
            Assert.IsTrue(RunGoal.TryParse(GoalKind.Distance, text, out var g, out var problem), problem);
            Assert.AreEqual(Km(tenths), g);
        }

        [TestCase("3.25", "0.1 km 단위까지 입력해 주세요")]
        [TestCase("0.4", "0.5km에서 42.2km 사이로 입력해 주세요")]
        [TestCase("42.3", "0.5km에서 42.2km 사이로 입력해 주세요")]
        [TestCase("1500", "0.5km에서 42.2km 사이로 입력해 주세요")]
        [TestCase("99999999", "0.5km에서 42.2km 사이로 입력해 주세요")]
        [TestCase("abc", "숫자로 입력해 주세요")]
        [TestCase("-3", "숫자로 입력해 주세요")]
        [TestCase("3.5.1", "숫자로 입력해 주세요")]
        public void RejectsBadDistance(string text, string expected)
        {
            Assert.IsFalse(RunGoal.TryParse(GoalKind.Distance, text, out _, out var problem));
            Assert.AreEqual(expected, problem);
        }

        [TestCase("30", 30)]
        [TestCase("180", 180)]
        [TestCase("7", 7)]
        public void ParsesTime(string text, int minutes)
        {
            Assert.IsTrue(RunGoal.TryParse(GoalKind.Time, text, out var g, out var problem), problem);
            Assert.AreEqual(Min(minutes), g);
        }

        [TestCase("4", "5분에서 180분 사이로 입력해 주세요")]
        [TestCase("181", "5분에서 180분 사이로 입력해 주세요")]
        [TestCase("100001", "5분에서 180분 사이로 입력해 주세요")]
        [TestCase("99999999999", "5분에서 180분 사이로 입력해 주세요")]
        [TestCase("30.5", "1분 단위로 입력해 주세요")]
        [TestCase("삼십", "숫자로 입력해 주세요")]
        public void RejectsBadTime(string text, string expected)
        {
            Assert.IsFalse(RunGoal.TryParse(GoalKind.Time, text, out _, out var problem));
            Assert.AreEqual(expected, problem);
        }

        [Test]
        public void EmptyInputJustBlocksConfirm()
        {
            Assert.IsFalse(RunGoal.TryParse(GoalKind.Distance, "  ", out _, out var problem));
            Assert.IsNull(problem, "빈칸은 오류 문구 없이 확인만 막음");
        }

        // ---------- 폰에 남기기 ----------
        [Test]
        public void RemembersLastGoalPerKind()
        {
            var prefs = new RunSetupPrefs(new MemoryPrefs());
            Assert.AreEqual(RunGoal.Default(GoalKind.Distance), prefs.LastGoal(), "처음엔 거리 3 km");

            prefs.Remember(Min(45));
            Assert.AreEqual(Min(45), prefs.LastGoal());
            Assert.AreEqual(RunGoal.Default(GoalKind.Distance), prefs.LastGoal(GoalKind.Distance));

            prefs.Remember(Km(50));
            Assert.AreEqual(Km(50), prefs.LastGoal());
            Assert.AreEqual(Min(45), prefs.LastGoal(GoalKind.Time), "시간 값은 그대로");
        }

        [Test]
        public void SkipManualIsStored()
        {
            var store = new MemoryPrefs();
            Assert.IsFalse(new RunSetupPrefs(store).SkipManual);
            new RunSetupPrefs(store).SkipManual = true;
            Assert.IsTrue(new RunSetupPrefs(store).SkipManual);
        }

        // ---------- 안내 순서 ----------
        [Test]
        public void StepsGoForwardAndBack()
        {
            var s = new RunSetupSteps(skipManual: false);
            Assert.AreEqual(SetupStep.Goal, s.Current);
            Assert.AreEqual(SetupStep.Mount, s.Next());
            Assert.AreEqual(SetupStep.Manual, s.Next());
            Assert.AreEqual(SetupStep.Mount, s.Back());
            Assert.AreEqual(SetupStep.Goal, s.Back());
            Assert.AreEqual(SetupStep.Exit, s.Back(), "D-01에서 뒤로 → 맵 나가기");
        }

        [Test]
        public void SkipManualGoesFromMountToDone()
        {
            var s = new RunSetupSteps(skipManual: true);
            s.Next();
            Assert.AreEqual(SetupStep.Done, s.Next(), "거치 안내는 매번, 매뉴얼은 건너뜀");
        }

        [Test]
        public void ManualLeadsToDone()
        {
            var s = new RunSetupSteps(skipManual: false);
            s.Next();
            s.Next();
            Assert.AreEqual(SetupStep.Done, s.Next());
            Assert.AreEqual(SetupStep.Done, s.Next(), "끝난 뒤에는 그대로");
        }

        // ---------- 주간 목표 문구 ----------
        [Test]
        public void WeeklyLeftLine()
        {
            Assert.AreEqual("이번 주 목표까지 1회 남았어요",
                            RunSetupText.WeeklyLeft(new WeeklyGoal { TargetCount = 3, DoneCount = 2 }));
            Assert.IsNull(RunSetupText.WeeklyLeft(new WeeklyGoal { TargetCount = null, DoneCount = 0 }), "목표 없음");
            Assert.IsNull(RunSetupText.WeeklyLeft(new WeeklyGoal { TargetCount = 3, DoneCount = 3 }), "다 채움");
            Assert.IsNull(RunSetupText.WeeklyLeft(new WeeklyGoal { TargetCount = 3, DoneCount = 5 }), "넘침");
            Assert.IsNull(RunSetupText.WeeklyLeft(null));
        }

        [Test]
        public void CourseChip() => Assert.AreEqual("노을 호수 한 바퀴 · 지금 12명", RunSetupText.CourseChip("노을 호수 한 바퀴", 12));

        // ---------- 서버 응답 ----------

        [Test]
        public void WeeklyGoalReadsServerShape()
        {
            var saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                var http = new FakeHttp
                {
                    // 서버 stub_data.WEEKLY_GOAL 모양
                    Handler = _ => FakeHttp.Ok("{\"targetCount\":3,\"cycleStart\":\"2026-11-03\",\"cycleEnd\":\"2026-11-09\"," +
                                               "\"doneCount\":2,\"daysLeft\":3,\"nextTargetCount\":null,\"history\":[]}")
                };
                var session = new Session(new MemorySecureStore());
                session.SetTokens("access", "refresh");
                var users = new UsersApi(new ApiClient(http, session, () => "http://test"));
                var t = users.GetWeeklyGoalAsync();
                Assert.IsTrue(t.IsCompleted);
                var g = t.GetAwaiter().GetResult();
                Assert.AreEqual(3, g.TargetCount);
                Assert.AreEqual(2, g.DoneCount);
                Assert.IsNull(g.NextTargetCount);
                StringAssert.EndsWith("/v1/users/me/weekly-goal", http.Sent[0].Url);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(saved);
            }
        }
    }
}
