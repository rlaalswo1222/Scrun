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

        // 로그인이 바뀔 때마다(로그인·가입 시작·가입·로그아웃) 오른다. 재발급(같은 로그인의 토큰 교체)으로는 안 바뀐다.
        // 재발급·재시도가 도는 사이 로그인이 바뀌었으면 그 결과를 버리는 데 쓴다(남의 계정으로 섞이지 않게).
        internal int Epoch { get; private set; }

        // 로그아웃 중에는 재발급이 거절돼도 Expired를 보내지 않는다(사용자가 스스로 나가는 중)
        internal bool LoggingOut;

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
                if (!string.IsNullOrEmpty(saved?.a) && !string.IsNullOrEmpty(saved.r))
                {
                    AccessToken = saved.a;
                    RefreshToken = saved.r;
                    return;
                }
            }
            catch (JsonException) { }
            store.Delete(StoreKey); // 깨졌거나 반쪽인 값은 버리고 다시 로그인
        }

        // 로그인·가입으로 새 토큰을 받음(로그인이 바뀜)
        internal void SetTokens(string access, string refresh)
        {
            Save(access, refresh);
            SignupToken = null;
            Epoch++;
        }

        // 재발급: 같은 로그인의 토큰만 바꿈
        internal void RotateTokens(string access, string refresh) => Save(access, refresh);

        void Save(string access, string refresh)
        {
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
                throw new ApiException(ApiFailure.BadResponse, "서버가 토큰을 주지 않았습니다.");
            AccessToken = access;
            RefreshToken = refresh;
            store.Set(StoreKey, JsonConvert.SerializeObject(new Saved { a = access, r = refresh }));
        }

        // 처음 온 사람: 예전 로그인이 남아 있어도 지우고 가입용 토큰만 둔다(가입 중에 예전 계정으로 API가 나가지 않게)
        internal void BeginSignup(string token)
        {
            if (string.IsNullOrEmpty(token))
                throw new ApiException(ApiFailure.BadResponse, "서버가 가입용 토큰을 주지 않았습니다.");
            Clear();
            SignupToken = token;
        }

        // 로그아웃·재로그인 필요. 저장된 토큰도 지운다.
        internal void Clear()
        {
            AccessToken = RefreshToken = SignupToken = null;
            store.Delete(StoreKey);
            Epoch++;
        }

        internal void Expire()
        {
            Clear();
            if (!LoggingOut) Expired?.Invoke();
        }

        class Saved
        {
            public string a;
            public string r;
        }
    }
}
