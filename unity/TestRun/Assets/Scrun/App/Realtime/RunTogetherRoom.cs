using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Scrun.App
{
    // 실시간 방에서 받은 메시지(docs/api/realtime.md)로 다른 러너 목록을 유지한다. 전송(WebSocket)과 떨어져 있어 테스트로 확인.
    // 다시 붙으면 welcome이 방 상태를 통째로 다시 주므로, 그 사이 들어오고 나간 사람만 Joined·Left로 알린다(처음 welcome은 알림 없음).
    public sealed class RunTogetherRoom
    {
        readonly Func<double> clock;
        readonly Dictionary<string, RemoteRunner> byId = new Dictionary<string, RemoteRunner>();
        readonly List<RemoteRunner> runners = new List<RemoteRunner>();
        bool welcomed;

        public RunTogetherRoom(Func<double> clock = null) => this.clock = clock ?? RealtimeClock.Now;

        public string MyUserId { get; private set; }
        public float CourseLengthM { get; private set; }
        public IReadOnlyList<RemoteRunner> Runners => runners;

        // 서버 시각 − 앱 시각(ms, 왕복 절반 보정). pong을 받기 전엔 null. 페이스 봇(S4 이후)이 씀
        public double? ServerOffsetMs { get; private set; }
        public double? RoundTripMs { get; private set; }

        public event Action<RemoteRunner> Joined;
        public event Action<RemoteRunner> Left;
        public event Action Welcomed; // welcome을 받음(처음·다시 붙을 때마다)

        // 메시지 하나. 모르는 종류·모양은 false(무시). 연결을 끊을 정도는 아님
        public bool Apply(string json)
        {
            JObject m;
            try { m = JObject.Parse(json); }
            catch (Exception) { return false; }
            double now = clock();
            try
            {
                switch ((string)m["type"])
                {
                    case "welcome": return Welcome(m, now);
                    case "states": return States(m, now);
                    case "joined":
                        if (!(m["runner"] is JObject r) || !Add(r, now, out var added)) return false;
                        Joined?.Invoke(added);
                        return true;
                    case "left":
                        return Remove((string)m["userId"], notify: true);
                    case "pong": return Pong(m, now);
                    default: return false;
                }
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ArgumentException ||
                                      e is OverflowException)
            {
                return false; // 필드 모양이 틀림(숫자 자리에 글자 등)
            }
        }

        public void Clear()
        {
            byId.Clear();
            runners.Clear();
            welcomed = false;
        }

        bool Welcome(JObject m, double now)
        {
            MyUserId = (string)m["you"];
            CourseLengthM = (float?)m["courseLengthM"] ?? CourseLengthM;
            var seen = new HashSet<string>();
            if (m["runners"] is JArray list)
                foreach (var t in list)
                {
                    if (!(t is JObject r)) continue;
                    string id = (string)r["userId"];
                    if (string.IsNullOrEmpty(id)) continue;
                    seen.Add(id);
                    if (byId.TryGetValue(id, out var known)) SampleFrom(known, r, now);
                    else if (Add(r, now, out var added) && welcomed) Joined?.Invoke(added);
                }
            foreach (var id in new List<string>(byId.Keys))
                if (!seen.Contains(id)) Remove(id, notify: welcomed);
            welcomed = true;
            Welcomed?.Invoke();
            return true;
        }

        bool States(JObject m, double now)
        {
            if (!(m["runners"] is JArray list)) return false;
            foreach (var t in list)
                if (t is JObject r && byId.TryGetValue((string)r["userId"] ?? "", out var known))
                    SampleFrom(known, r, now);
            return true;
        }

        bool Pong(JObject m, double now)
        {
            double? sent = (double?)m["t"], server = (double?)m["serverTime"];
            if (sent == null || server == null) return false;
            double nowMs = now * 1000;
            double rtt = nowMs - sent.Value;
            if (rtt < 0) return false;
            RoundTripMs = rtt;
            ServerOffsetMs = server.Value + rtt / 2 - nowMs;
            return true;
        }

        bool Add(JObject r, double now, out RemoteRunner added)
        {
            added = null;
            string id = (string)r["userId"];
            if (string.IsNullOrEmpty(id) || id == MyUserId) return false;
            if (byId.TryGetValue(id, out var known)) // 이미 있음(joined가 겹침): 값만 갱신, 알림 없음
            {
                SampleFrom(known, r, now);
                return false;
            }
            added = new RemoteRunner(id, (string)r["nickname"], CourseLengthM);
            SampleFrom(added, r, now);
            byId[id] = added;
            runners.Add(added);
            return true;
        }

        bool Remove(string id, bool notify)
        {
            if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var gone)) return false;
            byId.Remove(id);
            runners.Remove(gone);
            if (notify) Left?.Invoke(gone);
            return true;
        }

        static void SampleFrom(RemoteRunner runner, JObject r, double now)
        {
            float? d = (float?)r["d"], v = (float?)r["v"];
            if (d == null || v == null) return;
            runner.Sample(d.Value, v.Value, (bool?)r["running"] ?? false, now);
        }
    }

    // 앱 시계(초). 실시간 메시지 시각·내다보기에 씀. 스레드 상관없이 읽을 수 있다
    public static class RealtimeClock
    {
        static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
        public static double Now() => Watch.Elapsed.TotalSeconds;
    }
}
