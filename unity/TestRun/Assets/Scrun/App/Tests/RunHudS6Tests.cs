using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 계획 0041(S6): 구간 알림 RunMoments, 끊김 안내 D-16
    public class RunHudS6Tests
    {
        static RunMoments Distance(int units) => new RunMoments(new RunGoal(GoalKind.Distance, units));
        static RunMoments Time(int minutes) => new RunMoments(new RunGoal(GoalKind.Time, minutes));

        [Test]
        public void HalfThenLast500mOncePerRun()
        {
            var m = Distance(30); // 3.0 km
            Assert.IsNull(m.Check(1400f, 500, null));
            Assert.AreEqual("절반 왔어요 · 1.5km", m.Check(1500f, 520, null));
            Assert.IsNull(m.Check(1600f, 560, null), "한 번만");
            Assert.IsNull(m.Check(2499f, 860, null));
            Assert.AreEqual("마지막 500m", m.Check(2500f, 870, null));
            Assert.IsNull(m.Check(2600f, 900, null), "한 번만");
        }

        [Test]
        public void LastMentionsTheNearestPersonWithin50m()
        {
            var m = Distance(30);
            m.Check(1500f, 0, null);
            Assert.AreEqual("마지막 500m · 하늘보리 님이 8m 뒤", m.Check(2500f, 0, ("하늘보리", -7.6f)));

            var far = Distance(30);
            far.Check(1500f, 0, null);
            Assert.AreEqual("마지막 500m", far.Check(2500f, 0, ("하늘보리", 51f)), "50m 넘으면 언급 안 함");

            var ahead = Distance(30);
            ahead.Check(1500f, 0, null);
            Assert.AreEqual("마지막 500m · 비락식혜 님이 3m 앞", ahead.Check(2500f, 0, ("비락식혜", 3f)));
        }

        [Test]
        public void TimeGoalUsesHalfTimeAndLastThreeMinutes()
        {
            var m = Time(30);
            Assert.IsNull(m.Check(0f, 899, null));
            Assert.AreEqual("절반 왔어요 · 15분", m.Check(0f, 900, null));
            Assert.IsNull(m.Check(0f, 1619, null));
            Assert.AreEqual("마지막 3분", m.Check(0f, 1620, null));

            var odd = Time(15);
            Assert.AreEqual("절반 왔어요 · 7분 30초", odd.Check(0f, 450, null));
        }

        [Test]
        public void ShortGoalSkipsLastAndJumpsGiveHalfFirst()
        {
            var oneKm = Distance(10); // 절반 500m = 마지막 500m → 마지막은 안 띄움
            Assert.AreEqual("절반 왔어요 · 0.5km", oneKm.Check(500f, 0, null));
            Assert.IsNull(oneKm.Check(800f, 0, null));

            var jump = Distance(30); // 한 번에 2.6 km로 뛰어도(끊겼다 이어짐) 절반 먼저, 다음에 마지막
            Assert.AreEqual("절반 왔어요 · 1.5km", jump.Check(2600f, 0, null));
            Assert.AreEqual("마지막 500m", jump.Check(2600f, 0, null));
        }

        [Test]
        public void NothingAfterTheGoal()
        {
            var m = Distance(30);
            Assert.IsNull(m.Check(3000f, 0, null), "목표 달성(D-19)이 맡음");
            Assert.IsNull(m.Check(3100f, 0, null));
        }

        [Test]
        public void MinimapProgressText()
        {
            Assert.AreEqual("2.1 / 3.0km", RunHudView.MapProgressText(new RunGoal(GoalKind.Distance, 30), 2199f, 0));
            Assert.AreEqual("0.0 / 3.0km", RunHudView.MapProgressText(new RunGoal(GoalKind.Distance, 30), 0f, 0));
            Assert.AreEqual("12 / 30분", RunHudView.MapProgressText(new RunGoal(GoalKind.Time, 30), 0f, 728));
        }

        [Test]
        public void RunDistanceGoesOutAndComesBack()
        {
            Assert.AreEqual("{\"type\":\"state\",\"d\":10,\"v\":9,\"running\":true,\"run\":2400.1}",
                RealtimeMessages.State(new MyRunState(10f, 9f, true, 2400.06f)));
            Assert.AreEqual("{\"type\":\"state\",\"d\":10,\"v\":0,\"running\":false}",
                RealtimeMessages.State(new MyRunState(10f, 0f, false)), "달리기 전엔 run 없음");

            double now = 0;
            var room = new RunTogetherRoom(() => now);
            room.Apply("{\"type\":\"welcome\",\"you\":\"me\",\"courseLengthM\":1000,\"runners\":[" +
                       "{\"userId\":\"a\",\"nickname\":\"비락식혜\",\"d\":100,\"v\":9,\"running\":true,\"run\":2400}," +
                       "{\"userId\":\"bot_1\",\"nickname\":\"페이스 6:40\",\"d\":90,\"v\":9,\"running\":true,\"bot\":true}]}");
            Assert.AreEqual(2400f, room.Runners[0].RunDistanceM);
            Assert.IsNull(room.Runners[1].RunDistanceM);

            var person = RunnerTagsView.CardText(room.Runners[0], 88f, 1000f, now);
            Assert.AreEqual(("비락식혜", (string)null, (string)null, "12m 앞", "2.40km"), person);
            var bot = RunnerTagsView.CardText(room.Runners[1], 98f, 1000f, now);
            Assert.AreEqual("BOT", bot.badge);
            Assert.AreEqual("8m 뒤", bot.gap);
            Assert.IsNull(bot.dist, "봇은 거리 없음");
        }

        [Test]
        public void ReconnectingShowsBannerAndDimsOthers()
        {
            Assert.AreEqual("연결이 불안정해요 · 재연결 중…", RunHudView.NetText(RunTogetherStatus.Reconnecting, RunTogetherStop.None));
            Assert.IsTrue(RunHudView.OthersStale(RunTogetherStatus.Reconnecting));
            Assert.IsTrue(RunHudView.OthersStale(RunTogetherStatus.Stopped));
            Assert.IsFalse(RunHudView.OthersStale(RunTogetherStatus.Connected));
            Assert.IsNull(RunHudView.NetText(RunTogetherStatus.Connected, RunTogetherStop.None));
        }
    }
}
