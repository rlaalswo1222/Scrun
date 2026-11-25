using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 계획 0074: 화면 첫 페이지 캐시(A)와 access 미리 재발급(D)
    public class ResponseCacheTests
    {
        const string Base = "http://test";
        string dir;
        DateTime now;
        SynchronizationContext savedContext;
        FakeHttp http;
        ApiClient client;

        [SetUp]
        public void SetUp()
        {
            savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null); // await 뒤가 바로 이어지게
            dir = Path.Combine(Path.GetTempPath(), "scrun-cache-test-" + Guid.NewGuid().ToString("N"));
            now = new DateTime(2026, 11, 26, 3, 0, 0, DateTimeKind.Utc);
            http = new FakeHttp { Handler = r => FakeHttp.Ok("{\"nickname\":\"민트\"}") };
            client = new ApiClient(http, new Session(new MemorySecureStore()), () => Base) { Cache = Cache("1.0") };
            client.Session.SetTokens("a1", "r1");
        }

        [TearDown]
        public void TearDown()
        {
            SynchronizationContext.SetSynchronizationContext(savedContext);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        ResponseCache Cache(string version) => new ResponseCache(dir, version, () => now);

        static void Done(Task t)
        {
            Assert.IsTrue(t.IsCompleted, "가짜 응답이면 바로 끝나야 함");
            t.GetAwaiter().GetResult();
        }

        // ---------- 캐시 자체 ----------
        [Test]
        public void KeepsOnlyFirstPagesOfTabScreens()
        {
            Assert.IsTrue(ResponseCache.Cacheable("/v1/home"));
            Assert.IsTrue(ResponseCache.Cacheable("/v1/runs"));
            Assert.IsTrue(ResponseCache.Cacheable("/v1/runs?month=2026-11"));
            Assert.IsFalse(ResponseCache.Cacheable("/v1/runs?month=2026-11&cursor=abc"), "다음 쪽은 안 넣음");
            Assert.IsFalse(ResponseCache.Cacheable("/v1/feed?cursor=abc"));
            Assert.IsFalse(ResponseCache.Cacheable("/v1/runs/01ABC"), "상세는 안 넣음");
            Assert.IsFalse(ResponseCache.Cacheable("/v1/agreements"));
        }

        [Test]
        public void ExpiresAfterSevenDaysAndOnNewAppVersion()
        {
            var c = Cache("1.0");
            c.Put("/v1/home", "본문");
            Assert.AreEqual("본문", c.Get("/v1/home"));
            Assert.IsNull(Cache("1.1").Get("/v1/home"), "앱 버전이 바뀌면 안 씀(응답 모양이 다를 수 있음)");
            now = now.AddDays(ResponseCache.MaxAgeDays).AddMinutes(1);
            Assert.IsNull(c.Get("/v1/home"), "7일 지나면 안 씀");
            c.Put("/v1/agreements", "x");
            Assert.IsNull(c.Get("/v1/agreements"));
            c.Clear();
            Assert.IsFalse(Directory.Exists(dir));
        }

        [Test]
        public void BrokenFileIsLikeNoCache()
        {
            var c = Cache("1.0");
            c.Put("/v1/home", "본문");
            foreach (var f in Directory.GetFiles(dir)) File.WriteAllText(f, "{깨짐");
            Assert.IsNull(c.Get("/v1/home"));
        }

        // ---------- ApiClient와 함께 ----------
        [Test]
        public void SuccessfulGetIsKeptAndPeekReadsIt()
        {
            Assert.IsNull(new UsersApi(client).CachedMe(), "처음엔 없음");
            Done(new UsersApi(client).MeAsync());
            Assert.AreEqual("민트", new UsersApi(client).CachedMe().Nickname);
            Done(new RunsApi(client).ListAsync(cursor: "next"));
            Assert.IsNull(new RunsApi(client).CachedList(), "다음 쪽 응답은 첫 쪽 캐시가 아님");
        }

        [Test]
        public void FailedGetIsNotKept()
        {
            http.Handler = r => new HttpResponse { Status = 500, Body = "x" };
            Assert.IsTrue(new UsersApi(client).MeAsync().IsFaulted);
            Assert.IsNull(new UsersApi(client).CachedMe());
        }

        [Test]
        public void WritesClearTheCacheButRunSamplesDoNot()
        {
            Done(new UsersApi(client).MeAsync());
            Done(client.PostAsync<object>("/v1/runs/r1/samples", new { }, AuthMode.User));
            Assert.IsNotNull(new UsersApi(client).CachedMe(), "달리는 중 시계열은 화면 값을 안 바꿈");
            http.Handler = r => new HttpResponse { Status = 409, Body = "x" };
            Assert.IsTrue(client.PostAsync<object>("/v1/runs/r1/finish", new { }, AuthMode.User).IsFaulted);
            Assert.IsNotNull(new UsersApi(client).CachedMe(), "실패한 쓰기는 아무것도 안 바꿈");
            http.Handler = r => FakeHttp.Ok("{}");
            Done(client.PostAsync<object>("/v1/runs/r1/finish", new { }, AuthMode.User));
            Assert.IsNull(new UsersApi(client).CachedMe(), "러닝 종료 뒤엔 지난 값을 안 보임");
        }

        [Test]
        public void LoginChangeClearsTheCache()
        {
            Done(new UsersApi(client).MeAsync());
            client.Session.Clear(); // 로그아웃·계정 삭제
            Assert.IsNull(new UsersApi(client).CachedMe());
            client.Session.SetTokens("a2", "r2");
            http.Handler = r => FakeHttp.Ok("{\"nickname\":\"코랄\"}");
            Done(new UsersApi(client).MeAsync());
            client.Session.SetTokens("a3", "r3"); // 다른 사람으로 새 로그인
            Assert.IsNull(new UsersApi(client).CachedMe(), "다른 계정의 값이 보이지 않게");
        }

        // ---------- access 미리 재발급 ----------
        static string Jwt(long iat, long exp)
        {
            string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return B64("{\"alg\":\"HS256\"}") + "." + B64("{\"sub\":\"u\",\"iat\":" + iat + ",\"exp\":" + exp + "}") + ".sig";
        }

        [Test]
        public void RefreshesBeforeRequestWhenAccessExpiresSoon()
        {
            http.Handler = r => r.Url.EndsWith("/v1/auth/refresh")
                ? FakeHttp.Ok("{\"accessToken\":\"a2\",\"refreshToken\":\"r2\",\"expiresInS\":3600}")
                : FakeHttp.Ok("{\"nickname\":\"민트\"}");
            client.Session.SetTokens(Jwt(1000, 1100), "r1"); // 수명 100초 → 받자마자 곧 만료
            Done(new UsersApi(client).MeAsync());
            Assert.AreEqual(2, http.Sent.Count, "재발급 → 요청(401을 기다리지 않음)");
            Assert.IsTrue(http.Sent[0].Url.EndsWith("/v1/auth/refresh"));
            Assert.AreEqual("Bearer a2", http.Sent[1].Headers["Authorization"]);
        }

        [Test]
        public void NoEarlyRefreshWhenAccessIsFresh()
        {
            client.Session.SetTokens(Jwt(1000, 4600), "r1"); // 수명 1시간, 방금 받음(기기 시계와 상관없이)
            Done(new UsersApi(client).MeAsync());
            Assert.AreEqual(1, http.Sent.Count);
            Assert.IsFalse(client.AccessExpiringSoon(DateTime.UtcNow));
            Assert.IsTrue(client.AccessExpiringSoon(DateTime.UtcNow.AddMinutes(56)), "55분 뒤엔 곧 만료");
        }
    }
}
