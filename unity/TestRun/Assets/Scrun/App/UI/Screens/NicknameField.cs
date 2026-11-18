using System;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 닉네임 칸(A-06 가입·H-11 프로필 편집이 같이 씀, 계획 0031·0049). 입력을 멈추면 서버에 확인(가입과 같은 판정·정규화)하고
    // 칸 아래에 결과를 보여 준다. 연결·서버 문제면 입력이 그대로인 동안 다시 확인. 늦게 온 옛 결과는 버림.
    // unchanged가 참이면(프로필 편집에서 지금 닉네임 그대로) 확인하지 않음
    public sealed class NicknameField
    {
        const long CheckDelayMs = 400;   // 입력을 멈추고 이만큼 지나면 확인
        const long RetryDelayMs = 3000;  // 연결 문제로 확인을 못 했으면 이만큼 뒤 다시(입력이 그대로일 때)

        readonly VisualElement root;
        readonly TextField field;
        readonly Label status;
        readonly Func<bool> unchanged;
        readonly Action changed;
        IVisualElementScheduledItem pending;
        int seq;

        // 지금 입력에 대한 확인 결과(입력이 바뀌면 null)
        public NicknameCheck Checked { get; private set; }

        public NicknameField(VisualElement root, TextField field, Label status, Action changed, Func<bool> unchanged = null)
        {
            this.root = root;
            this.field = field;
            this.status = status;
            this.changed = changed;
            this.unchanged = unchanged ?? (() => false);
            field.maxLength = ProfileForm.NicknameInputMax;
            field.RegisterValueChangedCallback(_ => OnChanged());
            SetStatus("", false);
        }

        public void Stop() => pending?.Pause();

        // 저장·가입이 닉네임 때문에 거절됨(확인과 저장 사이에 다른 사람이 가져감 등) → 칸 아래에
        public void Reject(string message)
        {
            Checked = null;
            SetStatus(message, true);
        }

        // 서버가 이 응답으로 닉네임을 거절했는지(저장·가입 onError에서)
        public static bool IsRejection(ApiException e) => e.Code == "NICKNAME_DUPLICATED" || e.Code == "NICKNAME_NOT_ALLOWED";

        void OnChanged()
        {
            Checked = null;
            seq++;
            pending?.Pause();
            var text = field.value?.Trim() ?? "";
            if (text.Length == 0 || unchanged()) SetStatus("", false);
            else
            {
                SetStatus("확인 중", false);
                pending = root.schedule.Execute(Check).StartingIn(CheckDelayMs);
            }
            changed?.Invoke();
        }

        async void Check()
        {
            int mine = seq;
            var text = field.value;
            NicknameCheck result;
            try
            {
                result = await Api.Users.CheckNicknameAsync(text);
            }
            catch (ApiException e)
            {
                if (mine != seq || root.panel == null) return;
                if (e.Failure == ApiFailure.NotLoggedIn) { Feedback.Handle(e); return; } // 가입용 토큰 만료(30분)·로그인 만료
                bool transient = e.Failure != ApiFailure.Server || e.Status >= 500 || e.Status == 429;
                SetStatus(transient ? "확인하지 못했어요. 잠시 뒤 다시 확인해요" : e.Message, true);
                if (transient) pending = root.schedule.Execute(Check).StartingIn(RetryDelayMs);
                return;
            }
            if (mine != seq || root.panel == null) return; // 그사이 입력이 바뀜
            Checked = result;
            SetStatus(ProfileForm.DescribeNickname(text, result), !result.Available);
            changed?.Invoke();
        }

        void SetStatus(string text, bool isError)
        {
            status.text = text;
            status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            status.EnableInClassList("error-text", isError);
        }
    }
}
