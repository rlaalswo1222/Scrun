using System.Collections.Generic;

namespace Scrun.App
{
    // 서버 요청·응답 모양(필드 이름은 JSON에서 camelCase로 바뀜). 기준은 backend/app/schemas, docs/api/openapi.json.
    // 서버 모양을 바꾸면 여기도 같이 바꾼다.

    public sealed class LoginResult
    {
        public bool IsNewUser { get; set; }      // true면 가입 화면으로(그때는 SignupToken만 있음)
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int? ExpiresInS { get; set; }
        public string SignupToken { get; set; }
    }

    public sealed class TokenPair
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int ExpiresInS { get; set; }
    }

    public sealed class Agreement
    {
        public string AgreementId { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; }
        public string Version { get; set; }
        public string Url { get; set; } // 약관 본문 주소
    }

    public sealed class SignupRequest
    {
        public List<string> AgreedIds { get; set; } = new List<string>();
        public string Nickname { get; set; }
        public int HeightCm { get; set; }  // 100–230
        public int WeightKg { get; set; }  // 20–250
        public string Timezone { get; set; } = "Asia/Seoul";
        public string DeviceId { get; set; } // 비우면 ApiClient가 이 기기 값으로 채움
    }

    public sealed class NicknameCheck
    {
        public string Nickname { get; set; } // 서버가 정규화한 닉네임(가입 때 이대로 저장됨) → 화면에 이 값을 보여 줌
        public bool Available { get; set; }
        public string Reason { get; set; }   // INVALID(형식) / NOT_ALLOWED(금칙어) / DUPLICATED(중복) / null
    }
}
