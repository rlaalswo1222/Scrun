using System.Collections.Generic;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 같이 뛰기(계획 0029): 고리 코스 계산, 다른 러너 내다보기·맞춤, 방 메시지(docs/api/realtime.md), 다시 붙기 규칙, 달리기 시계·HUD 글자
    public class RealtimeTests
    {
        const float Lap = 1000f;

        // ---------- 고리 코스 ----------
        [Test]
        public void WrapAndShortestDelta()
        {
            Assert.AreEqual(10f, LoopMath.Wrap(1010f, Lap), 1e-3);
            Assert.AreEqual(990f, LoopMath.Wrap(-10f, Lap), 1e-3);
            Assert.AreEqual(20f, LoopMath.Delta(990f, 10f, Lap), 1e-3, "끝을 넘어 앞으로 20m");
            Assert.AreEqual(-20f, LoopMath.Delta(10f, 990f, Lap), 1e-3, "시작점을 넘어 뒤로 20m");
            Assert.AreEqual(500f, LoopMath.Delta(0f, 500f, Lap), 1e-3, "딱 반 바퀴는 앞");
        }

        [Test]
        public void NearestAheadAndBehindOnALoop()
        {
            var others = new List<(string, float)> { ("가", 120f), ("나", 990f), ("다", 400f), ("라", 560f) };
            var (a, b) = LoopMath.Nearest(100f, others, Lap);
            Assert.AreEqual("가", a.Value.Name);
            Assert.AreEqual(20f, a.Value.GapM, 1e-3);
            Assert.AreEqual("나", b.Value.Name, "시작점 너머 뒤 110m");
            Assert.AreEqual(110f, b.Value.GapM, 1e-3);

            var (none1, none2) = LoopMath.Nearest(100f, new List<(string, float)>(), Lap);
            Assert.IsNull(none1);
            Assert.IsNull(none2);
        }

        [Test]
        public void GapText()
        {
            Assert.AreEqual("12m 앞", LoopMath.GapText(12.4f, ahead: true));
            Assert.AreEqual("8m 뒤", LoopMath.GapText(7.6f, ahead: false));
            Assert.AreEqual("1.2km 앞", LoopMath.GapText(1234f, ahead: true));
        }

        // ---------- 다른 러너 내다보기 ----------
        [Test]
        public void PredictsForwardWithSpeed()
        {
            var r = new RemoteRunner("u1", "민트", Lap);
            r.Sample(100f, 36f, true, now: 10); // 10 m/s
            Assert.AreEqual(100f, r.PositionAt(10), 1e-3);
            Assert.AreEqual(115f, r.PositionAt(11.5), 1e-3);
            var shortLoop = new RemoteRunner("u2", "코랄", 200f);
            shortLoop.Sample(190f, 36f, true, now: 0);
            Assert.AreEqual(5f, shortLoop.PositionAt(1.5), 1e-3, "끝을 넘으면 감김");
        }

        [Test]
        public void NewSampleBlendsInsteadOfJumping()
        {
            var r = new RemoteRunner("u1", "민트", Lap);
            r.Sample(100f, 36f, true, now: 0);
            // 1초 뒤 보이는 자리 110. 서버 값은 106(조금 느렸음) → 그 순간엔 그대로 110, 0.5초 뒤엔 새 예측(106 + 5)에 맞음
            r.Sample(106f, 36f, true, now: 1);
            Assert.AreEqual(110f, r.PositionAt(1), 1e-3);
            Assert.AreEqual(106f + 2.5f + 2f, r.PositionAt(1.25), 1e-3, "반쯤 맞춤: 새 예측 108.5 + 차이 4의 절반");
            Assert.AreEqual(111f, r.PositionAt(1.5), 1e-3);
        }

        [Test]
        public void BigDifferenceSnaps()
        {
            var r = new RemoteRunner("u1", "민트", Lap);
            r.Sample(100f, 0f, false, now: 0);
            r.Sample(300f, 0f, false, now: 1);
            Assert.AreEqual(300f, r.PositionAt(1), 1e-3);
        }

        [Test]
        public void RepeatedStaleSampleKeepsPredicting()
        {
            var r = new RemoteRunner("u1", "민트", Lap);
            r.Sample(100f, 36f, true, now: 0);
            r.Sample(100f, 36f, true, now: 2); // 끊긴 사람의 마지막 값이 다시 옴
            Assert.AreEqual(120f, r.PositionAt(2), 1e-3);
            Assert.AreEqual(100f + 300f, r.PositionAt(100), 1e-3, "30초까지만 내다봄");
        }

        // ---------- 방 메시지 ----------
        sealed class Clock
        {
            public double T = 10;
            public double Now() => T;
        }

        static string Welcome(params string[] runners) =>
            "{\"type\":\"welcome\",\"you\":\"me\",\"mapInstanceId\":\"map_crs_x\",\"courseLengthM\":1000,\"serverTime\":5000,\"runners\":[" +
            string.Join(",", runners) + "]}";

        static string Runner(string id, float d, float v = 0f) =>
            $"{{\"userId\":\"{id}\",\"nickname\":\"닉{id}\",\"d\":{d},\"v\":{v},\"running\":true}}";

        [Test]
        public void WelcomeJoinedStatesLeft()
        {
            var clock = new Clock();
            var room = new RunTogetherRoom(clock.Now);
            var joined = new List<string>();
            var left = new List<string>();
            room.Joined += r => joined.Add(r.UserId);
            room.Left += r => left.Add(r.UserId);

            Assert.IsTrue(room.Apply(Welcome(Runner("a", 10f))));
            Assert.AreEqual("me", room.MyUserId);
            Assert.AreEqual(1000f, room.CourseLengthM);
            Assert.AreEqual(1, room.Runners.Count);
            Assert.AreEqual("닉a", room.Runners[0].Nickname);
            CollectionAssert.IsEmpty(joined, "처음 welcome에 있던 사람은 알림 없음");

            Assert.IsTrue(room.Apply("{\"type\":\"joined\",\"runner\":" + Runner("b", 50f) + "}"));
            CollectionAssert.AreEqual(new[] { "b" }, joined);

            Assert.IsTrue(room.Apply("{\"type\":\"states\",\"serverTime\":1,\"runners\":[" + Runner("a", 20f, 36f) + "," +
                                     Runner("zz", 1f) + "]}"));
            clock.T += RemoteRunner.CorrectSec;
            Assert.AreEqual(20f + 5f, room.Runners[0].PositionAt(clock.T), 1e-3, "새 값으로 다 맞춘 뒤 10 m/s로 0.5초");
            Assert.AreEqual(2, room.Runners.Count, "모르는 사람의 states는 무시");

            Assert.IsTrue(room.Apply("{\"type\":\"left\",\"userId\":\"a\"}"));
            CollectionAssert.AreEqual(new[] { "a" }, left);
            Assert.AreEqual("b", room.Runners[0].UserId);
        }

        [Test]
        public void ReWelcomeAnnouncesOnlyTheDifference()
        {
            var room = new RunTogetherRoom(new Clock().Now);
            var joined = new List<string>();
            var left = new List<string>();
            room.Joined += r => joined.Add(r.UserId);
            room.Left += r => left.Add(r.UserId);
            room.Apply(Welcome(Runner("a", 10f), Runner("b", 20f)));
            room.Apply(Welcome(Runner("b", 25f), Runner("c", 30f))); // 다시 붙음: a 나감, c 들어옴
            CollectionAssert.AreEqual(new[] { "c" }, joined);
            CollectionAssert.AreEqual(new[] { "a" }, left);
            Assert.AreEqual(2, room.Runners.Count);
        }

        [Test]
        public void IgnoresMeAndBadMessages()
        {
            var clock = new Clock();
            var room = new RunTogetherRoom(clock.Now);
            room.Apply(Welcome(Runner("a", 10f)));
            Assert.IsFalse(room.Apply("{\"type\":\"joined\",\"runner\":" + Runner("me", 1f) + "}"), "나는 목록에 안 넣음");
            Assert.IsFalse(room.Apply("not json"));
            Assert.IsFalse(room.Apply("{\"type\":\"states\",\"runners\":[{\"userId\":\"a\",\"d\":\"x\",\"v\":1}]}"));
            Assert.IsFalse(room.Apply("{\"type\":\"emoji\"}"));
            Assert.AreEqual(1, room.Runners.Count);
            Assert.AreEqual(10f, room.Runners[0].PositionAt(clock.T), 1e-3, "틀린 값은 반영 안 함");
        }

        [Test]
        public void PongGivesRoundTripAndServerOffset()
        {
            var clock = new Clock { T = 10.2 };
            var room = new RunTogetherRoom(clock.Now);
            Assert.IsTrue(room.Apply("{\"type\":\"pong\",\"t\":10000,\"serverTime\":50100}"));
            Assert.AreEqual(200, room.RoundTripMs.Value, 1e-6);
            Assert.AreEqual(50100 + 100 - 10200, room.ServerOffsetMs.Value, 1e-6);
        }

        // ---------- 보내는 메시지·다시 붙기 ----------
        [Test]
        public void OutgoingMessages()
        {
            Assert.AreEqual("{\"type\":\"state\",\"d\":12.5,\"v\":9.25,\"running\":true}",
                            RealtimeMessages.State(new MyRunState(12.5f, 9.25f, true)));
            Assert.AreEqual("{\"type\":\"state\",\"d\":0,\"v\":0,\"running\":false}",
                            RealtimeMessages.State(new MyRunState(float.NaN, -3f, false)), "서버가 NaN을 받으면 끊으므로 0으로");
            Assert.AreEqual("{\"type\":\"ping\",\"t\":1234.5}", RealtimeMessages.Ping(1234.5));
        }

        [Test]
        public void ReconnectPolicy()
        {
            CollectionAssert.AreEqual(new[] { 1.0, 2, 4, 8, 8 },
                                      new[] { Scrun.App.ReconnectPolicy.DelaySec(0), Scrun.App.ReconnectPolicy.DelaySec(1),
                                              Scrun.App.ReconnectPolicy.DelaySec(2), Scrun.App.ReconnectPolicy.DelaySec(3),
                                              Scrun.App.ReconnectPolicy.DelaySec(10) });
            Assert.AreEqual(RunTogetherStop.None, Scrun.App.ReconnectPolicy.StopFor(null));
            Assert.AreEqual(RunTogetherStop.None, Scrun.App.ReconnectPolicy.StopFor(1001));
            Assert.AreEqual(RunTogetherStop.ReplacedElsewhere, Scrun.App.ReconnectPolicy.StopFor(4001));
            Assert.AreEqual(RunTogetherStop.NeedsReentry, Scrun.App.ReconnectPolicy.StopFor(4401));
            Assert.AreEqual(RunTogetherStop.NeedsReentry, Scrun.App.ReconnectPolicy.StopFor(4403));
            Assert.AreEqual(RunTogetherStop.ProtocolError, Scrun.App.ReconnectPolicy.StopFor(4400));
        }

        [Test]
        public void NoStateUntilIslandIsReady()
        {
            RunLink.Begin(null);
            try
            {
                Assert.IsNull(RunLink.MyState(), "섬이 열리기 전엔 안 보냄(0m에 나타나지 않게)");
                RunLink.ReportPlayer(1010f, 9f, 1000f);
                var s = RunLink.MyState().Value;
                Assert.AreEqual(10f, s.D, 1e-3, "한 바퀴 안 위치로");
                Assert.IsFalse(s.Running);
                RunLink.Moving = true;
                Assert.IsTrue(RunLink.MyState().Value.Running);
                RunLink.IslandGone();
                Assert.IsNull(RunLink.MyState());
            }
            finally
            {
                RunLink.End();
            }
        }

        // ---------- 달리기 시계·HUD 글자 ----------
        [Test]
        public void SessionCountsFromStart()
        {
            var clock = new Clock { T = 100 };
            var s = new RunSession(new RunGoal(GoalKind.Distance, 30), clock.Now);
            Assert.AreEqual(0f, s.DistanceM(500f));
            Assert.AreEqual(0, s.ElapsedSec);
            s.Start(500f);
            clock.T = 160;
            Assert.AreEqual(1500f - 500f, s.DistanceM(1500f));
            Assert.AreEqual(60, s.ElapsedSec, 1e-9);
            Assert.AreEqual(1000f / 3000f, s.Progress(1500f), 1e-5);
            Assert.AreEqual(1f, s.Progress(9000f), "목표를 넘어도 1");

            var t = new RunSession(new RunGoal(GoalKind.Time, 30), clock.Now);
            t.Start(0f);
            clock.T += 900;
            Assert.AreEqual(0.5f, t.Progress(0f), 1e-5);
        }

        [Test]
        public void HudTexts()
        {
            Assert.AreEqual("1.24", RunSession.DistanceText(1249f), "내림(아직 1.25 km가 아님)");
            Assert.AreEqual("0.00", RunSession.DistanceText(0f));
            Assert.AreEqual("12:05", RunSession.TimeText(725.9));
            Assert.AreEqual("1:02:05", RunSession.TimeText(3725));
            Assert.AreEqual("목표 3.0 km", RunHudView.GoalText(new RunGoal(GoalKind.Distance, 30)));
            Assert.AreEqual("목표 30분", RunHudView.GoalText(new RunGoal(GoalKind.Time, 30)));
            Assert.IsNull(RunHudView.NetText(RunTogetherStatus.Connected, RunTogetherStop.None));
            Assert.IsNotNull(RunHudView.NetText(RunTogetherStatus.Reconnecting, RunTogetherStop.None));
            Assert.IsNull(RunHudView.NetText(RunTogetherStatus.Stopped, RunTogetherStop.Closed), "앱이 끝낸 건 안 알림");
        }
    }
}
