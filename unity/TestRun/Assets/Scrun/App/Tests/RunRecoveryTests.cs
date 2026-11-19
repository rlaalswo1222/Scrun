using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0052: 앱이 꺼진 뒤 달리기 복구(BL-37) — 폰 진행 기록·고르기·마무리·인증 가능 여부·E-01
    public class RunRecoveryTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 19, 1, 0, 0, DateTimeKind.Utc);
        string dir;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "scrun-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        static ActiveRunRecord Record(string user = "u1", string runId = "run_1", DateTime? last = null, int seconds = 130) => new ActiveRunRecord
        {
            UserId = user,
            Start = new RunStartBody { ClientRunId = "c1", MapInstanceId = "map_crs_lake_loop", StartedAt = Now.AddMinutes(-30),
                                       Goal = new RunGoalBody { Type = "DISTANCE", Value = 3000 } },
            RunId = runId,
            CourseName = "노을 호수 한 바퀴",
            DistanceM = 320,
            DurationS = seconds,
            Samples = new List<float>(new float[seconds]),
            LastAtUtc = last ?? Now.AddMinutes(-3),
        };

        static ActiveRun Server(string runId = "run_1", DateTime? started = null, DateTime? last = null) => new ActiveRun
        {
            Run = new RunSummary { RunId = runId, StartedAt = started ?? Now.AddMinutes(-30), CourseName = "노을 호수 한 바퀴",
                                   DistanceM = 300, DurationS = 120 },
            LastActiveAt = last ?? Now.AddMinutes(-4),
        };

        // ---------- 진행 기록 ----------
        [Test]
        public void StoreSavesLoadsAndClearsOnlyItsOwnRun()
        {
            var store = new ActiveRunStore(Path.Combine(dir, "active-run.json"));
            Assert.IsNull(store.Load(), "없으면 null");
            store.Save(Record());
            var r = store.Load();
            Assert.AreEqual(("c1", "run_1", 130), (r.Start.ClientRunId, r.RunId, r.Samples.Count));
            store.Clear("other");
            Assert.IsNotNull(store.Load(), "새 달리기가 덮어쓴 뒤 옛 clientRunId로 지우면 그대로");
            store.Save(Record(runId: "run_1b"));
            Assert.AreEqual("run_1b", store.Load().RunId, "통째로 바꿔 씀");
            store.Clear("c1");
            Assert.IsNull(store.Load());

            File.WriteAllText(Path.Combine(dir, "active-run.json"), "{깨진");
            Assert.IsNull(store.Load(), "깨진 파일은 무시(서버 active만 봄)");
        }

        // ---------- 고르기 ----------
        [Test]
        public void PhoneRecordWinsForTheSameRun()
        {
            var c = RunRecovery.Pick(Record(), Server(), "u1", Now);
            Assert.IsTrue(c.Run.FromPhone, "폰 쪽이 마지막 1분까지 있음");
            Assert.AreEqual((320, 130), (c.Run.DistanceM, c.Run.DurationS));
            Assert.AreEqual(Now.AddMinutes(-3), c.Run.FinishBody.EndedAt, "종료 = 마지막으로 남긴 시각");
            Assert.AreEqual(3, c.Run.ToPending("u1").Chunks.Count, "130칸 → 60·60·10(서버는 같은 offsetS를 덮어씀)");
            Assert.IsFalse(c.ClearPhone);
        }

        [Test]
        public void RunTheServerNeverHeardOfIsRecoveredFromThePhone()
        {
            var c = RunRecovery.Pick(Record(runId: null), new ActiveRun(), "u1", Now);
            Assert.IsNull(c.Run.RunId);
            Assert.AreEqual("c1", c.Run.ToPending("u1").Start.ClientRunId, "마무리할 때 같은 clientRunId로 시작부터");

            var lost = RunRecovery.Pick(Record(runId: null), Server(runId: "run_9"), "u1", Now);
            Assert.AreEqual("run_9", lost.Run.RunId, "서버가 받았는데 응답만 못 받음(같은 시작 시각) → 그 기록");
        }

        [Test]
        public void ServerDecidesWhenThePhoneRunIsNoLongerActive()
        {
            var gone = RunRecovery.Pick(Record(), new ActiveRun(), "u1", Now);
            Assert.IsNull(gone.Run, "서버에선 이미 끝났거나 버림·기한 지남");
            Assert.IsTrue(gone.ClearPhone);

            var offline = RunRecovery.Pick(Record(), null, "u1", Now);
            Assert.IsTrue(offline.Run.FromPhone, "서버에 못 물으면 폰 기록대로");

            var finishing = Record();
            finishing.Finish = new RunFinishBody { EndedAt = Now.AddMinutes(-2), Sensor = new RunStatsBody { DistanceM = 330, DurationS = 140 } };
            var resend = RunRecovery.Pick(finishing, new ActiveRun(), "u1", Now);
            Assert.AreSame(finishing.Finish, resend.Run.FinishBody, "종료를 보내던 중 꺼짐 → 같은 본문으로 다시(서버는 처음 결과)");
        }

        [Test]
        public void ServerOnlyRunEndsAtItsLastUpdate()
        {
            var c = RunRecovery.Pick(null, Server(), "u1", Now);
            Assert.IsFalse(c.Run.FromPhone);
            Assert.AreEqual(Now.AddMinutes(-4), c.Run.EndedAtUtc);
            Assert.AreEqual((300, 120), (c.Run.FinishBody.Sensor.DistanceM, c.Run.FinishBody.Sensor.DurationS));
            Assert.AreEqual(0, c.Run.ToPending("u1").Chunks.Count, "시계열은 이미 서버에");
            Assert.IsNull(RunRecovery.Pick(null, new ActiveRun(), "u1", Now).Run);
        }

        [Test]
        public void OtherAccountsAndOldRecordsAreNotAsked()
        {
            var other = RunRecovery.Pick(Record(user: "u2"), new ActiveRun(), "u1", Now);
            Assert.IsNull(other.Run);
            Assert.IsFalse(other.ClearPhone, "다른 계정 기록은 그 계정이 다시 로그인하면 물음");
            var old = RunRecovery.Pick(Record(last: Now.AddHours(-24).AddMinutes(-1)), null, "u1", Now);
            Assert.IsNull(old.Run);
            Assert.IsTrue(old.ClearPhone, "24시간 지남(BL-37 ③)");
            Assert.IsNull(RunRecovery.Pick(Record(), Server(), null, Now).Run, "로그인 전");
        }

        // ---------- 인증 가능 여부(정할 것 2 가): 서버가 정함 ----------
        [Test]
        public void VerifyFollowsTheServer()
        {
            var r = RunRecovery.Pick(Record(), null, "u1", Now).Run;
            Assert.IsTrue(new RecoveredRunScreen(r, new RunSummary { RunId = "run_1", Verification = VerifyStatus.Pending }).CanVerify);
            Assert.IsFalse(new RecoveredRunScreen(r, new RunSummary { RunId = "run_1", Verification = VerifyStatus.Unverified }).CanVerify,
                           "멈춘 뒤 10분이 지나 서버가 바로 미인증으로 닫음");
        }

        [Test]
        public void EndScreenShowsVerifyOnlyWhenItCan()
        {
            var r = RunRecovery.Pick(Record(), null, "u1", Now).Run;
            var can = new RecoveredRunScreen(r, new RunSummary { RunId = "run_1", Verification = VerifyStatus.Pending });
            can.Create(null);
            Assert.IsTrue(can.Root.Q("verify").IsVisible());
            Assert.AreEqual(RecoveryText.Skip, can.Root.Q<Button>("skip").text);
            Assert.AreEqual("0.32km · 02:10", can.Root.Q<Label>("stats").text);

            var late = new RecoveredRunScreen(r, new RunSummary { RunId = "run_1", Verification = VerifyStatus.Unverified });
            late.Create(null);
            Assert.IsFalse(late.Root.Q("verify").IsVisible());
            Assert.AreEqual(RecoveryText.SeeResult, late.Root.Q<Button>("skip").text);
            StringAssert.Contains("10분이 지나", late.Root.Q<Label>("body").text);
        }

        // ---------- 마무리 ----------
        sealed class Remote : IRunsRemote
        {
            public readonly List<string> Calls = new List<string>();
            public Exception FinishError;
            public Exception AllError;
            public Exception PutError;    // 서버가 이미 끝낸 러닝: 시계열 올리기가 RUN_ALREADY_FINISHED
            public Exception FinishOnce;  // 첫 종료만 실패

            public Task<RunStarted> StartAsync(RunStartBody b)
            {
                Calls.Add("start");
                return AllError != null ? Task.FromException<RunStarted>(AllError) : Task.FromResult(new RunStarted { RunId = "run_new" });
            }

            public Task PutSamplesAsync(string runId, RunSamplesBody b)
            {
                Calls.Add($"put {runId} {b.OffsetS}");
                var e = AllError ?? PutError;
                return e != null ? Task.FromException(e) : Task.CompletedTask;
            }

            public Task<RunSummary> FinishAsync(string runId, RunFinishBody b)
            {
                Calls.Add($"finish {runId} {b.Sensor.DistanceM} sent={b.SentAt.HasValue}");
                var e = AllError ?? FinishError ?? FinishOnce;
                FinishOnce = null;
                return e != null ? Task.FromException<RunSummary>(e)
                    : Task.FromResult(new RunSummary { RunId = runId, Verification = VerifyStatus.Pending });
            }

            public Task SkipVerificationAsync(string runId) => Task.CompletedTask;
        }

        static Task NoDelay(int ms, CancellationToken ct) => Task.CompletedTask;

        [Test]
        public async Task FinishSendsEverythingAndClearsThePhoneRecord()
        {
            var journal = new ActiveRunStore(Path.Combine(dir, "active-run.json"));
            var pending = new PendingRunStore(Path.Combine(dir, "pending.json"));
            journal.Save(Record(runId: null));
            var run = RunRecovery.Pick(journal.Load(), new ActiveRun(), "u1", Now).Run;
            var remote = new Remote();
            var r = await RunRecovery.FinishAsync(run, "u1", remote, pending, journal, NoDelay);
            Assert.AreEqual(RunRecovery.FinishOutcome.Saved, r.Outcome);
            Assert.AreEqual("run_new", r.RunId);
            CollectionAssert.AreEqual(new[] { "start", "put run_new 0", "put run_new 60", "put run_new 120", "finish run_new 320 sent=True" }, remote.Calls);
            Assert.IsNull(journal.Load());
            Assert.AreEqual(0, pending.Load().Count);
        }

        [Test]
        public async Task OfflineFinishKeepsTheRunOnThePhoneForLater()
        {
            var journal = new ActiveRunStore(Path.Combine(dir, "active-run.json"));
            var pending = new PendingRunStore(Path.Combine(dir, "pending.json"));
            journal.Save(Record());
            var run = RunRecovery.Pick(journal.Load(), null, "u1", Now).Run;
            var remote = new Remote { AllError = new ApiException(ApiFailure.Network, "연결 안 됨") };
            var r = await RunRecovery.FinishAsync(run, "u1", remote, pending, journal, NoDelay);
            Assert.AreEqual(RunRecovery.FinishOutcome.KeptLocal, r.Outcome);
            Assert.AreEqual("c1", pending.Load()[0].Start.ClientRunId, "다음에 홈에서 보냄");
            Assert.IsNull(journal.Load(), "진행 기록은 끝(남긴 기록이 맡음)");
        }

        [Test]
        public async Task AlreadyFinishedAndLoginExpiredAreHandled()
        {
            var journal = new ActiveRunStore(Path.Combine(dir, "active-run.json"));
            var pending = new PendingRunStore(Path.Combine(dir, "pending.json"));
            journal.Save(Record());
            var run = RunRecovery.Pick(journal.Load(), Server(), "u1", Now).Run;
            var done = new Remote { FinishError = new ApiException(ApiFailure.Server, "끝남", 409, "RUN_ALREADY_FINISHED") };
            var r = await RunRecovery.FinishAsync(run, "u1", done, pending, journal, NoDelay);
            Assert.AreEqual((RunRecovery.FinishOutcome.AlreadyClosed, "run_1"), (r.Outcome, r.RunId));
            Assert.IsNull(journal.Load());

            journal.Save(Record());
            var expired = new Remote { AllError = new ApiException(ApiFailure.NotLoggedIn, "로그인", 401) };
            Assert.ThrowsAsync<ApiException>(() => RunRecovery.FinishAsync(run, "u1", expired, pending, journal, NoDelay));
            Assert.IsNotNull(journal.Load(), "로그인 만료: 그대로 두고 다시 로그인하면 또 물음");
            Assert.AreEqual(0, pending.Load().Count);
        }

        // 리뷰: 종료를 보내던 중 꺼진 기록 → 서버는 이미 끝내 시계열 올리기가 막힘 → 같은 종료 본문으로 처음 결과를 받음
        [Test]
        public async Task FinishInterruptedWhileSendingGetsTheFirstResult()
        {
            var journal = new ActiveRunStore(Path.Combine(dir, "active-run.json"));
            var pending = new PendingRunStore(Path.Combine(dir, "pending.json"));
            var rec = Record();
            rec.Finish = new RunFinishBody { EndedAt = Now.AddMinutes(-2), Sensor = new RunStatsBody { DistanceM = 330, DurationS = 140 } };
            journal.Save(rec);
            var run = RunRecovery.Pick(journal.Load(), new ActiveRun(), "u1", Now).Run;
            var done = new ApiException(ApiFailure.Server, "끝남", 409, "RUN_ALREADY_FINISHED");
            var remote = new Remote { PutError = done };
            var r = await RunRecovery.FinishAsync(run, "u1", remote, pending, journal, NoDelay);
            Assert.AreEqual(RunRecovery.FinishOutcome.Saved, r.Outcome);
            CollectionAssert.AreEqual(new[] { "put run_1 0", "finish run_1 330 sent=True" }, remote.Calls);
            Assert.IsNull(journal.Load());
        }
    }
}
