using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 로딩·오류 공통 처리(화면 I-07 로딩, I-08 오류, I-09 네트워크). 화면은 서버 호출을 Feedback.Run으로 감싼다.
    //   bool ok = await Feedback.Run(async () => list = await Api.Auth.AgreementsAsync(), retry: Load);
    // 로그인이 끝났으면(NotLoggedIn) 오류창 대신 로그인 화면으로 보낸다(AppRoot).
    // 문구는 임시 — 상준이 Figma 예외 화면(I-07–I-09)과 docs/api/README.md "오류 코드"의 문구 칸을 채우면 바꾼다.
    public static class Feedback
    {
        static VisualElement loading;
        static VisualElement errorBox;
        static Label errorTitle, errorBody, errorId;
        static Button errorRetry, errorClose;
        static Action retryAction;
        static int loadingCount;

        // 로그인이 필요할 때(재발급 거절 등) 부를 곳. AppRoot가 로그인 화면으로 보냄
        public static Action<string> OnNotLoggedIn;

        internal static void Attach(VisualElement root)
        {
            loading = root.Q("loading");
            errorBox = root.Q("error");
            errorTitle = root.Q<Label>("error-title");
            errorBody = root.Q<Label>("error-body");
            errorId = root.Q<Label>("error-id");
            errorRetry = root.Q<Button>("error-retry");
            errorClose = root.Q<Button>("error-close");
            errorRetry.clicked += () => { HideError(); retryAction?.Invoke(); };
            errorClose.clicked += HideError;
            loadingCount = 0;
            Set(loading, false);
            Set(errorBox, false);
        }

        internal static void Detach()
        {
            loading = errorBox = null;
            retryAction = null;
            loadingCount = 0;
        }

        // 서버 호출 하나를 로딩 표시와 함께 돌리고, 실패하면 알맞은 안내를 띄운다. 성공하면 true
        public static async Task<bool> Run(Func<Task> work, Action retry = null)
        {
            ShowLoading(true);
            try
            {
                await work();
                return true;
            }
            catch (ApiException e)
            {
                Handle(e, retry);
                return false;
            }
            finally
            {
                ShowLoading(false);
            }
        }

        // 실패 하나를 화면에 알림(Run 밖에서 직접 잡았을 때)
        public static void Handle(ApiException e, Action retry = null)
        {
            Debug.LogWarning("[Api] " + e); // 토큰은 메시지에 없음
            if (e.Failure == ApiFailure.NotLoggedIn)
            {
                OnNotLoggedIn?.Invoke("다시 로그인해 주세요.");
                return;
            }
            var (title, body, canRetry) = Describe(e);
            ShowError(title, body, e.RequestId, canRetry ? retry : null);
        }

        // 실패 종류 → 제목·본문·다시 시도 가능 여부(임시 문구)
        public static (string title, string body, bool canRetry) Describe(ApiException e)
        {
            switch (e.Failure)
            {
                case ApiFailure.Network:
                    return ("연결이 안 돼요", "인터넷 연결을 확인하고 다시 시도해 주세요.", true);
                case ApiFailure.BadResponse:
                    return ("잠시 후 다시 시도해 주세요", "서버 응답을 읽지 못했어요.", true);
                default:
                    if (e.Status >= 500 || e.Status == 429)
                        return ("잠시 후 다시 시도해 주세요", e.Message, true);
                    return ("확인해 주세요", e.Message, false); // 보낸 값 문제(400·409 등) → 다시 보내도 같음
            }
        }

        public static void ShowLoading(bool on)
        {
            loadingCount = Math.Max(0, loadingCount + (on ? 1 : -1));
            Set(loading, loadingCount > 0);
        }

        public static void ShowError(string title, string body, string requestId = null, Action retry = null)
        {
            if (errorBox == null) return;
            errorTitle.text = title;
            errorBody.text = body;
            errorId.text = string.IsNullOrEmpty(requestId) ? "" : "요청 번호 " + requestId;
            retryAction = retry;
            Set(errorRetry, retry != null);
            Set(errorBox, true);
        }

        public static void HideError()
        {
            retryAction = null;
            Set(errorBox, false);
        }

        static void Set(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
