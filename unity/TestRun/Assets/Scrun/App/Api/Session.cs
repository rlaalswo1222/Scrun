using System;
using Newtonsoft.Json;

namespace Scrun.App
{
    // 이 기기의 로그인 상태. access·refresh 토큰은 ISecureStore(아이폰 키체인)에만 저장하고 로그에 찍지 않는다.
    // 가입용 토큰(30분)은 저장하지 않는다(앱을 껐다 켜면 카카오 로그인부터 다시).
    public sealed class Session
    {
        const string StoreKey = "session.v1";

        readonly ISecureStore store;

        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }
        public string SignupToken { get; private set; }

        public bool IsLoggedIn => RefreshToken != null;
        public bool IsSigningUp => SignupToken != null && !IsLoggedIn;

        // 재발급이 거절돼 다시 로그인해야 할 때(다른 기기에서 탈취 의심, refresh 만료 등). 화면은 로그인으로 보낸다.
        public event Action Expired;

        public Session(ISecureStore store)
        {
            this.store = store;
            var json = store.Get(StoreKey);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var saved = JsonConvert.DeserializeObject<Saved>(json);
                if (!string.IsNullOrEmpty(saved?.r))
                {
                    AccessToken = saved.a;
                    RefreshToken = saved.r;
                }
            }
            catch (JsonException)
            {
                store.Delete(StoreKey); // 깨진 값은 버리고 다시 로그인
            }
        }

        internal void SetTokens(string access, string refresh)
        {
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
                throw new ApiException(ApiFailure.BadResponse, "서버가 토큰을 주지 않았습니다.");
            AccessToken = access;
            RefreshToken = refresh;
            SignupToken = null;
            store.Set(StoreKey, JsonConvert.SerializeObject(new Saved { a = access, r = refresh }));
        }

        internal void SetSignupToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                throw new ApiException(ApiFailure.BadResponse, "서버가 가입용 토큰을 주지 않았습니다.");
            SignupToken = token;
        }

        // 로그아웃·재로그인 필요. 저장된 토큰도 지운다.
        internal void Clear()
        {
            AccessToken = RefreshToken = SignupToken = null;
            store.Delete(StoreKey);
        }

        internal void Expire()
        {
            Clear();
            Expired?.Invoke();
        }

        class Saved
        {
            public string a;
            public string r;
        }
    }
}
