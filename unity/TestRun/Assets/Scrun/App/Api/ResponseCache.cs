using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Scrun.App
{
    // 화면 첫 페이지 응답을 기기에 두었다가 앱을 다시 켤 때 먼저 보이기(계획 0074 A). ApiClient가 성공한 GET을 넣고,
    // 화면은 Peek으로 먼저 그린 뒤 평소처럼 새로 받는다(그때는 스켈레톤 없이 조용히, Feedback.Load).
    // - 넣는 것: Cacheable 목록(홈·기록 첫 쪽·친구·피드 첫 쪽·마이)만. 다음 쪽(cursor)·상세는 안 넣음
    // - 버리는 때: 로그인이 바뀜(새 로그인·로그아웃·계정 삭제, Session.LoginChanged), 쓰기 요청이 성공함(무엇이 바뀌었는지
    //   따지지 않고 전부 — 러닝 종료·친구 추가 뒤 지난 값이 보이지 않게, 달리는 중 시계열은 빼고), 앱 버전이 바뀜, MaxAgeDays 지남
    // - 기기 저장: Application.persistentDataPath 아래(앱 안에서만 읽힘). iOS는 기본 파일 보호(재부팅 뒤 첫 잠금 해제 전엔 암호화)
    // - 파일을 못 읽고 쓰는 것은 기능을 막지 않음(캐시가 없는 것처럼)
    public sealed class ResponseCache
    {
        public const int MaxAgeDays = 7;

        static readonly HashSet<string> Exact = new HashSet<string>
        {
            "/v1/home", "/v1/runs", "/v1/users/me/weekly-goal",
            "/v1/friends", "/v1/friends/running", "/v1/feed",
            "/v1/users/me", "/v1/users/me/progress", "/v1/users/me/calibration",
        };

        readonly string dir;
        readonly string appVersion;
        readonly Func<DateTime> utcNow;

        public ResponseCache(string dir, string appVersion, Func<DateTime> utcNow = null)
        {
            this.dir = dir;
            this.appVersion = appVersion ?? "";
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        // 화면 첫 페이지인 GET만. 기록은 달 칩(?month=)도 첫 쪽이면 넣음
        public static bool Cacheable(string path) =>
            path != null && (Exact.Contains(path) || path.StartsWith("/v1/runs?month=", StringComparison.Ordinal) && !path.Contains("cursor="));

        // 이 쓰기가 성공하면 캐시를 버리나(달리는 중 시계열은 화면 값을 바꾸지 않고 자주 와서 뺌. 종료·버리기는 버림)
        public static bool InvalidatedBy(string method, string path) =>
            method != "GET" && !(path != null && path.StartsWith("/v1/runs/", StringComparison.Ordinal) && path.EndsWith("/samples", StringComparison.Ordinal));

        public void Put(string path, string body)
        {
            if (!Cacheable(path) || string.IsNullOrEmpty(body)) return;
            Try(() =>
            {
                Directory.CreateDirectory(dir);
                var entry = new Entry { v = appVersion, at = utcNow().ToUniversalTime().Ticks, body = body };
                string file = FileFor(path), tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(entry), Encoding.UTF8);
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file); // 반쯤 쓴 파일을 읽지 않게
            });
        }

        // 쓸 수 있는 응답 본문(공통 껍데기 그대로). 없거나 버전이 다르거나 오래됐으면 null
        public string Get(string path)
        {
            if (!Cacheable(path)) return null;
            string body = null;
            Try(() =>
            {
                string file = FileFor(path);
                if (!File.Exists(file)) return;
                var e = JsonConvert.DeserializeObject<Entry>(File.ReadAllText(file, Encoding.UTF8));
                if (e == null || e.v != appVersion || string.IsNullOrEmpty(e.body)) return;
                var age = utcNow().ToUniversalTime() - new DateTime(e.at, DateTimeKind.Utc);
                if (age < TimeSpan.Zero || age > TimeSpan.FromDays(MaxAgeDays)) return;
                body = e.body;
            });
            return body;
        }

        public void Clear() => Try(() => { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); });

        string FileFor(string path)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return Path.Combine(dir, sb + ".json");
            }
        }

        static void Try(Action io)
        {
            try { io(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException ||
                                      e is ArgumentException || e is NotSupportedException)
            {
                UnityEngine.Debug.LogWarning("[캐시] " + e.Message); // 캐시는 없어도 됨
            }
        }

        class Entry
        {
            public string v;    // 앱 버전(응답 모양이 바뀌었을 수 있음)
            public long at;     // 넣은 시각(UTC ticks)
            public string body; // 응답 본문(껍데기 그대로)
        }
    }
}
