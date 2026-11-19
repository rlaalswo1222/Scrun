using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Scrun.App
{
    // 앱이 꺼진 뒤 달리기 복구(계획 0052, BL-37). 화면 없이 EditMode로 확인하는 부분:
    //  - ActiveRunRecord·ActiveRunStore: 달리는 동안 5초마다·백그라운드로 갈 때 폰에 남기는 진행 기록(정할 것 1 가)
    //  - RunRecovery.Pick: 폰 기록과 서버 GET /runs/active 중 무엇을 물을지
    //  - RecoveredRun·FinishAsync: 마무리(남은 시계열 + finish). 인증할 수 있는지는 서버가 정함(종료 본문의 sentAt − endedAt이 10분 넘으면
    //    바로 미인증, 정할 것 2 가) → E-01은 서버 응답이 인증 대기(PENDING)일 때만 촬영

    // 달리는 중인 기록 하나(앱 데이터 폴더 active-run.json). 정상으로 끝내면 지움(그 뒤는 PendingRunStore가 맡음)
    public sealed class ActiveRunRecord
    {
        public string UserId { get; set; }          // 로그인 토큰의 sub. 다른 계정이면 건드리지 않음
        public RunStartBody Start { get; set; }     // POST /runs 본문(서버가 못 받았어도 같은 clientRunId로 다시 보냄)
        public string RunId { get; set; }           // 서버가 준 ID(못 받았으면 null)
        public string CourseName { get; set; }
        public int DistanceM { get; set; }
        public int DurationS { get; set; }          // 일시정지를 뺀 달린 시간
        public List<float> Samples { get; set; } = new List<float>(); // 1초 칸 속도(km/h)
        public DateTime LastAtUtc { get; set; }     // 마지막으로 남긴 시각 = 복구할 때 종료 시각
        public RunFinishBody Finish { get; set; }   // 종료를 눌렀는데 보내기 전에 꺼졌으면 그 본문(다시 보내도 서버는 처음 결과)
    }

    public sealed class ActiveRunStore
    {
        static ActiveRunStore defaultStore;
        public static ActiveRunStore Default =>
            defaultStore ??= new ActiveRunStore(Path.Combine(Application.persistentDataPath, "active-run.json"));

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        readonly string path;
        readonly object gate = new object();

        public ActiveRunStore(string path) => this.path = path;

        // 없거나 깨졌으면 null(서버 active만 봄)
        public ActiveRunRecord Load()
        {
            lock (gate)
            {
                try
                {
                    if (!File.Exists(path)) return null;
                    var r = JsonConvert.DeserializeObject<ActiveRunRecord>(File.ReadAllText(path), Json);
                    return r?.Start?.ClientRunId == null ? null : r;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[복구] 진행 기록 파일을 못 읽음(무시): " + e.Message);
                    return null;
                }
            }
        }

        // 통째로 바꿔 씀. 쓰는 도중 앱이 꺼져도 옛 파일이 남게 임시 파일에 쓴 뒤 바꿈
        public void Save(ActiveRunRecord record)
        {
            if (record?.Start?.ClientRunId == null) return;
            lock (gate)
            {
                string tmp = path + ".tmp";
                try
                {
                    File.WriteAllText(tmp, JsonConvert.SerializeObject(record, Json));
                    if (File.Exists(path)) File.Replace(tmp, path, null);
                    else File.Move(tmp, path);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[복구] 진행 기록을 못 씀: " + e.Message);
                }
            }
        }

        // clientRunId가 있으면 그 기록일 때만 지움(새 달리기가 이미 덮어썼으면 그대로)
        public void Clear(string clientRunId = null)
        {
            lock (gate)
            {
                try
                {
                    if (!File.Exists(path)) return;
                    if (clientRunId != null && Load()?.Start?.ClientRunId != clientRunId) return;
                    File.Delete(path);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[복구] 진행 기록을 못 지움: " + e.Message);
                }
            }
        }
    }

    // 물어볼 러닝 하나(폰 기록 또는 서버만)
    public sealed class RecoveredRun
    {
        public string RunId { get; set; }
        public RunStartBody Start { get; set; }     // 폰 기록에서 왔으면 있음(서버가 못 받은 러닝도 다시 시작부터)
        public string CourseName { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public int DistanceM { get; set; }
        public int DurationS { get; set; }
        public DateTime EndedAtUtc { get; set; }    // 마지막으로 기록된 시각
        public List<float> Samples { get; set; }    // 폰 기록에서 왔으면 1초 칸 전부(서버는 같은 offsetS를 덮어씀)
        public RunFinishBody Finish { get; set; }   // 종료를 눌렀던 기록이면 그 본문 그대로
        public bool FromPhone => Start != null;
        public string ClientRunId => Start?.ClientRunId;

        public RunFinishBody FinishBody => Finish ?? new RunFinishBody
        {
            EndedAt = EndedAtUtc,
            Sensor = new RunStatsBody { DistanceM = DistanceM, DurationS = DurationS },
        };

        // RunRecordUploader.Send로 보낼 모양(runId가 없으면 시작부터, 시계열 전부, 종료)
        public PendingRun ToPending(string userId) => new PendingRun
        {
            UserId = userId,
            Start = Start,
            RunId = RunId,
            Chunks = Samples == null ? new List<RunSamplesBody>() : RunRecordUploader.Chunks(Samples, 0),
            Finish = FinishBody,
        };
    }

    public static class RunRecovery
    {
        public static readonly TimeSpan Recoverable = TimeSpan.FromHours(24); // BL-37 ③(서버 RECOVERABLE과 같음)

        public struct Choice
        {
            public RecoveredRun Run;  // 물어볼 러닝(없으면 null)
            public bool ClearPhone;   // 폰 기록을 지움(24시간 지남·서버에서 이미 끝났거나 버려짐)
        }

        // local: 폰 기록, server: GET /runs/active 결과(못 물었으면 null), userId: 지금 계정
        public static Choice Pick(ActiveRunRecord local, ActiveRun server, string userId, DateTime nowUtc)
        {
            var c = new Choice();
            if (string.IsNullOrEmpty(userId)) return c;
            if (local != null && local.UserId != userId) local = null; // 다른 계정 기록은 그대로 둠(그 계정이 다시 로그인하면 물음)
            if (local != null && nowUtc - local.LastAtUtc > Recoverable)
            {
                c.ClearPhone = true;
                local = null;
            }
            var remote = server?.Run;
            if (local != null)
            {
                if (local.RunId == null)
                {
                    // 서버가 출발 신호를 받았는데 응답만 못 받은 경우: 같은 시작 시각이면 같은 러닝(다시 보내도 같은 clientRunId라 하나)
                    string id = remote != null && Math.Abs((remote.StartedAt.ToUniversalTime() - local.Start.StartedAt.ToUniversalTime()).TotalSeconds) < 1
                        ? remote.RunId : null;
                    c.Run = FromPhone(local, id, remote);
                    return c;
                }
                // 서버에 물을 수 없음(연결 안 됨): 폰 기록대로 묻고, 마무리가 안 되면 폰에 남김
                // 서버가 같은 러닝을 아직 달리는 중이라고 함 / 종료를 보내던 중 꺼짐(같은 값으로 다시 보내면 처음 결과)
                if (server == null || remote?.RunId == local.RunId || local.Finish != null)
                {
                    c.Run = FromPhone(local, local.RunId, remote?.RunId == local.RunId ? remote : null);
                    return c;
                }
                // 서버에선 이미 끝났거나 버렸거나 기한이 지남 → 서버가 맞음
                c.ClearPhone = true;
            }
            if (remote != null) c.Run = FromServer(server);
            return c;
        }

        static RecoveredRun FromPhone(ActiveRunRecord r, string runId, RunSummary remote) => new RecoveredRun
        {
            RunId = runId,
            Start = r.Start,
            CourseName = r.CourseName ?? remote?.CourseName,
            StartedAtUtc = r.Start.StartedAt.ToUniversalTime(),
            DistanceM = r.DistanceM,
            DurationS = r.DurationS,
            EndedAtUtc = r.LastAtUtc,
            Samples = r.Samples ?? new List<float>(),
            Finish = r.Finish,
        };

        static RecoveredRun FromServer(ActiveRun a)
        {
            var r = a.Run;
            var started = r.StartedAt.ToUniversalTime();
            // 서버 거리·시간은 올라온 1분 묶음까지. 종료 시각은 마지막 갱신(없는 옛 서버면 시작 + 달린 시간)
            var ended = a.LastActiveAt?.ToUniversalTime() ?? started.AddSeconds(r.DurationS);
            if (ended < started.AddSeconds(r.DurationS)) ended = started.AddSeconds(r.DurationS);
            return new RecoveredRun
            {
                RunId = r.RunId,
                CourseName = r.CourseName,
                StartedAtUtc = started,
                DistanceM = r.DistanceM,
                DurationS = r.DurationS,
                EndedAtUtc = ended,
            };
        }

        public enum FinishOutcome
        {
            Saved,          // 서버가 끝냄(Summary 있음)
            KeptLocal,      // 연결이 안 돼 폰에 남김(다음에 홈에서 보냄, 인증은 못 함)
            AlreadyClosed,  // 서버에서 이미 끝난 기록(다른 값으로 끝났음) → 그 기록 결과로
            Rejected,       // 서버가 받지 않음(버렸거나 기한이 지남)
        }

        public struct FinishResult
        {
            public FinishOutcome Outcome;
            public RunSummary Summary;
            public string RunId;
            public ApiException Error;
        }

        // I-04 "마무리하기": 남은 시계열 + 종료. 어떤 결과든 폰 진행 기록은 지움(남길 것은 PendingRunStore로)
        public static async Task<FinishResult> FinishAsync(RecoveredRun run, string userId, IRunsRemote remote = null,
                                                           PendingRunStore pending = null, ActiveRunStore journal = null,
                                                           Func<int, CancellationToken, Task> delay = null)
        {
            remote ??= new ApiRunsRemote();
            pending ??= PendingRunStore.Default;
            journal ??= ActiveRunStore.Default;
            delay ??= (ms, ct) => Task.Delay(ms, ct);
            var p = run.ToPending(userId);
            FinishResult result;
            try
            {
                var summary = await RunRecordUploader.Send(p, remote, delay, CancellationToken.None, RunRecordUploader.MaxRetries);
                result = new FinishResult { Outcome = FinishOutcome.Saved, Summary = summary, RunId = summary.RunId };
            }
            catch (ApiException e) when (e.Failure == ApiFailure.NotLoggedIn)
            {
                throw; // 로그인 화면으로. 진행 기록은 그대로(다시 로그인하면 또 물음)
            }
            catch (ApiException e) when (RunRecordUploader.ShouldKeep(e))
            {
                pending.Add(p); // 연결 안 됨·5xx·429: 다음에 홈에서 보냄(보낸 만큼 runId·묶음 반영)
                result = new FinishResult { Outcome = FinishOutcome.KeptLocal, RunId = p.RunId, Error = e };
            }
            catch (ApiException e) when (e.Code == "RUN_ALREADY_FINISHED" && run.Finish != null && p.RunId != null)
            {
                // 종료를 보내던 중 꺼진 기록: 서버는 이미 끝냄(그래서 시계열 올리기가 막힘) → 같은 종료 본문은 처음 결과를 돌려줌(리뷰)
                try
                {
                    p.Finish.SentAt = DateTime.UtcNow;
                    var summary = await remote.FinishAsync(p.RunId, p.Finish);
                    result = new FinishResult { Outcome = FinishOutcome.Saved, Summary = summary, RunId = summary.RunId };
                }
                catch (ApiException again) when (again.Failure != ApiFailure.NotLoggedIn && !RunRecordUploader.ShouldKeep(again))
                {
                    result = new FinishResult { Outcome = FinishOutcome.AlreadyClosed, RunId = p.RunId, Error = again };
                }
            }
            catch (ApiException e)
            {
                Debug.LogWarning("[복구] 서버가 마무리를 받지 않음: " + e);
                result = new FinishResult
                {
                    Outcome = e.Code == "RUN_ALREADY_FINISHED" ? FinishOutcome.AlreadyClosed : FinishOutcome.Rejected,
                    RunId = p.RunId,
                    Error = e,
                };
            }
            if (run.ClientRunId != null) journal.Clear(run.ClientRunId); // 결과가 정해짐(남길 것은 PendingRunStore에)
            return result;
        }
    }

    // 글자(Figma I-04·E-01을 아직 못 읽어 임시 → 읽은 뒤 맞춤·상준 확인)
    public static class RecoveryText
    {
        public const string Title = "끝나지 않은 러닝이 있어요";
        public const string Body = "앱이 꺼져서 마무리하지 못했어요. 마무리하면 기록으로 남아요.";
        public const string Finish = "마무리하기";
        public const string Discard = "버리기";
        public const string DiscardTitle = "이 러닝을 버릴까요?";
        public const string DiscardBody = "기록이 지워지고 되돌릴 수 없어요.";
        public const string Cancel = "취소";
        public const string EndTitle = "수고했어요!";       // E-01(D-22와 같은 제목)
        public const string Verify = "계기판 촬영하기";       // D-22와 같음
        public const string Skip = "촬영 없이 마무리 (미인증)";
        public const string SeeResult = "결과 보기";
        public const string KeptLocal = "인터넷이 안 돼 기록을 폰에 저장했어요. 연결되면 미인증 기록으로 올라가요.";
        public const string Rejected = "이 러닝은 더 이상 마무리할 수 없어요.";
        public const string DiscardFailedTitle = "버리지 못했어요";

        // "노을 호수 한 바퀴 · 11.19 (수) 오전 7:40"
        public static string Info(RecoveredRun r, TimeZoneInfo zone = null) =>
            (string.IsNullOrEmpty(r.CourseName) ? "" : r.CourseName + " · ") + FriendText.RunDate(r.StartedAtUtc, zone);

        // "2.10km · 12:08"
        public static string Stats(int meters, int seconds) => FriendText.Km(meters) + " · " + FriendText.Duration(seconds);

        // E-01 본문: 찍을 수 있으면(서버가 인증 대기) D-22와 같은 안내, 아니면 왜 못 찍는지
        public static string EndBody(int meters, bool canVerify) => canVerify
            ? $"{FriendText.Km(meters)}를 달렸어요. 폰을 거치대에서 내려 러닝머신 계기판을 촬영해 인증해요."
            : $"{FriendText.Km(meters)}를 달렸어요. 앱이 꺼진 지 10분이 지나 계기판으로 인증할 수 없어 미인증 기록으로 남아요.";
    }
}
