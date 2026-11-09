namespace Scrun.App
{
    // 서버 주소. 에디터는 로컬 서버(가짜 카카오 k_123), 빌드(아이폰)는 팀 공용 개발 서버(진짜 카카오, HTTPS).
    // 에디터에서는 메뉴 Scrun → API 서버 창에서 바꾼다(이 컴퓨터에 저장).
    public static class ApiConfig
    {
        public const string LocalServer = "http://localhost:8000";
        public const string SharedDevServer = "https://161-118-211-155.sslip.io";

#if UNITY_EDITOR
        public static string BaseUrl = LocalServer;
#else
        public static string BaseUrl = SharedDevServer;
#endif

        public const int TimeoutSeconds = 15;
    }
}
