using System;

namespace Scrun.App
{
    public enum ApiFailure
    {
        Server,  // 서버가 답했지만 실패(Status·Code를 보고 화면 문구를 고름, docs/api/README.md "오류 코드")
        Network, // 연결 안 됨·시간 초과(서버에 닿았는지 모름) → "인터넷 연결을 확인해 주세요" + 다시 시도
        BadResponse, // 서버 응답을 읽지 못함(서버 앞단의 502 HTML 등)
        NotLoggedIn, // 로그인이 필요한 API인데 토큰이 없거나 재로그인이 필요함 → 로그인 화면으로
    }

    // 서버 호출 실패 하나. 화면은 Failure·Code로 갈래를 나누고, 오류 화면에는 RequestId를 작게 보여 준다.
    public sealed class ApiException : Exception
    {
        public ApiFailure Failure { get; }
        public int Status { get; }        // HTTP 상태(네트워크 실패면 0)
        public string Code { get; }       // 서버 error.code(예: NICKNAME_DUPLICATED). 없으면 null
        public string RequestId { get; }  // 서버 기록에서 이 요청을 찾는 번호. 없으면 null
        public string DetailsJson { get; } // error.details 원문(틀린 칸 등). 없으면 null

        public ApiException(ApiFailure failure, string message, int status = 0, string code = null,
                            string requestId = null, string detailsJson = null)
            : base(message)
        {
            Failure = failure;
            Status = status;
            Code = code;
            RequestId = requestId;
            DetailsJson = detailsJson;
        }

        public override string ToString() =>
            $"ApiException({Failure}, {Status}, {Code ?? "-"}, requestId={RequestId ?? "-"}): {Message}";
    }
}
