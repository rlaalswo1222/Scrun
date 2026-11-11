using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Scrun.App
{
    // 같이 뛰기 연결의 진짜 구현: .NET 내장 ClientWebSocket으로 우리 서버(FastAPI)의 /v1/realtime에 붙는다(계획 0029 정한 것 1).
    // 규칙은 docs/api/realtime.md. 메인 스레드에서 Start하면 await 뒤가 메인 스레드로 돌아오므로(UnitySynchronizationContext)
    // 방 상태·이벤트는 메인 스레드에서만 바뀐다.
    //   토큰은 Authorization 헤더로(주소에 넣으면 서버 접속 기록에 남음)
    //   상태 보내기 초당 2번(myState가 null이면 안 보냄: 섬이 열리기 전), ping 5초마다,
    //   서버가 5초 조용하면 끊고 다시 붙음(1·2·4·8초 간격), 닫는 코드가 4000번대면 멈춤(StopReason)
    //   Dispose(맵 나가기): leave를 보내고 닫기 인사까지 기다림(최대 LeaveWaitMs) → 다른 폰에서 바로 사라짐
    public sealed class WsRunTogether : IRunTogether
    {
        const double StateIntervalSec = 0.5;
        const double PingIntervalSec = 5;
        const double SilentSec = 5;      // 서버는 초당 2번 states를 보냄
        const int ConnectTimeoutMs = 8000;
        const int CloseWaitMs = 1000;
        const int LeaveWaitMs = 2000;    // 나갈 때 leave·닫기를 기다리는 최대 시간(그 뒤엔 그냥 끊음)
        const int MaxMessageBytes = 256 * 1024; // 20명 states는 약 2KB

        readonly Uri uri;
        readonly string authorization;
        readonly Func<MyRunState?> myState;
        readonly RunTogetherRoom room;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        bool started, closing;

        public WsRunTogether(RealtimeAccess access, Func<MyRunState?> myState)
        {
            if (access == null || string.IsNullOrEmpty(access.Url) || string.IsNullOrEmpty(access.Token))
                throw new ArgumentException("실시간 접속 정보가 없음(입장 응답 realtime)", nameof(access));
            uri = new Uri(access.Url);
            authorization = "Bearer " + access.Token;
            this.myState = myState ?? throw new ArgumentNullException(nameof(myState));
            room = new RunTogetherRoom();
            room.Joined += r => Joined?.Invoke(r);
            room.Left += r => Left?.Invoke(r);
        }

        public RunTogetherStatus Status { get; private set; } = RunTogetherStatus.Connecting;
        public RunTogetherStop StopReason { get; private set; }
        public IReadOnlyList<RemoteRunner> Runners => room.Runners;
        public double? RoundTripMs => room.RoundTripMs;
        public float CourseLengthM => room.CourseLengthM;

        public event Action<RemoteRunner> Joined;
        public event Action<RemoteRunner> Left;
        public event Action<RunTogetherStatus> StatusChanged;

        public void Start()
        {
            if (started) return;
            started = true;
            Application.quitting += Dispose; // Play를 멈춰도 연결이 남지 않게(도메인 리로드 꺼 둠)
            _ = RunAsync();
        }

        public void Dispose()
        {
            Application.quitting -= Dispose;
            if (closing) return;
            closing = true;
            if (Status != RunTogetherStatus.Stopped) Stop(RunTogetherStop.Closed);
            if (started) life.CancelAfter(LeaveWaitMs); // 연결 중이면 루프가 leave·닫기를 하고 끝냄. 못 하면 그때 끊음
            else life.Cancel();
        }

        async Task RunAsync()
        {
            int attempt = 0;
            while (!life.IsCancellationRequested && !closing)
            {
                SetStatus(attempt == 0 && room.MyUserId == null ? RunTogetherStatus.Connecting : RunTogetherStatus.Reconnecting);
                int? closeCode = null;
                bool welcomed = false;
                using (var ws = new ClientWebSocket())
                {
                    ws.Options.SetRequestHeader("Authorization", authorization);
                    try
                    {
                        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                        {
                            connect.CancelAfter(ConnectTimeoutMs);
                            await ws.ConnectAsync(uri, connect.Token);
                        }
                        closeCode = await Session(ws, () => welcomed = true);
                    }
                    catch (OperationCanceledException) when (life.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception e) when (e is WebSocketException || e is OperationCanceledException ||
                                              e is System.IO.IOException || e is System.Net.Sockets.SocketException)
                    {
                        Debug.LogWarning($"[같이 뛰기] 연결 끊김: {e.GetType().Name} {e.Message}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e); // 앱 버그(메시지 처리 등)도 다시 붙어서 이어감
                    }
                    // 소켓을 버리기 전에 닫기 인사(안 하면 서버가 10초 동안 죽은 연결에 계속 보냄)
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                        await CloseQuietly(ws);
                }
                if (closing || life.IsCancellationRequested) return;
                var stop = ReconnectPolicy.StopFor(closeCode);
                if (stop != RunTogetherStop.None)
                {
                    Debug.LogWarning($"[같이 뛰기] 서버가 닫음 {closeCode} → 다시 붙지 않음({stop})");
                    Stop(stop);
                    return;
                }
                if (welcomed) attempt = 0;
                double wait = ReconnectPolicy.DelaySec(attempt++);
                SetStatus(RunTogetherStatus.Reconnecting);
                try { await Task.Delay(TimeSpan.FromSeconds(wait), life.Token); }
                catch (OperationCanceledException) { return; }
            }
        }

        // 연결 하나가 끝날 때까지. 서버가 닫은 코드(없으면 null)
        async Task<int?> Session(ClientWebSocket ws, Action onWelcome)
        {
            using (var session = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
            {
                bool connected = false;
                void Welcomed()
                {
                    connected = true;
                    onWelcome();
                    if (!closing) SetStatus(RunTogetherStatus.Connected);
                }
                room.Welcomed += Welcomed;
                double lastHeard = RealtimeClock.Now();
                var receiving = Receive(ws, session.Token, text =>
                {
                    lastHeard = RealtimeClock.Now();
                    room.Apply(text);
                });
                double nextState = 0, nextPing = 0;
                try
                {
                    while (!receiving.IsCompleted)
                    {
                        if (closing)
                        {
                            // leave → 닫기 인사 → 서버가 닫을 때까지(받기를 취소하면 소켓이 강제로 끊겨 leave가 못 갈 수 있음)
                            await Send(ws, RealtimeMessages.Leave, life.Token);
                            await CloseQuietly(ws);
                            await Task.WhenAny(receiving, Task.Delay(CloseWaitMs));
                            return null;
                        }
                        double now = RealtimeClock.Now();
                        if (now - lastHeard > SilentSec)
                        {
                            Debug.LogWarning("[같이 뛰기] 서버가 5초 동안 조용함 → 다시 붙음");
                            return null;
                        }
                        if (connected && now >= nextState)
                        {
                            nextState = now + StateIntervalSec;
                            var s = myState();
                            if (s.HasValue) await Send(ws, RealtimeMessages.State(s.Value), session.Token);
                        }
                        if (connected && now >= nextPing)
                        {
                            nextPing = now + PingIntervalSec;
                            await Send(ws, RealtimeMessages.Ping(now * 1000), session.Token);
                        }
                        await Task.WhenAny(receiving, Task.Delay(50, session.Token));
                    }
                    return await receiving;
                }
                finally
                {
                    room.Welcomed -= Welcomed;
                    session.Cancel();
                    try { await receiving; } catch (Exception) { /* 받기를 멈춤(취소·끊김) */ }
                }
            }
        }

        // 받기: 메시지마다 onText. 서버가 닫으면 그 코드
        static async Task<int?> Receive(ClientWebSocket ws, CancellationToken ct, Action<string> onText)
        {
            var buffer = new byte[8192];
            var message = new List<byte>();
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return (int?)ws.CloseStatus ?? (int?)result.CloseStatus;
                message.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
                if (message.Count > MaxMessageBytes) throw new WebSocketException("메시지가 너무 큼");
                if (!result.EndOfMessage) continue;
                if (result.MessageType == WebSocketMessageType.Text)
                    onText(Encoding.UTF8.GetString(message.ToArray()));
                message.Clear();
            }
            return (int?)ws.CloseStatus;
        }

        static Task Send(ClientWebSocket ws, string text, CancellationToken ct) =>
            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, ct);

        static async Task CloseQuietly(ClientWebSocket ws)
        {
            try
            {
                using (var t = new CancellationTokenSource(CloseWaitMs))
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", t.Token);
            }
            catch (Exception) { /* 이미 끊김 */ }
        }

        void Stop(RunTogetherStop reason)
        {
            StopReason = reason;
            SetStatus(RunTogetherStatus.Stopped);
        }

        void SetStatus(RunTogetherStatus s)
        {
            if (Status == s) return;
            Status = s;
            StatusChanged?.Invoke(s);
        }
    }

    // 다시 붙을지·얼마나 기다릴지(docs/api/realtime.md "끊김")
    public static class ReconnectPolicy
    {
        static readonly double[] Delays = { 1, 2, 4, 8 };

        public static double DelaySec(int attempt) => Delays[Math.Max(0, Math.Min(attempt, Delays.Length - 1))];

        public static RunTogetherStop StopFor(int? closeCode)
        {
            switch (closeCode)
            {
                case 4001: return RunTogetherStop.ReplacedElsewhere;
                case 4401:
                case 4403: return RunTogetherStop.NeedsReentry;
                case 4400: return RunTogetherStop.ProtocolError;
                default:
                    return closeCode >= 4000 && closeCode < 5000 ? RunTogetherStop.ProtocolError : RunTogetherStop.None;
            }
        }
    }

    // 보내는 메시지(서버와 같은 모양, docs/api/realtime.md)
    public static class RealtimeMessages
    {
        public const string Leave = "{\"type\":\"leave\"}";

        static string Num(double x) => x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        public static string State(MyRunState s)
        {
            float d = float.IsNaN(s.D) || float.IsInfinity(s.D) ? 0f : s.D;
            float v = float.IsNaN(s.SpeedKmh) || float.IsInfinity(s.SpeedKmh) ? 0f : Math.Max(0f, s.SpeedKmh);
            return "{\"type\":\"state\",\"d\":" + Num(d) + ",\"v\":" + Num(v) + ",\"running\":" + (s.Running ? "true" : "false") + "}";
        }

        public static string Ping(double clientMs) => "{\"type\":\"ping\",\"t\":" + Num(Math.Round(clientMs, 1)) + "}";
    }
}
