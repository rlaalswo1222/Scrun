using System;

namespace Scrun.App
{
    // 서버 주소. 에디터는 로컬 서버(가짜 카카오 k_123), 빌드(아이폰)는 팀 공용 개발 서버(진짜 카카오, HTTPS).
    // 에디터에서는 메뉴 Scrun → API 서버 창에서 바꾼다(이 컴퓨터에 저장).
    public static class ApiConfig
    {
        // localhost가 아니라 127.0.0.1: 컴퓨터 이름이 한글이면 Mono가 localhost를 풀다가 "Illegal byte sequence"로 죽어
        // 에디터의 실시간 연결(ClientWebSocket)이 안 붙는다(HTTP는 다른 경로라 괜찮음). 실시간 주소는 서버가 이 주소의 호스트로 만든다
        public const string LocalServer = "http://127.0.0.1:8000";
        public const string SharedDevServer = "https://161-118-211-155.sslip.io";

#if UNITY_EDITOR
        public static string BaseUrl = LocalServer;
#else
        public static string BaseUrl = SharedDevServer;
#endif

        public const int TimeoutSeconds = 15;

        // 주소 정리: 앞뒤 공백·끝 '/' 빼고, 비면 로컬 서버, 호스트 localhost는 127.0.0.1로(위 이유. 예전에 저장한 주소도)
        public static string Normalize(string url)
        {
            url = (url ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) return LocalServer;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                url = new UriBuilder(uri) { Host = "127.0.0.1" }.Uri.ToString().TrimEnd('/');
            return url;
        }
    }
}
