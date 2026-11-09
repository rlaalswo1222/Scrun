using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Scrun.App.Editor
{
    // 메뉴 Scrun → 개발 → 서버 로그인 흐름 확인: Play 중에 지금 API 서버(Scrun → API 서버)로
    // 약관 → 가짜 카카오 로그인(새 사람) → 닉네임 확인 → 가입 → 다시 로그인 → 로그아웃을 해 보고 콘솔에 남긴다.
    // 로컬 서버(KAKAO_MODE=fake)에서만 된다. 저장된 에디터 로그인은 건드리지 않는다(메모리 저장소 사용).
    public static class ApiSmokeTest
    {
        [MenuItem("Scrun/개발/서버 로그인 흐름 확인")]
        static void RunMenu()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[ApiSmoke] Play 중에 실행하세요(서버 호출이 Play 루프에서 돕니다).");
                return;
            }
            _ = RunAsync();
        }

        public static async Task<bool> RunAsync()
        {
            var session = new Session(new MemorySecureStore());
            var client = new ApiClient(new UnityHttpTransport(), session, () => ApiConfig.BaseUrl);
            var auth = new AuthApi(client);
            var users = new UsersApi(client);
            // 매번 새 사람: k_ + 시각 숫자(가짜 카카오가 회원번호로 씀)
            var kakao = "k_" + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000);
            var nick = "확인" + (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100000);
            try
            {
                Log($"서버 {ApiConfig.BaseUrl}, 카카오 {kakao}");
                var agreements = await auth.AgreementsAsync();
                Log($"약관 {agreements.Count}개: {string.Join(", ", agreements.ConvertAll(a => a.AgreementId + (a.Required ? "(필수)" : "")))}");

                var first = await auth.KakaoLoginAsync(kakao);
                Log($"카카오 로그인: 새 사람={first.IsNewUser}, 가입 중={session.IsSigningUp}");

                var check = await users.CheckNicknameAsync(" " + nick + " "); // 앞뒤 공백 → 서버가 정규화
                Log($"닉네임 확인 '{nick}' → '{check.Nickname}' 사용 가능={check.Available} 이유={check.Reason ?? "-"}");

                var req = new SignupRequest { Nickname = check.Nickname, HeightCm = 175, WeightKg = 70 };
                foreach (var a in agreements) if (a.Required) req.AgreedIds.Add(a.AgreementId);
                await auth.SignupAsync(req);
                Log($"가입 끝: 로그인={session.IsLoggedIn}");

                var mine = await users.CheckNicknameAsync(check.Nickname);
                Log($"로그인 뒤 내 닉네임 확인 → 사용 가능={mine.Available}(내 것은 가능)");

                session.Clear();
                var again = await auth.KakaoLoginAsync(kakao);
                Log($"다시 로그인: 새 사람={again.IsNewUser}, 로그인={session.IsLoggedIn}");

                await auth.LogoutAsync();
                Log($"로그아웃: 로그인={session.IsLoggedIn}");
                Log("성공");
                return true;
            }
            catch (ApiException e)
            {
                Debug.LogError("[ApiSmoke] 실패: " + e);
                return false;
            }
        }

        static void Log(string m) => Debug.Log("[ApiSmoke] " + m);
    }
}
