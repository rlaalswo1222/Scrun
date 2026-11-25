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

        // 진행 중인 재발급과 그것이 속한 로그인(Epoch). 끝나면 null(동시에 만료된 요청들이 이 하나를 같이 기다림)
        Task<bool> refreshing;
        int refreshingEpoch;

        // 화면 첫 페이지 응답 캐시(계획 0074 A). null이면 안 씀(테스트 기본). 로그인이 바뀌면 통째로 버림
        public ResponseCache Cache { get; set; }

        public ApiClient(IHttpTransport http, Session session, Func<string> baseUrl)
        {
            this.http = http;
            Session = session;
            this.baseUrl = baseUrl;
            session.LoginChanged += () => Cache?.Clear();
        }

        // 캐시에 둔 응답(없거나 못 읽으면 default). 화면이 새로 받기 전에 먼저 그릴 때
        public T Peek<T>(string path)
        {
            var body = Cache?.Get(path);
            if (body == null) return default;
            try { return ParseEnvelope<T>(new HttpResponse { Status = 200, Body = body }); }
            catch (Exception e) when (e is ApiException || e is JsonException || e is InvalidOperationException ||
                                      e is ArgumentException || e is InvalidCastException)
            {
                return default; // 모양이 바뀐 옛 캐시 등
            }
        }

        // 성공한 응답 뒤: GET 첫 페이지는 캐시에 넣고, 쓰기가 성공하면 캐시를 버림. 그사이 로그인이 바뀌었으면 아무것도 안 함
        void AfterSuccess(string method, string path, AuthMode auth, HttpResponse res, int epoch)
        {
            if (Cache == null || Session.Epoch != epoch) return;
            if (method == "GET")
            {
                if (auth == AuthMode.User && ResponseCache.Cacheable(path)) Cache.Put(path, res.Body);
            }
            else if (ResponseCache.InvalidatedBy(method, path)) Cache.Clear();
        }

        // ---- access 만료 전에 미리 재발급(계획 0074 D) ----
        // 401을 받고 재발급하면 왕복이 셋(요청 → 401 → 재발급 → 다시). 곧 만료면 요청 전에 재발급해 둘로, 앱으로 돌아올 때 해 두면 하나로
        public const int RefreshAheadSeconds = 300;
        const double ProactiveRetrySeconds = 30; // 미리 재발급이 망 오류로 실패하면 이만큼은 다시 안 함(요청마다 늦어지지 않게)
        string jwtToken;
        long jwtIat, jwtExp;
        DateTime proactiveFailedUtc = DateTime.MinValue;

        // 지금 access가 RefreshAheadSeconds 안에 만료되나. 이 실행에서 받은 토큰은 받은 때 + 수명(exp − iat)으로 봐 기기 시계가
        // 틀려도 맞음. 저장소에서 읽은 토큰은 exp를 기기 시계로(틀리면 한 번 더 재발급하는 정도)
        internal bool AccessExpiringSoon(DateTime utcNow)
        {
            var token = Session.AccessToken;
            if (token == null || Session.RefreshToken == null || !ReadJwtTimes(token)) return false;
            DateTime expires = Session.AccessReceivedUtc is DateTime got
                ? got.AddSeconds(jwtExp - jwtIat)
                : DateTimeOffset.FromUnixTimeSeconds(jwtExp).UtcDateTime;
            return (expires - utcNow).TotalSeconds < RefreshAheadSeconds;
        }

        public async Task RefreshIfExpiringSoonAsync()
        {
            var now = DateTime.UtcNow;
            if ((now - proactiveFailedUtc).TotalSeconds < ProactiveRetrySeconds || !AccessExpiringSoon(now)) return;
            try { await RefreshAsync(Session.AccessToken); }
            catch (ApiException e) when (e.Failure != ApiFailure.NotLoggedIn)
            {
                proactiveFailedUtc = now; // 망 오류: 지금 토큰으로 그대로(만료됐으면 401 → 재발급 길이 처리)
            }
        }

        bool ReadJwtTimes(string token)
        {
            if (token == jwtToken) return true;
            try
            {
                var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
                part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
                var claims = JObject.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(part)));
                jwtIat = (long)claims["iat"];
                jwtExp = (long)claims["exp"];
                jwtToken = token;
                return true;
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is FormatException || e is JsonException ||
                                      e is ArgumentException || e is InvalidCastException || e is NullReferenceException)
            {
                return false; // JWT가 아니면(테스트의 가짜 토큰 등) 미리 재발급 안 함
            }
        }

        public Task<T> GetAsync<T>(string path, AuthMode auth) => SendAsync<T>("GET", path, null, auth);
        public Task<T> PostAsync<T>(string path, object body, AuthMode auth) => SendAsync<T>("POST", path, body, auth);

        public Task<T> SendAsync<T>(string method, string path, object body, AuthMode auth) =>
            SendCore<T>(method, path, body == null ? null : JsonConvert.SerializeObject(body, JsonSettings), null, auth);

        // multipart/form-data(계기판 사진, 계획 0038). 재발급·오류 처리는 JSON 요청과 같음
        public Task<T> PostMultipartAsync<T>(string path, MultipartBody body, AuthMode auth, int timeoutSeconds) =>
            SendCore<T>("POST", path, null, body, auth, timeoutSeconds);

        async Task<T> SendCore<T>(string method, string path, string json, MultipartBody raw, AuthMode auth,
                                  int timeoutSeconds = ApiConfig.TimeoutSeconds)
        {
            if (auth == AuthMode.User) await RefreshIfExpiringSoonAsync();
            int epoch = Session.Epoch;
            string usedToken = TokenFor(auth);
            var res = await SendOnce(method, path, json, raw, usedToken, timeoutSeconds);

            // access 만료 → (아직 같은 로그인이면) 재발급 한 번 → 원래 요청 한 번 다시
            bool sentAccess = usedToken != null && auth != AuthMode.Signup && usedToken != Session.SignupToken;
            if (res.Status == 401 && sentAccess && Session.Epoch == epoch && ErrorCode(res) == "TOKEN_EXPIRED")
            {
                await RefreshAsync(usedToken);
                if (Session.Epoch != epoch) throw NotLoggedIn("로그인이 바뀌었습니다. 다시 시도하세요.");
                res = await SendOnce(method, path, json, raw, TokenFor(auth), timeoutSeconds);
            }
            var result = Parse<T>(res); // 실패면 여기서 예외(캐시는 건드리지 않음)
            AfterSuccess(method, path, auth, res, epoch);
            return result;
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

        Task<HttpResponse> SendOnce(string method, string path, string json, MultipartBody raw, string token, int timeoutSeconds)
        {
            var req = new HttpRequest { Method = method, Url = baseUrl().TrimEnd('/') + path, Body = json,
                                        RawBody = raw?.Bytes, ContentType = raw?.ContentType, TimeoutSeconds = timeoutSeconds };
            if (token != null) req.Headers["Authorization"] = "Bearer " + token;
            return http.SendAsync(req);
        }

        // ---- 재발급 (한 번에 하나만) ----

        Task RefreshAsync(string expiredAccess)
        {
            if (Session.AccessToken != expiredAccess && Session.AccessToken != null)
                return Task.CompletedTask; // 그사이 다른 요청이 이미 재발급함
            if (refreshing != null && refreshingEpoch == Session.Epoch) return Unwrap(refreshing);

            var t = DoRefresh();
            if (!t.IsCompleted)
            {
                refreshing = t;
                refreshingEpoch = Session.Epoch;
                _ = ClearWhenDone(t);
            }
            return Unwrap(t);
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

        // true = 새 토큰 받음, false = 거절됐거나 그사이 로그인이 바뀜.
        // 네트워크 실패는 예외(토큰은 그대로 → 서버가 30초 안에 같은 토큰을 다시 받아 줌)
        async Task<bool> DoRefresh()
        {
            var refresh = Session.RefreshToken;
            int epoch = Session.Epoch;
            if (refresh == null) return false;
            var body = JsonConvert.SerializeObject(new { refreshToken = refresh }, JsonSettings);
            var res = await SendOnce("POST", "/v1/auth/refresh", body, null, null, ApiConfig.TimeoutSeconds);

            if (Session.Epoch != epoch) return false; // 그사이 로그아웃·다른 로그인 → 이 결과는 버림(되살리지 않음)
            if (res.NetworkError == null && (res.Status == 401 || res.Status == 403))
            {
                Session.Expire();
                return false;
            }
            var pair = Parse<TokenPair>(res) ?? throw new ApiException(ApiFailure.BadResponse, "재발급 응답이 비었습니다.", res.Status);
            Session.RotateTokens(pair.AccessToken, pair.RefreshToken);
            return true;
        }

        // ---- 응답 풀기 ----

        static string ErrorCode(HttpResponse res)
        {
            try { return (string)JObject.Parse(res.Body ?? "")["error"]?["code"]; }
            catch (Exception) { return null; } // JSON이 아니거나 모양이 다름
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
            try
            {
                return ParseEnvelope<T>(res);
            }
            catch (ApiException) { throw; }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is ArgumentException || e is InvalidCastException)
            {
                // JSON이 아니거나 우리 껍데기 모양이 아님(서버 앞단의 502 HTML, {"error":"Bad Gateway"} 등)
                throw new ApiException(ApiFailure.BadResponse, $"서버 응답을 읽지 못했습니다(HTTP {res.Status}).", res.Status);
            }
        }

        static T ParseEnvelope<T>(HttpResponse res)
        {
            var root = JObject.Parse(res.Body);
            var requestId = (string)root["meta"]?["requestId"];
            if ((string)root["resultType"] == "SUCCESS" && res.Status >= 200 && res.Status < 300)
            {
                var data = root["success"]?["data"];
                return data == null || data.Type == JTokenType.Null ? default : data.ToObject<T>(Json);
            }

            var err = root["error"] as JObject
                      ?? throw new ApiException(ApiFailure.BadResponse, $"서버 응답을 읽지 못했습니다(HTTP {res.Status}).", res.Status, null, requestId);
            var code = (string)err["code"];
            var message = (string)err["message"] ?? $"요청이 실패했습니다(HTTP {res.Status}).";
            var details = err["details"];
            // 로그인 자체가 없거나 끝난 경우만 NotLoggedIn(카카오 토큰 거절 INVALID_KAKAO_TOKEN도 401이지만 그건 Server)
            var failure = res.Status == 401 && (code == "UNAUTHORIZED" || code == "TOKEN_EXPIRED")
                ? ApiFailure.NotLoggedIn : ApiFailure.Server;
            throw new ApiException(failure, message, res.Status, code, requestId,
                                   details == null || details.Type == JTokenType.Null ? null : details.ToString(Formatting.None));
        }
    }
}
