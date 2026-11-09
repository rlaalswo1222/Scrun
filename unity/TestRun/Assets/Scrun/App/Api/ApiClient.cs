using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Scrun.App
{
    // 이 API에 어떤 토큰을 붙이나(docs/api/README.md "로그인")
    public enum AuthMode
    {
        None,          // 카카오 로그인·약관·재발급
        Signup,        // 가입
        SignupOrUser,  // 닉네임 확인(가입 중이면 가입용 토큰, 로그인했으면 access)
        User,          // 나머지 전부
    }

    // 서버를 부르는 한 곳. 응답 껍데기(resultType·success·error·meta)를 풀고, 실패는 ApiException 하나로,
    // access 만료(401 TOKEN_EXPIRED)면 재발급을 한 번만 보내고(동시에 여러 요청이 만료돼도) 원래 요청을 한 번 다시 보낸다.
    // 화면 코드는 보통 이 클래스 대신 Api.Auth·Api.Users를 쓴다.
    public sealed class ApiClient
    {
        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };
        static readonly JsonSerializer Json = JsonSerializer.Create(JsonSettings);

        readonly IHttpTransport http;
        readonly Func<string> baseUrl;
        public Session Session { get; }

        // 진행 중인 재발급. 끝나면 null(동시에 만료된 요청들이 이 하나를 같이 기다림)
        Task<bool> refreshing;

        public ApiClient(IHttpTransport http, Session session, Func<string> baseUrl)
        {
            this.http = http;
            Session = session;
            this.baseUrl = baseUrl;
        }

        public Task<T> GetAsync<T>(string path, AuthMode auth) => SendAsync<T>("GET", path, null, auth);
        public Task<T> PostAsync<T>(string path, object body, AuthMode auth) => SendAsync<T>("POST", path, body, auth);

        public async Task<T> SendAsync<T>(string method, string path, object body, AuthMode auth)
        {
            string json = body == null ? null : JsonConvert.SerializeObject(body, JsonSettings);
            string usedToken = TokenFor(auth);
            var res = await SendOnce(method, path, json, usedToken);

            // access 만료 → 재발급 한 번 → 원래 요청 한 번 다시
            if (res.Status == 401 && usedToken != null && usedToken == Session.AccessToken && ErrorCode(res) == "TOKEN_EXPIRED")
            {
                await RefreshAsync(usedToken);
                res = await SendOnce(method, path, json, TokenFor(auth));
            }
            else if (res.Status == 401 && usedToken != null && usedToken != Session.AccessToken && Session.AccessToken != null
                     && ErrorCode(res) == "TOKEN_EXPIRED")
            {
                // 이 요청이 나가는 사이 다른 요청이 이미 재발급함 → 새 토큰으로 한 번 다시
                res = await SendOnce(method, path, json, TokenFor(auth));
            }
            return Parse<T>(res);
        }

        string TokenFor(AuthMode auth)
        {
            switch (auth)
            {
                case AuthMode.None: return null;
                case AuthMode.Signup:
                    return Session.SignupToken ?? throw NotLoggedIn("가입용 토큰이 없습니다. 카카오 로그인부터 다시 하세요.");
                case AuthMode.SignupOrUser:
                    return Session.AccessToken ?? Session.SignupToken ?? throw NotLoggedIn("로그인이 필요합니다.");
                default:
                    return Session.AccessToken ?? throw NotLoggedIn("로그인이 필요합니다.");
            }
        }

        static ApiException NotLoggedIn(string message) => new ApiException(ApiFailure.NotLoggedIn, message, 401, "UNAUTHORIZED");

        Task<HttpResponse> SendOnce(string method, string path, string json, string token)
        {
            var req = new HttpRequest { Method = method, Url = baseUrl().TrimEnd('/') + path, Body = json };
            if (token != null) req.Headers["Authorization"] = "Bearer " + token;
            return http.SendAsync(req);
        }

        // ---- 재발급 (한 번에 하나만) ----

        Task RefreshAsync(string expiredAccess)
        {
            if (Session.AccessToken != expiredAccess && Session.AccessToken != null)
                return Task.CompletedTask; // 그사이 다른 요청이 이미 재발급함
            if (refreshing == null)
            {
                var t = DoRefresh();
                if (!t.IsCompleted)
                {
                    refreshing = t;
                    _ = ClearWhenDone(t);
                }
                return Unwrap(t);
            }
            return Unwrap(refreshing);
        }

        async Task ClearWhenDone(Task<bool> t)
        {
            try { await t; } catch { /* 결과는 기다리는 쪽이 받음 */ }
            if (refreshing == t) refreshing = null;
        }

        static async Task Unwrap(Task<bool> t)
        {
            if (!await t) throw NotLoggedIn("다시 로그인해 주세요.");
        }

        // true = 새 토큰 받음, false = 거절(로그인 끝남). 네트워크 실패는 예외(토큰은 그대로 → 30초 안에 같은 토큰으로 다시 보내도 됨)
        async Task<bool> DoRefresh()
        {
            var refresh = Session.RefreshToken;
            if (refresh == null) return false;
            var body = JsonConvert.SerializeObject(new { refreshToken = refresh }, JsonSettings);
            var res = await SendOnce("POST", "/v1/auth/refresh", body, null);
            if (res.NetworkError == null && (res.Status == 401 || res.Status == 403))
            {
                if (Session.RefreshToken == refresh) Session.Expire(); // 그사이 다른 로그인이 들어오지 않았으면
                return false;
            }
            var pair = Parse<TokenPair>(res);
            Session.SetTokens(pair.AccessToken, pair.RefreshToken);
            return true;
        }

        // ---- 응답 풀기 ----

        static string ErrorCode(HttpResponse res)
        {
            try { return (string)JObject.Parse(res.Body ?? "")["error"]?["code"]; }
            catch (JsonException) { return null; }
        }

        static T Parse<T>(HttpResponse res)
        {
            if (res.NetworkError != null)
                throw new ApiException(ApiFailure.Network, "인터넷 연결을 확인해 주세요. (" + res.NetworkError + ")");
            if (res.Status == 204 || string.IsNullOrWhiteSpace(res.Body))
            {
                if (res.Status >= 200 && res.Status < 300) return default;
                throw new ApiException(ApiFailure.BadResponse, $"서버 응답이 비었습니다(HTTP {res.Status}).", res.Status);
            }

            JObject root;
            try { root = JObject.Parse(res.Body); }
            catch (JsonException)
            {
                throw new ApiException(ApiFailure.BadResponse, $"서버 응답을 읽지 못했습니다(HTTP {res.Status}).", res.Status);
            }

            var requestId = (string)root["meta"]?["requestId"];
            if ((string)root["resultType"] == "SUCCESS" && res.Status >= 200 && res.Status < 300)
            {
                var data = root["success"]?["data"];
                try { return data == null || data.Type == JTokenType.Null ? default : data.ToObject<T>(Json); }
                catch (JsonException e)
                {
                    throw new ApiException(ApiFailure.BadResponse, "서버 응답 모양이 앱과 다릅니다: " + e.Message, res.Status, null, requestId);
                }
            }

            var err = root["error"];
            var code = (string)err?["code"];
            var message = (string)err?["message"] ?? $"요청이 실패했습니다(HTTP {res.Status}).";
            var details = err?["details"];
            // 로그인 자체가 없거나 끝난 경우만 NotLoggedIn(카카오 토큰 거절 INVALID_KAKAO_TOKEN도 401이지만 그건 Server)
            var failure = res.Status == 401 && (code == "UNAUTHORIZED" || code == "TOKEN_EXPIRED")
                ? ApiFailure.NotLoggedIn : ApiFailure.Server;
            throw new ApiException(failure, message, res.Status, code, requestId,
                                   details == null || details.Type == JTokenType.Null ? null : details.ToString(Formatting.None));
        }
    }
}
