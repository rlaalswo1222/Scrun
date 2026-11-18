using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-11 프로필 편집(계획 0049, Figma 700:2476). 닉네임·키·몸무게 → PATCH /users/me(바꾼 칸만).
    // 닉네임 확인·정규화는 가입(A-06)과 같은 NicknameField(내 지금 닉네임 그대로면 확인 안 함), 키·몸무게 범위도 같음(ProfileForm).
    // 재보정은 권장만(정할 것 2 가): 아래 안내 + "재보정하기 →" → H-09. 바꾼 게 없거나 확인 전이면 저장이 꺼짐
    public sealed class ProfileEditScreen : AppScreen
    {
        readonly Me me;
        TextField nickname, height, weight;
        Label bodyStatus;
        Button submit;
        NicknameField nick;
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
            height = Q<TextField>("height");
            weight = Q<TextField>("weight");
            bodyStatus = Q<Label>("body-status");
            submit = Q<Button>("submit");

            nickname.SetValueWithoutNotify(me.Nickname ?? "");
            nick = new NicknameField(Root, nickname, Q<Label>("nickname-status"), Refresh, () => NicknameUnchanged);
            height.keyboardType = TouchScreenKeyboardType.NumberPad;
            weight.keyboardType = TouchScreenKeyboardType.NumberPad;
            height.maxLength = weight.maxLength = 3;
            height.SetValueWithoutNotify(me.HeightCm > 0 ? me.HeightCm.ToString() : "");
            weight.SetValueWithoutNotify(me.WeightKg > 0 ? me.WeightKg.ToString() : "");
            ProfileForm.WireFocus(Root, (nickname, "nickname-box"), (height, "height-box"), (weight, "weight-box"));
            height.RegisterValueChangedCallback(_ => Refresh());
            weight.RegisterValueChangedCallback(_ => Refresh());
            height.RegisterCallback<FocusOutEvent>(_ => { showBodyError = true; Refresh(); });
            weight.RegisterCallback<FocusOutEvent>(_ => { showBodyError = true; Refresh(); });
            submit.clicked += Submit;
            Refresh();
        }

        protected internal override void OnDestroy() => nick.Stop();

        bool NicknameUnchanged => (nickname.value?.Trim() ?? "") == (me.Nickname ?? "");

        // 보낼 것(바꾼 칸만). 보낼 게 없거나 아직 맞지 않으면 null
        MeUpdate Pending()
        {
            if (!ProfileForm.BodyFilled(height.value, weight.value)) return null;
            ProfileForm.CheckBody(height.value, weight.value, out int h, out int w);
            var up = new MeUpdate();
            bool any = false;
            if (!NicknameUnchanged)
            {
                if (nick.Checked == null || !nick.Checked.Available) return null;
                up.Nickname = nick.Checked.Nickname;
                any = true;
            }
            if (h != me.HeightCm) { up.HeightCm = h; any = true; }
            if (w != me.WeightKg) { up.WeightKg = w; any = true; }
            return any ? up : null;
        }

        internal void Refresh() // 테스트가 값을 바꾼 뒤 부름(패널 밖에선 값 변경 이벤트가 안 옴)
        {
            bodyStatus.text = ProfileForm.BodyMessage(height.value, weight.value, showBodyError) ?? "";
            if (ProfileForm.BodyFilled(height.value, weight.value)) showBodyError = false; // 고치면 바로 지움
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
                if (!NicknameField.IsRejection(e)) return false;
                if (Root.panel != null) nick.Reject(e.Message); // 확인과 저장 사이에 다른 사람이 가져감 등
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
