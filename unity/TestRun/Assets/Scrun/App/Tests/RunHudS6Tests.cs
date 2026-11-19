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

        // 계획 0059: 서버가 welcome·joined에 실은 level → 이름표·카드 "Lv.N". 봇·옛 서버·이상한 값은 칩 없음
        [Test]
        public void LevelComesWithRunnerAndShowsAsChip()
        {
            double now = 0;
            var room = new RunTogetherRoom(() => now);
            room.Apply("{\"type\":\"welcome\",\"you\":\"me\",\"courseLengthM\":1000,\"runners\":[" +
                       "{\"userId\":\"a\",\"nickname\":\"비락식혜\",\"d\":100,\"v\":9,\"running\":true,\"level\":3}," +
                       "{\"userId\":\"b\",\"nickname\":\"하늘보리\",\"d\":50,\"v\":9,\"running\":true}," +
                       "{\"userId\":\"c\",\"nickname\":\"이상한값\",\"d\":60,\"v\":9,\"running\":true,\"level\":\"3\"}," +
                       "{\"userId\":\"bot_1\",\"nickname\":\"페이스 6:40\",\"d\":90,\"v\":9,\"running\":true,\"bot\":true,\"level\":2}]}");
            Assert.AreEqual(4, room.Runners.Count, "level 모양이 달라도 메시지는 버리지 않음");
            Assert.AreEqual("Lv.3", RunnerTagsView.LevelText(room.Runners[0]));
            Assert.IsNull(RunnerTagsView.LevelText(room.Runners[1]), "옛 서버(레벨 없음)");
            Assert.IsNull(RunnerTagsView.LevelText(room.Runners[2]), "숫자가 아니면 모르는 것으로");
            Assert.IsNull(RunnerTagsView.LevelText(room.Runners[3]), "봇은 레벨 없음");

            // joined가 겹쳐 늦게 와도 레벨을 채움
            room.Apply("{\"type\":\"joined\",\"runner\":{\"userId\":\"b\",\"nickname\":\"하늘보리\",\"d\":51,\"v\":9,\"running\":true,\"level\":1}}");
            Assert.AreEqual("Lv.1", RunnerTagsView.LevelText(room.Runners[1]));

            // 다시 붙어 welcome으로 이미 아는 사람을 받아도 새 레벨(그 사이 맵 밖에서 인증하고 다시 입장, 리뷰)
            room.Apply("{\"type\":\"welcome\",\"you\":\"me\",\"courseLengthM\":1000,\"runners\":[" +
                       "{\"userId\":\"a\",\"nickname\":\"비락식혜\",\"d\":100,\"v\":9,\"running\":true,\"level\":4}," +
                       "{\"userId\":\"b\",\"nickname\":\"하늘보리\",\"d\":50,\"v\":9,\"running\":true,\"level\":3000000000}]}");
            Assert.AreEqual(2, room.Runners.Count, "int 범위 밖 레벨이 와도 메시지를 버리지 않음");
            Assert.AreEqual("Lv.4", RunnerTagsView.LevelText(room.Runners[0]));
            Assert.AreEqual("Lv.1", RunnerTagsView.LevelText(room.Runners[1]), "이상한 값은 무시하고 아는 값 유지");
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
