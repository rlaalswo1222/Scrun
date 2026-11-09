using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // ApiClient 규칙(docs/api/README.md "로그인"·"응답 껍데기"). 서버 대신 가짜 HTTP로 응답을 정해 준다.
    public class ApiClientTests
    {
        // 서버가 줄 응답을 경로별로 정하는 가짜. 응답을 바로 주거나(Reply), 테스트가 나중에 풀게(Hold) 둘 수 있다.
        sealed class FakeHttp : IHttpTransport
        {
            public readonly List<HttpRequest> Sent = new List<HttpRequest>();
            public Func<HttpRequest, Task<HttpResponse>> Handler;
            public Task<HttpResponse> SendAsync(HttpRequest r) { Sent.Add(r); return Handler(r); }
            public int Count(string path) => Sent.Count(r => r.Url.EndsWith(path));
        }

        sealed class Me { public string Nickname { get; set; } }

        const string Base = "http://test";
        SynchronizationContext savedContext;
        FakeHttp http;
        MemorySecureStore store;
        ApiClient client;

        [SetUp]
        public void SetUp()
        {
            // 테스트 안에서 await 뒤가 바로(같은 줄에서) 이어지게 Unity 동기화 문맥을 잠시 뺀다
            savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            http = new FakeHttp();
            store = new MemorySecureStore();
            client = new ApiClient(http, new Session(store), () => Base);
        }

        [TearDown]
        public void TearDown() => SynchronizationContext.SetSynchronizationContext(savedContext);

        static Task<HttpResponse> Reply(int status, string body) =>
            Task.FromResult(new HttpResponse { Status = status, Body = body });

        static string Ok(string dataJson) =>
            "{\"resultType\":\"SUCCESS\",\"success\":{\"data\":" + dataJson + "},\"error\":null,\"meta\":{\"requestId\":\"req_ok\"}}";

        static string Fail(string code, string message = "실패") =>
            "{\"resultType\":\"FAIL\",\"success\":null,\"error\":{\"code\":\"" + code + "\",\"message\":\"" + message +
            "\",\"details\":null},\"meta\":{\"requestId\":\"req_fail\"}}";

        static string Pair(string a, string r) => Ok("{\"accessToken\":\"" + a + "\",\"refreshToken\":\"" + r + "\",\"expiresInS\":3600}");

        static T Run<T>(Task<T> t)
        {
            Assert.IsTrue(t.IsCompleted, "가짜 응답이면 바로 끝나야 함");
            return t.GetAwaiter().GetResult();
        }

        static void Done(Task t)
        {
            Assert.IsTrue(t.IsCompleted, "가짜 응답이면 바로 끝나야 함");
            t.GetAwaiter().GetResult();
        }

        static ApiException Throws(Task t)
        {
            Assert.IsTrue(t.IsCompleted, "가짜 응답이면 바로 끝나야 함");
            var e = Assert.Throws<ApiException>(() => t.GetAwaiter().GetResult());
            return e;
        }

        string AuthHeader(int i) => http.Sent[i].Headers.TryGetValue("Authorization", out var h) ? h : null;

        [Test]
        public void 성공_응답에서_data를_꺼낸다()
        {
            http.Handler = r => Reply(200, Ok("[{\"agreementId\":\"terms\",\"title\":\"이용약관\",\"required\":true,\"version\":\"v1\",\"url\":\"u\"}]"));
            var list = Run(new AuthApi(client).AgreementsAsync());
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("terms", list[0].AgreementId);
            Assert.IsTrue(list[0].Required);
            Assert.AreEqual(Base + "/v1/agreements", http.Sent[0].Url);
            Assert.IsNull(AuthHeader(0), "약관은 토큰 없이");
        }

        [Test]
        public void 실패_응답은_코드_상태_요청번호를_가진_ApiException()
        {
            client.Session.SetTokens("a1", "r1");
            http.Handler = r => Reply(409, Fail("NICKNAME_DUPLICATED", "이미 쓰는 닉네임이에요."));
            var e = Throws(client.GetAsync<Me>("/v1/users/me", AuthMode.User));
            Assert.AreEqual(ApiFailure.Server, e.Failure);
            Assert.AreEqual(409, e.Status);
            Assert.AreEqual("NICKNAME_DUPLICATED", e.Code);
            Assert.AreEqual("req_fail", e.RequestId);
            Assert.AreEqual("이미 쓰는 닉네임이에요.", e.Message);
        }

        [Test]
        public void JSON이_아닌_응답과_네트워크_끊김을_구분한다()
        {
            http.Handler = r => Reply(502, "<html>Bad Gateway</html>");
            var bad = Throws(new AuthApi(client).AgreementsAsync());
            Assert.AreEqual(ApiFailure.BadResponse, bad.Failure);
            Assert.AreEqual(502, bad.Status);

            http.Handler = r => Task.FromResult(new HttpResponse { Status = 0, NetworkError = "Cannot connect" });
            var net = Throws(new AuthApi(client).AgreementsAsync());
            Assert.AreEqual(ApiFailure.Network, net.Failure);
        }

        [Test]
        public void 카카오_로그인_처음이면_가입용_토큰만_기존이면_토큰을_저장한다()
        {
            http.Handler = r => Reply(200, Ok("{\"isNewUser\":true,\"signupToken\":\"s1\"}"));
            var first = Run(new AuthApi(client).KakaoLoginAsync("k_123"));
            Assert.IsTrue(first.IsNewUser);
            Assert.IsTrue(client.Session.IsSigningUp);
            Assert.IsFalse(client.Session.IsLoggedIn);
            StringAssert.Contains("\"kakaoAccessToken\":\"k_123\"", http.Sent[0].Body);

            http.Handler = r => Reply(200, Ok("{\"isNewUser\":false,\"accessToken\":\"a1\",\"refreshToken\":\"r1\",\"expiresInS\":3600}"));
            Run(new AuthApi(client).KakaoLoginAsync("k_123"));
            Assert.IsTrue(client.Session.IsLoggedIn);

            var reopened = new Session(store); // 앱을 다시 켠 것
            Assert.AreEqual("a1", reopened.AccessToken);
            Assert.AreEqual("r1", reopened.RefreshToken);
            Assert.IsNull(reopened.SignupToken, "가입용 토큰은 저장 안 함");
        }

        [Test]
        public void 가입은_가입용_토큰을_붙이고_끝나면_로그인된다()
        {
            client.Session.BeginSignup("s1");
            http.Handler = r => Reply(201, Pair("a1", "r1"));
            Done(new AuthApi(client).SignupAsync(new SignupRequest
            {
                AgreedIds = { "terms", "privacy" }, Nickname = "민재", HeightCm = 175, WeightKg = 70,
            }));
            Assert.AreEqual("Bearer s1", AuthHeader(0));
            StringAssert.Contains("\"agreedIds\":[\"terms\",\"privacy\"]", http.Sent[0].Body);
            StringAssert.Contains("\"heightCm\":175", http.Sent[0].Body);
            Assert.IsTrue(client.Session.IsLoggedIn);
            Assert.IsNull(client.Session.SignupToken);
        }

        [Test]
        public void 닉네임_확인은_가입_중엔_가입용_토큰_로그인_뒤엔_access를_쓰고_한글을_주소에_맞게_바꾼다()
        {
            http.Handler = r => Reply(200, Ok("{\"nickname\":\"AB\",\"available\":true,\"reason\":null}"));
            client.Session.BeginSignup("s1");
            var n = Run(new UsersApi(client).CheckNicknameAsync("ＡＢ 민재"));
            Assert.AreEqual("AB", n.Nickname);
            Assert.AreEqual("Bearer s1", AuthHeader(0));
            StringAssert.Contains("nickname=" + Uri.EscapeDataString("ＡＢ 민재"), http.Sent[0].Url);

            client.Session.SetTokens("a1", "r1");
            Run(new UsersApi(client).CheckNicknameAsync("AB"));
            Assert.AreEqual("Bearer a1", AuthHeader(1));
        }

        [Test]
        public void 로그인이_필요한데_토큰이_없으면_서버에_보내지_않는다()
        {
            http.Handler = r => Reply(200, Ok("{}"));
            var e = Throws(client.GetAsync<Me>("/v1/users/me", AuthMode.User));
            Assert.AreEqual(ApiFailure.NotLoggedIn, e.Failure);
            Assert.AreEqual(0, http.Sent.Count);
        }

        [Test]
        public void access가_만료되면_재발급하고_원래_요청을_한_번_다시_보낸다()
        {
            client.Session.SetTokens("a1", "r1");
            http.Handler = r =>
                r.Url.EndsWith("/v1/auth/refresh") ? Reply(200, Pair("a2", "r2"))
                : r.Headers["Authorization"] == "Bearer a1" ? Reply(401, Fail("TOKEN_EXPIRED"))
                : Reply(200, Ok("{\"nickname\":\"민재\"}"));

            var me = Run(client.GetAsync<Me>("/v1/users/me", AuthMode.User));
            Assert.AreEqual("민재", me.Nickname);
            Assert.AreEqual(1, http.Count("/v1/auth/refresh"));
            StringAssert.Contains("\"refreshToken\":\"r1\"", http.Sent[1].Body);
            Assert.AreEqual("Bearer a2", AuthHeader(2));
            Assert.AreEqual("r2", new Session(store).RefreshToken, "새 토큰을 바로 저장");
        }

        [Test]
        public void 동시에_여러_요청이_만료돼도_재발급은_한_번만()
        {
            client.Session.SetTokens("a1", "r1");
            var first = new List<TaskCompletionSource<HttpResponse>>();
            var refresh = new TaskCompletionSource<HttpResponse>();
            http.Handler = r =>
            {
                if (r.Url.EndsWith("/v1/auth/refresh")) return refresh.Task;
                if (r.Headers["Authorization"] == "Bearer a1")
                {
                    var t = new TaskCompletionSource<HttpResponse>();
                    first.Add(t);
                    return t.Task;
                }
                return Reply(200, Ok("{\"nickname\":\"민재\"}"));
            };

            var calls = Enumerable.Range(0, 3).Select(_ => client.GetAsync<Me>("/v1/users/me", AuthMode.User)).ToArray();
            Assert.AreEqual(3, first.Count);
            foreach (var t in first) t.SetResult(new HttpResponse { Status = 401, Body = Fail("TOKEN_EXPIRED") });
            Assert.AreEqual(1, http.Count("/v1/auth/refresh"), "재발급 요청은 하나");
            Assert.IsFalse(calls.Any(c => c.IsCompleted), "재발급을 기다리는 중");

            refresh.SetResult(new HttpResponse { Status = 200, Body = Pair("a2", "r2") });
            foreach (var c in calls) Assert.AreEqual("민재", Run(c).Nickname);
            Assert.AreEqual(1, http.Count("/v1/auth/refresh"));
            Assert.AreEqual(3, http.Sent.Count(r => r.Headers.TryGetValue("Authorization", out var h) && h == "Bearer a2"));
        }

        [Test]
        public void 재발급이_거절되면_로그인을_지우고_Expired를_알린다()
        {
            client.Session.SetTokens("a1", "r1");
            int expired = 0;
            client.Session.Expired += () => expired++;
            http.Handler = r => r.Url.EndsWith("/v1/auth/refresh") ? Reply(401, Fail("UNAUTHORIZED"))
                                                                    : Reply(401, Fail("TOKEN_EXPIRED"));
            var e = Throws(client.GetAsync<Me>("/v1/users/me", AuthMode.User));
            Assert.AreEqual(ApiFailure.NotLoggedIn, e.Failure);
            Assert.AreEqual(1, expired);
            Assert.IsFalse(client.Session.IsLoggedIn);
            Assert.IsNull(new Session(store).RefreshToken, "저장된 토큰도 지움");
        }

        [Test]
        public void 재발급_중_네트워크가_끊기면_토큰을_지우지_않는다()
        {
            client.Session.SetTokens("a1", "r1");
            http.Handler = r => r.Url.EndsWith("/v1/auth/refresh")
                ? Task.FromResult(new HttpResponse { Status = 0, NetworkError = "timeout" })
                : Reply(401, Fail("TOKEN_EXPIRED"));
            var e = Throws(client.GetAsync<Me>("/v1/users/me", AuthMode.User));
            Assert.AreEqual(ApiFailure.Network, e.Failure);
            Assert.AreEqual("r1", client.Session.RefreshToken, "다음에 같은 refresh 토큰으로 다시 보냄(30초 유예)");
        }

        [Test]
        public void 로그아웃은_서버에_못_알려도_이_기기_토큰을_지운다()
        {
            client.Session.SetTokens("a1", "r1");
            http.Handler = r => Task.FromResult(new HttpResponse { Status = 0, NetworkError = "offline" });
            Done(new AuthApi(client).LogoutAsync());
            Assert.IsFalse(client.Session.IsLoggedIn);
            Assert.IsNull(new Session(store).RefreshToken);
        }

        [Test]
        public void 카카오_토큰_거절은_로그인_필요가_아니라_서버_실패()
        {
            http.Handler = r => Reply(401, Fail("INVALID_KAKAO_TOKEN"));
            var e = Throws(new AuthApi(client).KakaoLoginAsync("bad"));
            Assert.AreEqual(ApiFailure.Server, e.Failure);
            Assert.AreEqual("INVALID_KAKAO_TOKEN", e.Code);
        }
        [Test]
        public void 재발급_중에_로그아웃하면_옛_토큰을_되살리지_않는다()
        {
            client.Session.SetTokens("a1", "r1");
            var refresh = new TaskCompletionSource<HttpResponse>();
            http.Handler = r => r.Url.EndsWith("/v1/auth/refresh") ? refresh.Task : Reply(401, Fail("TOKEN_EXPIRED"));
            var call = client.GetAsync<Me>("/v1/users/me", AuthMode.User);
            client.Session.Clear(); // 그사이 사용자가 로그아웃
            refresh.SetResult(new HttpResponse { Status = 200, Body = Pair("a2", "r2") });
            Assert.AreEqual(ApiFailure.NotLoggedIn, Throws(call).Failure);
            Assert.IsFalse(client.Session.IsLoggedIn);
            Assert.IsNull(new Session(store).RefreshToken, "저장소에도 되살아나지 않음");
        }

        [Test]
        public void 다른_카카오_계정이_새로_오면_예전_로그인을_지우고_가입용_토큰을_쓴다()
        {
            client.Session.SetTokens("a1", "r1");
            http.Handler = r => r.Url.Contains("/v1/auth/kakao")
                ? Reply(200, Ok("{\"isNewUser\":true,\"signupToken\":\"s2\"}"))
                : Reply(200, Ok("{\"nickname\":\"B\",\"available\":true}"));
            Run(new AuthApi(client).KakaoLoginAsync("k_999"));
            Assert.IsTrue(client.Session.IsSigningUp);
            Assert.IsNull(client.Session.AccessToken);
            Assert.IsNull(new Session(store).RefreshToken);
            Run(new UsersApi(client).CheckNicknameAsync("B"));
            Assert.AreEqual("Bearer s2", AuthHeader(1));
        }

        [Test]
        public void 우리_껍데기가_아닌_JSON_오류나_빈_재발급_응답도_ApiException()
        {
            http.Handler = r => Reply(502, "{\"error\":\"Bad Gateway\"}");
            Assert.AreEqual(ApiFailure.BadResponse, Throws(new AuthApi(client).AgreementsAsync()).Failure);

            client.Session.SetTokens("a1", "r1");
            http.Handler = r => r.Url.EndsWith("/v1/auth/refresh") ? Reply(200, "") : Reply(401, Fail("TOKEN_EXPIRED"));
            Assert.AreEqual(ApiFailure.BadResponse, Throws(client.GetAsync<Me>("/v1/users/me", AuthMode.User)).Failure);
        }

        [Test]
        public void 반쪽만_저장된_로그인은_로그인으로_보지_않는다()
        {
            store.Set("session.v1", "{\"a\":null,\"r\":\"r1\"}");
            var s = new Session(store);
            Assert.IsFalse(s.IsLoggedIn);
            Assert.IsNull(store.Get("session.v1"), "버림");
        }

        [Test]
        public void 로그아웃_중_재발급이_거절돼도_Expired를_보내지_않는다()
        {
            client.Session.SetTokens("a1", "r1");
            int expired = 0;
            client.Session.Expired += () => expired++;
            http.Handler = r => r.Url.EndsWith("/v1/auth/refresh") ? Reply(401, Fail("UNAUTHORIZED")) : Reply(401, Fail("TOKEN_EXPIRED"));
            Done(new AuthApi(client).LogoutAsync());
            Assert.AreEqual(0, expired);
            Assert.IsFalse(client.Session.IsLoggedIn);
        }
    }
}

