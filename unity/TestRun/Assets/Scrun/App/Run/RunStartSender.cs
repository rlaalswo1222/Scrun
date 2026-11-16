using System;
using System.Threading;
using System.Threading.Tasks;

namespace Scrun.App
{
    // 카운트다운이 끝나면 기록은 앱에서 바로 시작하고, 서버에 알리는 POST /v1/runs는 뒤에서 보낸다(계획 0035 정한 것 2).
    // 네트워크 때문에 달리기가 늦게 시작되지 않게. 같은 clientRunId라 다시 보내도 서버 기록은 하나.
    // 다시 보낼 수 있는 실패(연결 안 됨·읽지 못한 응답·서버 5xx)만 몇 번 다시, 나머지(400·로그인 만료)는 바로 포기.
    // 그래도 안 되면 runId 없이 달린다 → 종료·인증 쪽(민재 S5, 지환 S4)이 그때 다시 보낼 수 있게 요청을 남겨 둠(RunSession.StartBody)
    public sealed class RunStartSender
    {
        public static readonly System.Collections.Generic.IReadOnlyList<int> RetryDelaysMs = new[] { 2000, 5000, 10000 };

        readonly Func<RunStartBody, Task<RunStarted>> send;
        readonly Func<int, CancellationToken, Task> delay;

        public RunStartSender(Func<RunStartBody, Task<RunStarted>> send = null, Func<int, CancellationToken, Task> delay = null)
        {
            this.send = send ?? (b => Api.Runs.StartAsync(b));
            this.delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
        }

        // 성공하면 응답, 끝내 못 보내면 null. 맵을 나가면(ct) 그만둔다(OperationCanceledException)
        public async Task<RunStarted> SendAsync(RunStartBody body, CancellationToken ct)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            for (int attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await send(body);
                }
                catch (ApiException e) when (Retryable(e) && attempt < RetryDelaysMs.Count)
                {
                    await delay(RetryDelaysMs[attempt], ct);
                }
                catch (ApiException e)
                {
                    UnityEngine.Debug.LogWarning("[러닝 시작] 서버에 못 알림(runId 없이 계속): " + e);
                    return null;
                }
            }
        }

        public static bool Retryable(ApiException e) =>
            e.Failure == ApiFailure.Network || e.Failure == ApiFailure.BadResponse ||
            (e.Failure == ApiFailure.Server && e.Status >= 500);
    }
}
