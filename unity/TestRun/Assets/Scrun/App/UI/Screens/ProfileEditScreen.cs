using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-11 프로필 편집(계획 0049, Figma 700:2476). 닉네임·키·몸무게 → PATCH /users/me(바꾼 칸만).
    // 닉네임은 가입(A-06)과 같은 확인·정규화(서버 /users/nickname/check, 내 지금 닉네임은 사용 가능), 키·몸무게 범위도 같음(ProfileForm).
    // 재보정은 권장만(정할 것 2 가): 아래 안내 + "재보정하기 →" → H-09. 바꾼 게 없거나 확인 전이면 저장이 꺼짐
    public sealed class ProfileEditScreen : AppScreen
    {
        const long CheckDelayMs = 400;
        const long RetryDelayMs = 3000;

        readonly Me me;
        TextField nickname, height, weight;
        Label nicknameStatus, bodyStatus;
        Button submit;
        IVisualElementScheduledItem pendingCheck;
        int checkSeq;
        NicknameCheck checkedNick;
        bool submitting, showBodyError;

        public ProfileEditScreen(Me me) => this.me = me ?? throw new System.ArgumentNullException(nameof(me));

        protected override string Uxml => "UI/ProfileEdit";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            Q<Label>("lead").text = ProfileForm.Lead;
            Q<Label>("recal-title").text = MyText.RecalibrateTitle;
            Q<Label>("recal-body").text = MyText.RecalibrateBody;
            Q<Button>("recalibrate").clicked += () => { if (Nav.Current == this) Nav.Push(new RecalibrateScreen()); };
            nickname = Q<TextField>("nickname");
            nicknameStatus = Q<Label>("nickname-status");
            height = Q<TextField>("height");
            weight = Q<TextField>("weight");
            bodyStatus = Q<Label>("body-status");
            submit = Q<Button>("submit");

            nickname.maxLength = ProfileForm.NicknameInputMax;
            height.keyboardType = TouchScreenKeyboardType.NumberPad;
            weight.keyboardType = TouchScreenKeyboardType.NumberPad;
            height.maxLength = weight.maxLength = 3;
            nickname.SetValueWithoutNotify(me.Nickname ?? "");
            height.SetValueWithoutNotify(me.HeightCm > 0 ? me.HeightCm.ToString() : "");
            weight.SetValueWithoutNotify(me.WeightKg > 0 ? me.WeightKg.ToString() : "");
            foreach (var (field, box) in new[] { (nickname, "nickname-box"), (height, "height-box"), (weight, "weight-box") })
            {
                var b = Q<VisualElement>(box);
                field.RegisterCallback<FocusInEvent>(_ => b.AddToClassList("fig-field--focus"));
                field.RegisterCallback<FocusOutEvent>(_ => b.RemoveFromClassList("fig-field--focus"));
            }
            nickname.RegisterValueChangedCallback(_ => OnNicknameChanged());
            height.RegisterValueChangedCallback(_ => Refresh());
            weight.RegisterValueChangedCallback(_ => Refresh());
            height.RegisterCallback<FocusOutEvent>(_ => { showBodyError = true; Refresh(); });
            weight.RegisterCallback<FocusOutEvent>(_ => { showBodyError = true; Refresh(); });
            submit.clicked += Submit;
            SetNickStatus("", false);
            Refresh();
        }

        protected internal override void OnDestroy() => pendingCheck?.Pause();

        // 지금 닉네임 그대로면 확인할 필요 없음
        bool NicknameUnchanged => (nickname.value?.Trim() ?? "") == (me.Nickname ?? "");

        void OnNicknameChanged()
        {
            checkedNick = null;
            checkSeq++;
            pendingCheck?.Pause();
            var text = nickname.value?.Trim() ?? "";
            if (text.Length == 0 || NicknameUnchanged) SetNickStatus("", false);
            else
            {
                SetNickStatus("확인 중", false);
                pendingCheck = Root.schedule.Execute(CheckNickname).StartingIn(CheckDelayMs);
            }
            Refresh();
        }

        async void CheckNickname()
        {
            int seq = checkSeq;
            var text = nickname.value;
            NicknameCheck result;
            try
            {
                result = await Api.Users.CheckNicknameAsync(text);
            }
            catch (ApiException e)
            {
                if (seq != checkSeq || Root.panel == null) return;
                if (e.Failure == ApiFailure.NotLoggedIn) { Feedback.Handle(e); return; }
                bool transient = e.Failure != ApiFailure.Server || e.Status >= 500 || e.Status == 429;
                SetNickStatus(transient ? "확인하지 못했어요. 잠시 뒤 다시 확인해요" : e.Message, true);
                if (transient) pendingCheck = Root.schedule.Execute(CheckNickname).StartingIn(RetryDelayMs);
                return;
            }
            if (seq != checkSeq || Root.panel == null) return;
            checkedNick = result;
            SetNickStatus(ProfileForm.DescribeNickname(text, result), !result.Available);
            Refresh();
        }

        void SetNickStatus(string text, bool isError)
        {
            nicknameStatus.text = text;
            nicknameStatus.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            nicknameStatus.EnableInClassList("error-text", isError);
        }

        // 보낼 것(바꾼 칸만). 보낼 게 없거나 아직 맞지 않으면 null
        MeUpdate Pending()
        {
            if (ProfileForm.CheckBody(height.value, weight.value, out int h, out int w) != null) return null;
            if (string.IsNullOrEmpty(height.value) || string.IsNullOrEmpty(weight.value)) return null;
            var up = new MeUpdate();
            bool any = false;
            if (!NicknameUnchanged)
            {
                if (checkedNick == null || !checkedNick.Available) return null;
                up.Nickname = checkedNick.Nickname;
                any = true;
            }
            if (h != me.HeightCm) { up.HeightCm = h; any = true; }
            if (w != me.WeightKg) { up.WeightKg = w; any = true; }
            return any ? up : null;
        }

        internal void Refresh() // 테스트가 값을 바꾼 뒤 부름(패널 밖에선 값 변경 이벤트가 안 옴)
        {
            var bodyProblem = ProfileForm.CheckBody(height.value, weight.value, out _, out _);
            if (bodyProblem == null) showBodyError = false;
            bodyStatus.text = showBodyError ? bodyProblem ?? "" : "";
            bodyStatus.style.display = string.IsNullOrEmpty(bodyStatus.text) ? DisplayStyle.None : DisplayStyle.Flex;
            submit.SetEnabled(!submitting && Pending() != null);
        }

        async void Submit()
        {
            var up = Pending();
            if (submitting || up == null) return;
            submitting = true;
            Refresh();
            Me saved = null;
            bool ok = await Feedback.Run(async () => saved = await Api.Users.UpdateMeAsync(up), retry: Submit, onError: e =>
            {
                if (e.Code != "NICKNAME_DUPLICATED" && e.Code != "NICKNAME_NOT_ALLOWED") return false;
                checkedNick = null; // 확인과 저장 사이에 다른 사람이 가져감 등 → 칸 아래에
                if (Root.panel != null) SetNickStatus(e.Message, true);
                return true;
            });
            submitting = false;
            if (Root.panel == null) return;
            if (!ok) { Refresh(); return; }
            me.Nickname = saved.Nickname;
            me.HeightCm = saved.HeightCm;
            me.WeightKg = saved.WeightKg;
            if (Nav.Current == this) Nav.Pop(); // 마이 메인이 보일 때 다시 받음
        }
    }
}
