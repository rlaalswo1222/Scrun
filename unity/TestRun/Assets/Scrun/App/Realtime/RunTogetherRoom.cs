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

        // 서버 시각 − 앱 시각(ms, 왕복 절반 보정). pong을 받기 전엔 null. 받은 값이 언제 나왔는지 어림하는 데 씀(SentAt, 계획 0074)
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
                    if (byId.TryGetValue(id, out var known))
                    {
                        SampleFrom(known, r, now);
                        LevelFrom(known, r); // 다시 붙었을 때 그 사이 다시 입장한 사람의 새 레벨(리뷰)
                    }
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
                LevelFrom(known, r);
                return false;
            }
            // bot은 true/false만 봄(다른 모양이면 사람으로: 형변환 예외로 메시지 전체를 버리지 않게)
            bool isBot = r["bot"]?.Type == JTokenType.Boolean && (bool)r["bot"];
            added = new RemoteRunner(id, (string)r["nickname"], CourseLengthM, isBot);
            SampleFrom(added, r, now);
            LevelFrom(added, r);
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

        // level(계획 0059): 1 이상 정수만(지금 서버는 1–6, 경험치 기준을 늘리면 더 커질 수 있음). 다른 모양·너무 큰 값은 모르는 것으로,
        // 메시지는 버리지 않음(int 범위 밖 정수를 (int)로 바꾸면 예외 → welcome 전체를 잃음, 리뷰). 봇엔 안 붙임
        static void LevelFrom(RemoteRunner runner, JObject r)
        {
            if (runner.IsBot || r["level"]?.Type != JTokenType.Integer) return;
            long lv = r["level"].Value<long>();
            if (lv >= 1 && lv <= 999) runner.Level = (int)lv;
        }

        // 받은 값이 상대 폰에서 나온 때(앱 시계, 초) 어림. 서버가 그 값을 받은 시각 at(ms, 서버 시계, 계획 0074)을 pong으로 잰
        // 시계 차이로 앱 시계로 바꾸고, 상대 폰 → 서버 몫은 내 왕복의 절반으로 어림(상대 왕복은 모름, 가설).
        // at·시계 차이가 없으면(옛 서버, 첫 pong 전) 받은 순간. 시계가 틀어져도 지금보다 미래·MaxAgeSec보다 옛날로는 안 감
        public const double MaxAgeSec = 2.0;

        internal double SentAt(JObject r, double now)
        {
            var at = r["at"];
            if (ServerOffsetMs == null || at == null || (at.Type != JTokenType.Integer && at.Type != JTokenType.Float)) return now;
            double sent = ((double)at - ServerOffsetMs.Value) / 1000.0 - (RoundTripMs ?? 0) / 2000.0;
            return Math.Min(now, Math.Max(now - MaxAgeSec, sent));
        }

        void SampleFrom(RemoteRunner runner, JObject r, double now)
        {
            float? d = (float?)r["d"], v = (float?)r["v"];
            if (d == null || v == null) return;
            runner.Sample(d.Value, v.Value, (bool?)r["running"] ?? false, now, SentAt(r, now));
            var run = r["run"]; // 이번 달리기 거리(러너 카드 D-12). 숫자가 아니면 모름
            runner.RunDistanceM = run != null && (run.Type == JTokenType.Float || run.Type == JTokenType.Integer) ? (float?)run : null;
        }
    }

    // 앱 시계(초). 실시간 메시지 시각·내다보기에 씀. 스레드 상관없이 읽을 수 있다
    public static class RealtimeClock
    {
        static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
        public static double Now() => Watch.Elapsed.TotalSeconds;
    }
}
