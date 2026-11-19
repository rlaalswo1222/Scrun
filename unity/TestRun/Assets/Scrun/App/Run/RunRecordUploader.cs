using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scrun.App
{
    // 서버 runs API 중 달리는 쪽(계획 0037). 테스트는 가짜로 바꿔 끼운다
    public interface IRunsRemote
    {
        Task<RunStarted> StartAsync(RunStartBody body);
        Task PutSamplesAsync(string runId, RunSamplesBody body);
        Task<RunSummary> FinishAsync(string runId, RunFinishBody body);
        Task SkipVerificationAsync(string runId);
    }

    public sealed class ApiRunsRemote : IRunsRemote
    {
        public Task<RunStarted> StartAsync(RunStartBody body) => Api.Runs.StartAsync(body);
        public Task PutSamplesAsync(string runId, RunSamplesBody body) => Api.Runs.PutSamplesAsync(runId, body);
        public Task<RunSummary> FinishAsync(string runId, RunFinishBody body) => Api.Runs.FinishAsync(runId, body);
        public Task SkipVerificationAsync(string runId) => Api.Runs.SkipVerificationAsync(runId);
    }

    // 달리기 기록을 서버에 올린다(계획 0037 2번). 달리는 동안엔 60초마다 속도 묶음, 끝나면 남은 묶음 + 종료.
    // 다시 보낼 수 있는 실패(연결 안 됨·5xx)는 같이 뛰기 다시 붙기와 같은 간격으로 다시 보내고, 끝낼 때까지 못 보낸 것은
    // 기기에 남겨(PendingRunStore) 다음에 홈에 올 때 보낸다(BL-37과 이어짐). 같은 clientRunId·offsetS·endedAt이라 다시 보내도 서버 기록은 하나.
    public sealed class RunRecordUploader
    {
        public const int ChunkSamples = 60;  // 1초 간격 60칸 = 1분
        public const int MaxRetries = 4;     // 끝낼 때 한 요청에 1 + 2 + 4 + 8 = 15초까지
        const double RetryLaterSec = 30;     // 달리는 동안 묶음 올리기가 실패하면 이만큼 뒤에 다시(끝낼 때는 다 올림)

        readonly RunSession session;
        readonly string mapInstanceId;
        readonly IRunsRemote remote;
        readonly Func<int, CancellationToken, Task> delay;
        readonly Func<double> clock;
        readonly PendingRunStore store;
        readonly string userId;
        int uploaded;      // 서버에 올린 칸 수
        Task inflight = Task.CompletedTask; // 달리는 동안 올리는 중인 묶음(끝낼 때 이걸 기다림)
        double nextTryAt;

        public RunRecordUploader(RunSession session, string mapInstanceId, IRunsRemote remote = null, PendingRunStore store = null,
                                 string userId = null, Func<int, CancellationToken, Task> delay = null, Func<double> clock = null)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.mapInstanceId = mapInstanceId;
            this.remote = remote ?? new ApiRunsRemote();
            this.store = store ?? PendingRunStore.Default;
            this.userId = userId;
            this.delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
            this.clock = clock ?? RealtimeClock.Now;
        }

        public int Uploaded => uploaded;

        // 서버가 지금은 못 받지만 나중엔 받을 실패: 연결 안 됨·5xx(IsTransient), 로그인 만료(다시 로그인하면 됨), 요청 너무 잦음(429).
        // 이것들로 끝낸 기록을 버리면 안 됨(계획 0037 리뷰). 나머지 4xx는 다시 보내도 같음
        public static bool ShouldKeep(ApiException e) =>
            e.IsTransient || e.Failure == ApiFailure.NotLoggedIn || e.Status == 429;

        // 달리는 동안(화면이 자주 부름): 다 찬 1분 묶음이 있으면 뒤에서 올린다. runId가 아직 없으면(POST /runs 다시 보내는 중) 기다림
        public void StartUploadDue(CancellationToken ct)
        {
            if (!inflight.IsCompleted || session.RunId == null || session.Samples.Count - uploaded < ChunkSamples || clock() < nextTryAt) return;
            inflight = UploadDueAsync(ct);
        }

        // 테스트·StartUploadDue. 예외를 밖으로 내지 않음(뒤에서 돌아 아무도 안 기다릴 수 있음)
        public async Task UploadDueAsync(CancellationToken ct)
        {
            if (session.RunId == null || session.Samples.Count - uploaded < ChunkSamples || clock() < nextTryAt) return;
            try
            {
                while (session.Samples.Count - uploaded >= ChunkSamples)
                {
                    ct.ThrowIfCancellationRequested();
                    await remote.PutSamplesAsync(session.RunId, Chunk(session.Samples, uploaded, ChunkSamples));
                    uploaded += ChunkSamples;
                }
            }
            catch (OperationCanceledException) { } // 끝냄·맵 나감: 남은 것은 끝낼 때 다 올림
            catch (ApiException e)
            {
                nextTryAt = clock() + RetryLaterSec; // 끝낼 때 남은 것을 다 올리므로 여기선 버리지 않음
                Debug.LogWarning("[기록] 속도 묶음을 못 올림(나중에 다시): " + e);
            }
        }

        // 끝난 뒤(D-22로 갈 때). 서버 요약을 돌려주고, 끝내 못 보냈으면 기기에 남기고 null
        public async Task<RunSummary> FinishAsync(CancellationToken ct)
        {
            var finish = session.FinishBody ?? throw new InvalidOperationException("아직 끝나지 않은 달리기");
            try { await inflight; } catch (Exception) { } // 달리는 동안 올리던 묶음이 끝나길(같은 묶음을 두 번 세지 않게, 예외는 거기서 처리)
            var pending = new PendingRun
            {
                UserId = userId,
                Start = session.StartBody(mapInstanceId),
                RunId = session.RunId,
                Chunks = Chunks(session.Samples, uploaded),
                Finish = finish,
            };
            try
            {
                var r = await Send(pending, remote, delay, ct, MaxRetries);
                session.RunId = pending.RunId;
                return r;
            }
            catch (Exception e) when ((e is ApiException a && ShouldKeep(a)) || e is OperationCanceledException)
            {
                // 연결이 안 됨·로그인 만료·보내는 중에 맵을 나감 → 보낸 만큼(runId·묶음)을 반영해 기기에 남김
                Debug.LogWarning("[기록] 서버에 다 못 보냄 → 기기에 남김(다음에 홈에서 보냄): " + e.Message);
                if (pending.RunId != null) session.RunId = pending.RunId;
                store.Add(pending);
                if (e is OperationCanceledException) throw;
                return null;
            }
            catch (ApiException e)
            {
                Debug.LogWarning("[기록] 서버가 받지 않음(다시 보내도 같음, 버림): " + e);
                return null;
            }
        }

        // 남겨 둔 기록 하나 보내기: runId가 없으면 시작부터(같은 clientRunId라 기록 하나), 남은 묶음, 종료. 보낸 것은 pending에서 지워 나감
        internal static async Task<RunSummary> Send(PendingRun p, IRunsRemote remote, Func<int, CancellationToken, Task> delay,
                                                    CancellationToken ct, int retries)
        {
            if (p.RunId == null)
            {
                if (p.Start == null) throw new ApiException(ApiFailure.BadResponse, "시작 요청이 없는 기록");
                p.RunId = (await Retry(() => remote.StartAsync(p.Start), delay, ct, retries)).RunId;
            }
            while (p.Chunks.Count > 0)
            {
                var c = p.Chunks[0];
                await Retry(async () => { await remote.PutSamplesAsync(p.RunId, c); return true; }, delay, ct, retries);
                p.Chunks.RemoveAt(0);
            }
            var summary = await Retry(() =>
            {
                p.Finish.SentAt = DateTime.UtcNow; // 멈춘 뒤 얼마나 지나 보냈는지(앱이 꺼졌다 늦게 복구·폰에 남겼다 나중에 보냄 → 인증 불가)
                return remote.FinishAsync(p.RunId, p.Finish);
            }, delay, ct, retries);
            if (p.SkipVerification)
            {
                // 오프라인에서 "촬영 없이 마무리"를 골랐던 기록. 인증 API(지환 BL-18) 쪽 실패는 기록 저장을 막지 않음
                try { await remote.SkipVerificationAsync(p.RunId); }
                catch (ApiException e) { Debug.LogWarning("[기록] 미인증 마무리를 서버에 못 알림(인증 대기로 남음): " + e); }
            }
            return summary;
        }

        static async Task<T> Retry<T>(Func<Task<T>> op, Func<int, CancellationToken, Task> delay, CancellationToken ct, int retries)
        {
            for (int attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await op();
                }
                catch (ApiException e) when (e.IsTransient && attempt < retries)
                {
                    await delay((int)(ReconnectPolicy.DelaySec(attempt) * 1000), ct);
                }
            }
        }

        static RunSamplesBody Chunk(IReadOnlyList<float> s, int from, int count)
        {
            var speeds = new float[count];
            for (int i = 0; i < count; i++) speeds[i] = s[from + i];
            return new RunSamplesBody { OffsetS = from * RunSession.SampleIntervalSec, IntervalS = RunSession.SampleIntervalSec, SpeedsKmh = speeds };
        }

        // from부터 끝까지 1분씩(마지막은 짧을 수 있음). 복구(RecoveredRun)도 씀
        internal static List<RunSamplesBody> Chunks(IReadOnlyList<float> s, int from)
        {
            var list = new List<RunSamplesBody>();
            for (int i = from; i < s.Count; i += ChunkSamples) list.Add(Chunk(s, i, Math.Min(ChunkSamples, s.Count - i)));
            return list;
        }

        // 로그인 토큰(JWT)의 사용자 ID(sub). 남겨 둔 기록을 다른 계정으로 보내지 않게
        public static string TokenSubject(string jwt)
        {
            try
            {
                var parts = jwt?.Split('.');
                if (parts == null || parts.Length < 2) return null;
                string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                return (string)JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)))["sub"];
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    // 서버에 못 보낸 기록(끝난 달리기). 다음에 홈에 오면 보낸다
    public sealed class PendingRun
    {
        public string UserId { get; set; }
        public RunStartBody Start { get; set; }
        public string RunId { get; set; }
        public List<RunSamplesBody> Chunks { get; set; } = new List<RunSamplesBody>();
        public RunFinishBody Finish { get; set; }
        public bool SkipVerification { get; set; } // D-22 "촬영 없이 마무리"를 고름(보낸 뒤 미인증으로)
    }

    // 기기에 남긴 기록(앱 데이터 폴더의 JSON 하나). 계정마다 그 계정으로만 보낸다
    public sealed class PendingRunStore
    {
        static PendingRunStore defaultStore;
        public static PendingRunStore Default =>
            defaultStore ??= new PendingRunStore(Path.Combine(Application.persistentDataPath, "pending-runs.json"));

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        static bool flushing;

        readonly string path;
        readonly object gate = new object();

        public PendingRunStore(string path) => this.path = path;

        public List<PendingRun> Load()
        {
            lock (gate)
            {
                try
                {
                    return File.Exists(path)
                        ? JsonConvert.DeserializeObject<List<PendingRun>>(File.ReadAllText(path), Json) ?? new List<PendingRun>()
                        : new List<PendingRun>();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[기록] 남긴 기록 파일을 못 읽음(새로 시작): " + e.Message);
                    return new List<PendingRun>();
                }
            }
        }

        void Save(List<PendingRun> runs)
        {
            lock (gate)
            {
                try
                {
                    if (runs.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
                    File.WriteAllText(path, JsonConvert.SerializeObject(runs, Json));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[기록] 남긴 기록을 못 씀: " + e.Message);
                }
            }
        }

        public bool Has(string clientRunId) => Load().Exists(r => r.Start?.ClientRunId == clientRunId);

        // 남긴 기록에 "촬영 없이 마무리"를 적어 둠(보낼 때 미인증으로)
        public void MarkSkipVerification(string clientRunId)
        {
            lock (gate)
            {
                var all = Load();
                var run = all.Find(r => r.Start?.ClientRunId == clientRunId);
                if (run == null) return;
                run.SkipVerification = true;
                Save(all);
            }
        }

        public void Add(PendingRun run)
        {
            lock (gate)
            {
                var all = Load();
                all.RemoveAll(r => r.Start?.ClientRunId != null && r.Start.ClientRunId == run.Start?.ClientRunId);
                all.Add(run);
                Save(all);
            }
        }

        // 이 계정의 남긴 기록을 보낸다(한 요청에 한 번씩만, 안 되면 다음에). 보냈거나 서버가 받지 않는 것(4xx)은 지운다
        public async Task FlushAsync(string userId, IRunsRemote remote = null, CancellationToken ct = default)
        {
            if (flushing || string.IsNullOrEmpty(userId)) return;
            flushing = true;
            try
            {
                remote ??= new ApiRunsRemote();
                foreach (var run in Load())
                {
                    if (run.UserId != userId) continue;
                    bool done;
                    try
                    {
                        await RunRecordUploader.Send(run, remote, (ms, t) => Task.Delay(ms, t), ct, retries: 0);
                        done = true;
                    }
                    catch (ApiException e) when (!RunRecordUploader.ShouldKeep(e))
                    {
                        Debug.LogWarning("[기록] 남긴 기록을 서버가 받지 않음(버림): " + e);
                        done = true;
                    }
                    catch (ApiException)
                    {
                        done = false; // 연결 안 됨·로그인 만료 등 → 다음에
                    }
                    lock (gate)
                    {
                        var all = Load();
                        all.RemoveAll(r => r.Start?.ClientRunId == run.Start?.ClientRunId && r.UserId == run.UserId);
                        if (!done) all.Add(run); // 보낸 만큼(묶음·runId)은 반영해서 다시 남김
                        Save(all);
                    }
                }
            }
            finally
            {
                flushing = false;
            }
        }
    }
}
