using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 달리기 멈춤·끝과 기록 올리기(계획 0037): RunSession 일시정지·시계열·목표·종료, RunRecordUploader, PendingRunStore
    public class RunEndTests
    {
        sealed class Clock
        {
            public double T;
            public double Now() => T;
        }

        static RunSession Started(Clock c, GoalKind kind = GoalKind.Distance, int units = 30)
        {
            var s = new RunSession(new RunGoal(kind, units), c.Now);
            s.Start(100f);
            return s;
        }

        // ---------- 일시정지 ----------
        [Test]
        public void 일시정지하면_시간이_멈추고_계속은_걸음이_다시_잡혀야_흐른다()
        {
            var c = new Clock();
            var s = Started(c);
            c.T = 10;
            Assert.IsTrue(s.Pause());
            Assert.AreEqual(RunPhase.Paused, s.Phase);
            c.T = 70;
            Assert.AreEqual(10, s.ElapsedSec, 1e-9, "멈춘 60초는 안 셈");

            Assert.IsTrue(s.Resume());
            Assert.AreEqual(RunPhase.Resuming, s.Phase);
            c.T = 75;
            s.Update(0f);
            Assert.AreEqual(10, s.ElapsedSec, 1e-9, "계속을 눌러도 걸음(아바타)이 없으면 시간 멈춤");
            s.Update(0.4f);
            Assert.AreEqual(RunPhase.Resuming, s.Phase, "멈추며 줄어드는 속도·흔들림은 다시 걷기가 아님");
            s.Update(3f);
            Assert.AreEqual(RunPhase.Running, s.Phase);
            c.T = 80;
            Assert.AreEqual(15, s.ElapsedSec, 1e-9);
        }

        [Test]
        public void 다시_걷기_전에_또_멈출_수_있고_시작_전_끝난_뒤엔_멈추지_않는다()
        {
            var c = new Clock();
            var s = new RunSession(new RunGoal(GoalKind.Distance, 30), c.Now);
            Assert.IsFalse(s.Pause(), "시작 전");
            s.Start(0f);
            s.Pause();
            s.Resume();
            Assert.IsTrue(s.Pause(), "다시 걷기 전에 또 멈춤");
            Assert.IsFalse(s.Resume() && s.Resume(), "Resuming에서 또 계속은 안 됨");
            s.Finish(0f, 0f);
            Assert.IsFalse(s.Pause());
            Assert.IsFalse(s.Resume());
        }

        // ---------- 시계열 ----------
        [Test]
        public void 시계열은_달린_시간_1초마다_한_칸_일시정지_구간은_빠진다()
        {
            var c = new Clock();
            var s = Started(c);
            for (int i = 1; i <= 25; i++) { c.T = i * 0.2; s.Update(8f); } // 5초
            Assert.AreEqual(5, s.Samples.Count);
            s.Pause();
            c.T = 100;
            s.Update(0f);
            Assert.AreEqual(5, s.Samples.Count, "멈춘 동안 칸 없음");
            s.Resume();
            s.Update(10f); // 다시 걷기 → 이 순간부터 다시 셈
            for (int i = 1; i <= 10; i++) { c.T = 100 + i * 0.2; s.Update(10f); } // 2초
            Assert.AreEqual(7, s.Samples.Count);
            CollectionAssert.AreEqual(new[] { 8f, 8f, 8f, 8f, 8f, 10f, 10f }, s.Samples);
        }

        [Test]
        public void 틱이_밀린_채_멈추면_멈추기_전까지_칸을_마지막_속도로_채운다()
        {
            var c = new Clock();
            var s = Started(c);
            c.T = 2; s.Update(9f);
            c.T = 10; // 8초 동안 틱 없음(앱이 잠깐 멈춤)
            s.Pause();
            Assert.AreEqual(10, s.Samples.Count, "달린 시간 10초 = 10칸");
            Assert.AreEqual(9f, s.Samples[9]);
            s.Finish(500f, 0f);
            Assert.AreEqual(10, s.FinishBody.Sensor.DurationS);
        }

        // ---------- 목표 ----------
        [Test]
        public void 목표는_처음_닿을_때_한_번만_멈춘_동안엔_안_알린다()
        {
            var c = new Clock();
            var s = Started(c, GoalKind.Distance, 30); // 3.0 km, 시작 거리 100
            Assert.IsFalse(s.CheckGoalReached(3099f));
            s.Pause();
            Assert.IsFalse(s.CheckGoalReached(3100f), "멈춘 동안");
            s.Resume();
            s.Update(5f);
            Assert.IsTrue(s.CheckGoalReached(3100f));
            Assert.IsFalse(s.CheckGoalReached(3200f), "한 번만");

            var t = Started(c, GoalKind.Time, 5); // 5분(시간 목표 최소)
            c.T += 299;
            Assert.IsFalse(t.CheckGoalReached(0f));
            c.T += 1;
            Assert.IsTrue(t.CheckGoalReached(0f));
        }

        // ---------- 끝 ----------
        [Test]
        public void 끝내면_거리_시간이_굳고_본문은_다시_보내도_같다()
        {
            var c = new Clock();
            var s = Started(c);
            c.T = 30;
            s.Pause();
            c.T = 90; // 멈춘 60초
            var body = s.Finish(1300f, 0f);
            Assert.IsNotNull(body);
            Assert.AreEqual(1200, body.Sensor.DistanceM);
            Assert.AreEqual(30, body.Sensor.DurationS, "일시정지 뺀 달린 시간");
            Assert.AreEqual(DateTimeKind.Utc, body.EndedAt.Kind);
            Assert.IsNull(s.Finish(5000f, 0f), "두 번째는 null");
            Assert.AreEqual(1200f, s.DistanceM(9999f), "끝난 뒤 거리는 그대로");
            Assert.AreEqual(body.EndedAt, s.FinishBody.EndedAt);
            Assert.AreEqual(30, s.ElapsedSec, 1e-9);
        }

        [Test]
        public void 달리는_중에_끝내면_마지막_칸까지_채운다()
        {
            var c = new Clock();
            var s = Started(c);
            c.T = 3.5;
            s.Finish(110f, 9f);
            Assert.AreEqual(3, s.Samples.Count);
            Assert.AreEqual(3, s.FinishBody.Sensor.DurationS);
        }

        [Test]
        public void 화면_글자()
        {
            Assert.AreEqual("2.10km · 12:08\n기록은 계기판을 인증하면 저장돼요. 인증하지 않으면 미인증 기록으로 남아요.",
                            RunEndText.ConfirmBody(2105f, 728));
            Assert.AreEqual("목표 달성! 3.0km 완주", RunEndText.GoalBanner(new RunGoal(GoalKind.Distance, 30)));
            Assert.AreEqual("2.10km를 달렸어요. 폰을 거치대에서 내려 러닝머신 계기판을 촬영해 인증해요.", RunEndText.DoneBody(2100f));
            Assert.AreEqual("", RunEndText.Saving(RunSaveState.Saved));
        }

        // ---------- 올리기 ----------
        sealed class FakeRemote : IRunsRemote
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<RunSamplesBody> Puts = new List<RunSamplesBody>();
            public Queue<Exception> Fail = new Queue<Exception>();
            public RunFinishBody Finished;

            Exception Next() => Fail.Count > 0 ? Fail.Dequeue() : null;

            public Task<RunStarted> StartAsync(RunStartBody b)
            {
                Calls.Add("start");
                var e = Next();
                if (e != null) return Task.FromException<RunStarted>(e);
                return Task.FromResult(new RunStarted { RunId = "run_1", ClientRunId = b.ClientRunId });
            }

            public Task PutSamplesAsync(string runId, RunSamplesBody b)
            {
                Calls.Add($"put {b.OffsetS} {b.SpeedsKmh.Length}");
                var e = Next();
                if (e != null) return Task.FromException(e);
                Puts.Add(b);
                return Task.CompletedTask;
            }

            public Task SkipVerificationAsync(string runId)
            {
                Calls.Add("skip " + runId);
                return Task.CompletedTask;
            }

            public Task<RunSummary> FinishAsync(string runId, RunFinishBody b)
            {
                Calls.Add("finish " + runId);
                var e = Next();
                if (e != null) return Task.FromException<RunSummary>(e);
                Finished = b;
                return Task.FromResult(new RunSummary { RunId = runId, Verification = "PENDING", DistanceM = b.Sensor.DistanceM });
            }
        }

        static readonly Func<int, CancellationToken, Task> NoWait = (ms, ct) => Task.CompletedTask;
        static ApiException Offline => new ApiException(ApiFailure.Network, "끊김");

        string dir;
        PendingRunStore store;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "scrun-pending-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            store = new PendingRunStore(Path.Combine(dir, "pending-runs.json"));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(dir, true);

        static RunSession WithSamples(Clock c, int seconds)
        {
            var s = Started(c);
            for (int i = 1; i <= seconds; i++) { c.T = i; s.Update(8f); }
            return s;
        }

        [Test]
        public async Task 달리는_동안_다_찬_1분_묶음만_runId가_생긴_뒤에_올린다()
        {
            var c = new Clock();
            var s = WithSamples(c, 130);
            var remote = new FakeRemote();
            var u = new RunRecordUploader(s, "map_x", remote, store, "u1", NoWait, c.Now);
            await u.UploadDueAsync(CancellationToken.None);
            Assert.IsEmpty(remote.Calls, "runId가 아직 없음");
            s.RunId = "run_1";
            await u.UploadDueAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "put 0 60", "put 60 60" }, remote.Calls);
            Assert.AreEqual(120, u.Uploaded);
        }

        [Test]
        public async Task 달리는_동안_실패하면_30초_뒤에_다시()
        {
            var c = new Clock();
            var s = WithSamples(c, 60);
            s.RunId = "run_1";
            var remote = new FakeRemote();
            remote.Fail.Enqueue(Offline);
            var u = new RunRecordUploader(s, "map_x", remote, store, "u1", NoWait, c.Now);
            await u.UploadDueAsync(CancellationToken.None);
            await u.UploadDueAsync(CancellationToken.None);
            Assert.AreEqual(1, remote.Calls.Count, "바로 다시 안 보냄");
            c.T += 30;
            await u.UploadDueAsync(CancellationToken.None);
            Assert.AreEqual(60, u.Uploaded);
        }

        [Test]
        public async Task 끝내면_남은_묶음과_종료를_보내고_runId가_없으면_시작부터()
        {
            var c = new Clock();
            var s = WithSamples(c, 75);
            s.Finish(1100f, 8f);
            var remote = new FakeRemote();
            var u = new RunRecordUploader(s, "map_x", remote, store, "u1", NoWait, c.Now);
            var r = await u.FinishAsync(CancellationToken.None);
            Assert.AreEqual("run_1", r.RunId);
            CollectionAssert.AreEqual(new[] { "start", "put 0 60", "put 60 15", "finish run_1" }, remote.Calls);
            Assert.AreEqual("run_1", s.RunId);
            Assert.AreEqual(1000, remote.Finished.Sensor.DistanceM);
            Assert.IsFalse(store.Has(s.ClientRunId));
        }

        [Test]
        public async Task 끝낼_때_연결이_안_되면_기기에_남기고_다음에_보낸다()
        {
            var c = new Clock();
            var s = WithSamples(c, 70);
            s.RunId = "run_1";
            s.Finish(1100f, 8f);
            var remote = new FakeRemote();
            for (int i = 0; i <= RunRecordUploader.MaxRetries; i++) remote.Fail.Enqueue(Offline); // 첫 묶음이 끝내 실패
            var u = new RunRecordUploader(s, "map_x", remote, store, "u1", NoWait, c.Now);
            Assert.IsNull(await u.FinishAsync(CancellationToken.None));
            Assert.IsTrue(store.Has(s.ClientRunId));

            var later = new FakeRemote();
            await store.FlushAsync("someone-else", later);
            Assert.IsEmpty(later.Calls, "다른 계정으로는 안 보냄");
            await store.FlushAsync("u1", later);
            CollectionAssert.AreEqual(new[] { "put 0 60", "put 60 10", "finish run_1" }, later.Calls);
            Assert.IsFalse(store.Has(s.ClientRunId));
        }

        [Test]
        public async Task 남긴_기록은_보낸_만큼_반영하고_서버가_안_받으면_버린다()
        {
            var c = new Clock();
            var s = WithSamples(c, 70);
            s.Finish(1100f, 8f);
            // 시작 성공 → 첫 묶음 성공 → 둘째 묶음 끝내 실패
            var r = new FakeRemote();
            r.Fail.Enqueue(null);
            r.Fail.Enqueue(null);
            for (int i = 0; i <= RunRecordUploader.MaxRetries; i++) r.Fail.Enqueue(Offline);
            var u = new RunRecordUploader(s, "map_x", r, store, "u1", NoWait, c.Now);
            Assert.IsNull(await u.FinishAsync(CancellationToken.None));
            var kept = store.Load()[0];
            Assert.AreEqual("run_1", kept.RunId, "시작은 됐으니 다시 시작하지 않음");
            Assert.AreEqual(1, kept.Chunks.Count, "보낸 첫 묶음은 빠짐");

            var rejected = new FakeRemote();
            rejected.Fail.Enqueue(new ApiException(ApiFailure.Server, "이미 끝남", 409, "RUN_ALREADY_FINISHED"));
            await store.FlushAsync("u1", rejected);
            Assert.IsFalse(store.Has(s.ClientRunId), "4xx는 다시 보내도 같아 버림");
        }

        [Test]
        public async Task 로그인_만료_429는_버리지_않고_남긴다()
        {
            var c = new Clock();
            var s = WithSamples(c, 10);
            s.RunId = "run_1";
            s.Finish(200f, 8f);
            var remote = new FakeRemote();
            remote.Fail.Enqueue(new ApiException(ApiFailure.NotLoggedIn, "만료", 401, "TOKEN_EXPIRED"));
            Assert.IsNull(await new RunRecordUploader(s, "map_x", remote, store, "u1", NoWait, c.Now).FinishAsync(CancellationToken.None));
            Assert.IsTrue(store.Has(s.ClientRunId), "로그인 만료");

            var busy = new FakeRemote();
            busy.Fail.Enqueue(new ApiException(ApiFailure.Server, "잦음", 429, "RATE_LIMITED"));
            await store.FlushAsync("u1", busy);
            Assert.IsTrue(store.Has(s.ClientRunId), "429면 다음에");
        }

        [Test]
        public async Task 오프라인에서_촬영_없이_마무리를_고르면_보낸_뒤_미인증으로()
        {
            var c = new Clock();
            var s = WithSamples(c, 5);
            s.RunId = "run_1";
            s.Finish(100f, 8f);
            var off = new FakeRemote();
            for (int i = 0; i <= RunRecordUploader.MaxRetries; i++) off.Fail.Enqueue(Offline);
            await new RunRecordUploader(s, "map_x", off, store, "u1", NoWait, c.Now).FinishAsync(CancellationToken.None);
            store.MarkSkipVerification(s.ClientRunId);
            var later = new FakeRemote();
            await store.FlushAsync("u1", later);
            CollectionAssert.AreEqual(new[] { "put 0 5", "finish run_1", "skip run_1" }, later.Calls);
        }

        [Test]
        public async Task 달리는_동안_올리던_묶음은_끝낼_때_기다려_두_번_안_올린다()
        {
            var c = new Clock();
            var s = WithSamples(c, 61);
            s.RunId = "run_1";
            var remote = new FakeRemote();
            var u = new RunRecordUploader(s, "map_x", remote, store, "u1", NoWait, c.Now);
            u.StartUploadDue(CancellationToken.None);
            s.Finish(500f, 8f);
            await u.FinishAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "put 0 60", "put 60 1", "finish run_1" }, remote.Calls);
        }

        [Test]
        public void 토큰의_사용자_ID()
        {
            // {"sub":"01ABC","typ":"access"}
            string payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"sub\":\"01ABC\",\"typ\":\"access\"}"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.AreEqual("01ABC", RunRecordUploader.TokenSubject("h." + payload + ".sig"));
            Assert.IsNull(RunRecordUploader.TokenSubject(null));
            Assert.IsNull(RunRecordUploader.TokenSubject("not-a-jwt"));
        }
    }
}
