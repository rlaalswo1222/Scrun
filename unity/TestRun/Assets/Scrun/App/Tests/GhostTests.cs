using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0050(고스트러너): 시계열 → 거리, 기록 끝 뒤, 배너 문구, 출발 자리, POST /runs의 ghostRunId, 러닝 설정 화면 칸
    public class GhostTests
    {
        static GhostTrack Steady(float kmh, int seconds, int officialM)
        {
            var speeds = new List<float>();
            for (int i = 0; i < seconds; i++) speeds.Add(kmh);
            return GhostTrack.From(officialM, seconds, 1.0, speeds);
        }

        [Test]
        public void SpeedsBecomeDistanceEndingAtTheOfficialDistance()
        {
            // 10 km/h × 360초 = 1000 m(센서), 공식(계기판) 1050 m → 끝이 1050이 되게 늘림
            var t = Steady(10f, 360, 1050);
            Assert.AreEqual(1050f, t.TotalM, 0.01f);
            Assert.AreEqual(360.0, t.DurationS, 1e-9);
            Assert.AreEqual(525f, t.DistanceAt(180), 0.01f, "가운데");
            Assert.AreEqual(1.4583f, t.DistanceAt(0.5), 0.001f, "칸 사이는 이어서");
            Assert.AreEqual(10.5f, t.SpeedKmhAt(10), 0.01f, "걸음 속도도 같은 비율");
        }

        [Test]
        public void GhostStopsWhereTheRecordEnded()
        {
            var t = Steady(9f, 60, 150);
            Assert.IsTrue(t.FinishedAt(60));
            Assert.AreEqual(150f, t.DistanceAt(999));
            Assert.AreEqual(0f, t.SpeedKmhAt(61), "끝나면 서 있음(정할 것 3 가)");
            Assert.AreEqual(0f, t.DistanceAt(-3), "출발 전");
        }

        [Test]
        public void GhostEndsAtTheOfficialDurationEvenIfSamplesAreMissing()
        {
            // 공식 31:20(1880초)인데 칸이 1700개(못 올린 묶음): 기록에 보인 시간에 끝나야 함(3분 일찍 멈추지 않게)
            var t = Steady(10f, 1700, 5130);
            var stretched = GhostTrack.From(5130, 1880, 1.0, new List<float>(new float[1700]).ConvertAll(_ => 10f));
            Assert.AreEqual(1880.0, stretched.DurationS, 1e-6);
            Assert.IsFalse(stretched.FinishedAt(1800));
            Assert.AreEqual(2565f, stretched.DistanceAt(940), 0.5f, "일정 속도면 시간 절반에 거리 절반");
            Assert.AreEqual(1700.0, t.DurationS, 1e-6, "칸 수와 공식 시간이 같으면 그대로");
        }

        [Test]
        public void GapRowUsesTheTrueGhostDistanceNotTheLap()
        {
            // 한 바퀴 6.7 km, 고스트가 4.0 km 앞 → 접으면 "2.7km 뒤"가 되지만 배너처럼 "4.0km 앞"
            var (a, b) = LoopMath.WithExact((null, null), GhostText.Name, 4000f);
            Assert.AreEqual(GhostText.Name, a.Value.Name);
            Assert.AreEqual(4000f, a.Value.GapM);
            Assert.IsNull(b);
            // 더 가까운 사람이 이미 앞에 있으면 그대로
            var near = (new LoopMath.Neighbor("친구", 30f), (LoopMath.Neighbor?)null);
            Assert.AreEqual("친구", LoopMath.WithExact(near, GhostText.Name, 50f).ahead.Value.Name);
            Assert.AreEqual(GhostText.Name, LoopMath.WithExact(near, GhostText.Name, -12f).behind.Value.Name);
        }

        [Test]
        public void RecordsWithoutSamplesRunAtAnEvenPace()
        {
            var t = GhostTrack.From(3000, 1200, 1.0, new List<float>());
            Assert.AreEqual(1500f, t.DistanceAt(600), 0.01f);
            Assert.IsNull(GhostTrack.From(0, 1200, 1.0, null), "거리 0이면 고스트 없음");
        }

        [Test]
        public void PoseStartsWhereIStartedAndStandsWhilePaused()
        {
            var g = new GhostRun(Steady(10f, 360, 1000), startIslandM: 4500f);
            var p = g.PoseAt(180, moving: true);
            Assert.AreEqual(5000f, p.IslandDistanceM, 0.01f, "내 출발 자리 + 고스트 거리");
            Assert.AreEqual(10f, p.SpeedKmh, 0.01f);
            Assert.AreEqual(0f, g.PoseAt(180, moving: false).SpeedKmh, "멈춤이면 걸음 0");
        }

        [Test]
        public void BannerTellsHowFarAheadOrBehind()
        {
            Assert.AreEqual("어제의 나보다 +42m 앞서요", GhostText.Banner(1042f, 1000f, false));
            Assert.AreEqual("어제의 나보다 18m 뒤처져요", GhostText.Banner(982f, 1000f, false), "뒤처져도 몇 m인지(11/18 민재)");
            Assert.AreEqual("어제의 나와 나란히 달려요", GhostText.Banner(1000.4f, 1000f, false));
            Assert.AreEqual("어제의 나보다 +1.2km 앞서요", GhostText.Banner(2250f, 1000f, false));
            Assert.AreEqual("어제의 나는 30m 앞에서 멈췄어요", GhostText.Banner(970f, 1000f, true));
            Assert.AreEqual("어제의 나보다 +5m 더 달렸어요", GhostText.Banner(1005f, 1000f, true));
        }

        [Test]
        public void SummaryAndDetailLines()
        {
            var r = new RunSummary { DistanceM = 5130, DurationS = 1880, AvgPaceSPerKm = 368 };
            Assert.AreEqual("5.1km · 31:20", GhostText.Summary(r));
            Assert.AreEqual("31:20 · 평균 6'08\"/km", GhostText.Detail(r));
        }

        [Test]
        public void StartBodyCarriesTheGhostRun()
        {
            var s = new RunSession(RunGoal.Default(GoalKind.Distance), () => 0) { GhostRunId = "run_g" };
            s.Start(0f);
            Assert.AreEqual("run_g", s.StartBody("map_crs_lake_loop").GhostRunId);
            Assert.IsNull(new RunSetup(RunGoal.Default(GoalKind.Distance)).GhostRunId, "고르지 않으면 없음");
        }

        [Test]
        public void SetupHasGhostCardAndStartsOff()
        {
            var host = new VisualElement();
            var view = new RunSetupView(host, new CourseDetail { CourseId = "crs_lake_loop", Name = "노을 호수 한 바퀴" },
                                        new RunSetupPrefs(new MemoryPrefs()));
            // Start()는 주간 목표를 서버에 물어서 부르지 않음. 만들 때부터 꺼진 모양
            Assert.IsFalse(host.Q("ghost-switch").ClassListContains("ghost-switch--on"), "D-01: 처음엔 꺼짐");
            Assert.IsFalse(host.Q("ghost-picked").IsVisible());
            Assert.AreEqual(GhostText.OffNote, host.Q<Label>("ghost-note").text);
            Assert.IsFalse(host.Q("ghost-pick").IsVisible());
            Assert.IsFalse(host.Q("ghost-empty").IsVisible());
        }
    }
}
