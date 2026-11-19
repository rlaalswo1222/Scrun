using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 로딩·오류 공통 처리. 화면은 서버 호출을 Feedback.Run으로 감싼다(버튼 동작: 덮개 + 실패 안내창).
    // 화면을 처음 불러올 때는 Feedback.Load(state, …): 덮개 대신 그 화면의 공통 칸(StateView)에 스켈레톤·실패·네트워크 없음(계획 0056).
    // 화면 번호(Figma 11/19): I-07 네트워크 없음, I-08 로딩·스켈레톤, I-09 일시적인 오류 — 처음 불러오기에선 StateView가 이 셋을 보여 줌
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
            errorRetry.clicked += PressRetry;
            errorClose.clicked += HideError;
            loadingCount = 0;
            Set(loading, false);
            Set(errorBox, false);
        }

        // 오류창의 "다시 시도"
        internal static void PressRetry()
        {
            var retry = retryAction; // HideError가 지우기 전에 잡아 둠
            HideError();
            retry?.Invoke();
        }

        internal static void Detach()
        {
            loading = errorBox = null;
            retryAction = null;
            loadingCount = 0;
        }

        // 로딩 덮개나 오류창이 떠 있으면 true(그동안 뒤로 가기 키를 막음)
        public static bool IsBlocking => IsShown(loading) || IsShown(errorBox);

        // 서버 호출 하나를 로딩 표시와 함께 돌리고, 실패하면 알맞은 안내를 띄운다. 성공하면 true.
        // onError가 true를 돌려주면 그 화면이 직접 처리한 것(예: 닉네임 중복은 입력 칸 아래에)이라 안내창을 띄우지 않음.
        // overlay: false면 덮개(I-07) 없이(끌어서 새로고침처럼 화면이 자기 표시를 가질 때). 실패 안내는 그대로
        public static async Task<bool> Run(Func<Task> work, Action retry = null, Func<ApiException, bool> onError = null, bool overlay = true)
        {
            if (overlay) ShowLoading(true);
            try
            {
                await work();
                return true;
            }
            catch (ApiException e)
            {
                if (onError == null || !onError(e)) Handle(e, retry);
                return false;
            }
            catch (Exception e)
            {
                // 앱 쪽 버그·기기 기능 오류. 화면이 멈춘 것처럼 보이지 않게 안내는 띄우고 기록은 남긴다
                Debug.LogException(e);
                ShowError("문제가 생겼어요", "잠시 후 다시 시도해 주세요.", null, retry);
                return false;
            }
            finally
            {
                if (overlay) ShowLoading(false);
            }
        }

        // 화면이 보일 때마다 다시 받는 곳의 규칙 하나: 아직 보여 줄 게 없으면(처음) 공통 칸에 스켈레톤·실패, 이미 보이면 조용히(RunQuiet).
        // 탭을 누를 때마다 "잠시만요"가 화면을 덮지 않게. (덮개·안내창으로 처음 받던 옛 Load(bool …)는 계획 0056에서 없앰: 새 화면도 StateView로)

        // 공통 상태 칸이 있는 화면의 처음 불러오기(계획 0056 정할 것 1·2 가): 덮개 대신 그 칸에 스켈레톤, 실패하면 그 칸에
        // 실패·네트워크 없음(안내창은 안 띄움). 이미 보이면 조용히(위와 같음). onError가 true면 화면이 직접 처리(state.Message 등)
        // current: 이 요청이 아직 최신인지(칩을 빨리 바꿔 버려진 옛 요청이 새 요청의 스켈레톤·실패 칸을 건드리지 않게, 리뷰)
        public static async Task<bool> Load(StateView state, bool shown, Func<Task> work, Action retry, Func<ApiException, bool> onError = null,
                                            Func<bool> current = null)
        {
            if (shown) return await RunQuiet(work, onError);
            if (state == null) return await Run(work, retry, onError);
            state.Loading();
            try
            {
                await work();
                if (current == null || current()) state.Clear();
                return true;
            }
            catch (ApiException e)
            {
                if (current != null && !current())
                {
                    if (e.Failure == ApiFailure.NotLoggedIn) Handle(e);
                    return false;
                }
                if (e.Failure == ApiFailure.NotLoggedIn)
                {
                    state.Clear();
                    Handle(e);
                }
                else if (onError != null && onError(e))
                {
                    if (state.Current == StateView.Mode.Loading) state.Clear(); // 화면이 안내를 띄우지 않았으면 스켈레톤만 걷음
                }
                else
                {
                    Debug.LogWarning("[Api] 처음 불러오기 실패: " + e);
                    state.Failed(e, retry);
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (current == null || current()) state.Failed(null, retry);
                return false;
            }
        }

        // 이미 보이는 화면을 뒤에서 다시 받을 때(탭에 다시 들어옴·뒤 화면에서 돌아옴): 덮개·오류창 없이, 실패하면 지난 값 그대로.
        // 로그인이 풀렸으면 로그인으로 보내고, onError는 화면이 직접 처리할 실패(없어진 친구·코스 등)에만 불림
        public static async Task<bool> RunQuiet(Func<Task> work, Func<ApiException, bool> onError = null)
        {
            try
            {
                await work();
                return true;
            }
            catch (ApiException e)
            {
                if (e.Failure == ApiFailure.NotLoggedIn) Handle(e);
                else if (onError == null || !onError(e)) Debug.LogWarning("[Api] 조용히 다시 받기 실패(지난 값 그대로): " + e);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
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

        static bool IsShown(VisualElement e) => e != null && e.style.display == DisplayStyle.Flex;

        static void Set(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
