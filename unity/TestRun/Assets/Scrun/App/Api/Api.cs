using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Scrun.App
{
    // 화면 코드가 서버를 부르는 입구.
    //   var r = await Api.Auth.KakaoLoginAsync(kakaoToken);       // 처음이면 r.IsNewUser
    //   var n = await Api.Users.CheckNicknameAsync("민재_01");     // n.Nickname = 정규화된 값
    //   Api.Session.Expired += GoToLogin;
    // 실패는 ApiException(Failure·Code·RequestId). 서버 주소는 ApiConfig.
    public static class Api
    {
        static ApiClient client;

        public static ApiClient Client => client ??= Create();
        public static Session Session => Client.Session;
        public static AuthApi Auth => new AuthApi(Client);
        public static UsersApi Users => new UsersApi(Client);

        static ApiClient Create() =>
            new ApiClient(new UnityHttpTransport(), new Session(Native.SecureStore), () => ApiConfig.BaseUrl);

        // Play마다 저장된 로그인부터 다시 읽는다(이 프로젝트는 Play 때 도메인 리로드를 꺼 둠).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => client = null;

        // 이 기기를 가리키는 값(iOS는 identifierForVendor). 같은 기기의 예전 로그인을 서버가 폐기하는 데 씀.
        internal static string DeviceId
        {
            get
            {
                var id = SystemInfo.deviceUniqueIdentifier;
                return string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier ? null
                    : id.Length > 100 ? id.Substring(0, 100) : id;
            }
        }
    }

    public sealed class AuthApi
    {
        readonly ApiClient c;
        public AuthApi(ApiClient client) => c = client;

        // 카카오 SDK 토큰 → 이미 가입했으면 로그인 끝(Session.IsLoggedIn), 처음이면 IsNewUser + 가입용 토큰 보관(Session.IsSigningUp)
        public async Task<LoginResult> KakaoLoginAsync(string kakaoAccessToken)
        {
            var r = await c.PostAsync<LoginResult>("/v1/auth/kakao",
                new { kakaoAccessToken, deviceId = Api.DeviceId }, AuthMode.None);
            if (r == null) throw new ApiException(ApiFailure.BadResponse, "로그인 응답이 비었습니다.");
            if (r.IsNewUser) c.Session.SetSignupToken(r.SignupToken);
            else c.Session.SetTokens(r.AccessToken, r.RefreshToken);
            return r;
        }

        public Task<List<Agreement>> AgreementsAsync() => c.GetAsync<List<Agreement>>("/v1/agreements", AuthMode.None);

        // 가입용 토큰으로 가입 → 로그인 끝. 같은 가입을 다시 보내도(응답 유실·두 번 누름) 서버가 같은 계정 토큰을 준다.
        public async Task SignupAsync(SignupRequest request)
        {
            request.DeviceId ??= Api.DeviceId;
            var pair = await c.PostAsync<TokenPair>("/v1/auth/signup", request, AuthMode.Signup);
            if (pair == null) throw new ApiException(ApiFailure.BadResponse, "가입 응답이 비었습니다.");
            c.Session.SetTokens(pair.AccessToken, pair.RefreshToken);
        }

        // 이 기기 로그인 끝내기. 서버에 못 알려도(네트워크 등) 이 기기의 토큰은 지운다.
        public async Task LogoutAsync()
        {
            var refresh = c.Session.RefreshToken;
            try
            {
                if (refresh != null && c.Session.AccessToken != null)
                    await c.PostAsync<object>("/v1/auth/logout", new { refreshToken = refresh }, AuthMode.User);
            }
            catch (ApiException e)
            {
                Debug.LogWarning($"[Api] 로그아웃을 서버에 알리지 못함(이 기기 토큰은 지움): {e.Failure} {e.Code}");
            }
            finally
            {
                c.Session.Clear();
            }
        }
    }

    public sealed class UsersApi
    {
        readonly ApiClient c;
        public UsersApi(ApiClient client) => c = client;

        // 가입 중(가입용 토큰)에도, 로그인 뒤(내 정보 수정)에도 된다. 로그인한 사람의 지금 닉네임은 사용 가능.
        public Task<NicknameCheck> CheckNicknameAsync(string nickname) =>
            c.GetAsync<NicknameCheck>("/v1/users/nickname/check?nickname=" + Uri.EscapeDataString(nickname ?? ""),
                                      AuthMode.SignupOrUser);
    }
}
