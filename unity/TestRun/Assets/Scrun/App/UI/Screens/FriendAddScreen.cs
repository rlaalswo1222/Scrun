using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // G-05 친구 추가 · G-06 입력 중 · G-08 오류(계획 0044 2번). 내 코드 복사·공유 + 받은 코드 입력 → G-07.
    // 코드 모양(8글자)이 아니면 서버에 보내지 않는다: 서버는 없는 코드를 10분에 10번 넘게 넣으면 막아서, 오타로 횟수를 쓰지 않게.
    public sealed class FriendAddScreen : AppScreen
    {
        Label title, lead, myCode, notice, inputTitle, error;
        VisualElement codeCard, codeBox;
        TextField input;
        Button copy, share, submit;
        string code;
        bool sending, sharing, focused, loadingMe;
        IVisualElementScheduledItem hideNotice;

        protected override string Uxml => "UI/FriendAdd";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => Nav.Pop();
            title = Q<Label>("title");
            lead = Q<Label>("lead");
            codeCard = Q<VisualElement>("my-code-card");
            myCode = Q<Label>("my-code");
            notice = Q<Label>("code-notice");
            inputTitle = Q<Label>("input-title");
            codeBox = Q<VisualElement>("code-box");
            input = Q<TextField>("code");
            error = Q<Label>("error");
            copy = Q<Button>("copy");
            share = Q<Button>("share");
            submit = Q<Button>("submit");

            input.maxLength = FriendText.InputMax; // 공유 문구를 통째로 붙여 넣어도 됨(코드만 골라 보냄)
            input.textEdition.placeholder = "받은 코드를 입력해 주세요";
            input.keyboardType = TouchScreenKeyboardType.ASCIICapable;
            input.RegisterCallback<FocusInEvent>(_ => { focused = true; codeBox.AddToClassList("fig-field--focus"); Refresh(); });
            input.RegisterCallback<FocusOutEvent>(_ => { focused = false; codeBox.RemoveFromClassList("fig-field--focus"); Refresh(); });
            input.RegisterValueChangedCallback(_ => { ShowError(null); Refresh(); });
            input.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(); });

            copy.clicked += Copy;
            share.clicked += Share;
            submit.clicked += Submit;
            notice.SetVisible(false);
            ShowError(null);
            SetCode(null);
        }

        // 처음 보일 때(완료 화면에서 돌아올 때는 이미 있음)
        protected internal override void OnShow()
        {
            if (code == null) LoadMyCode();
        }

        async void LoadMyCode()
        {
            if (loadingMe) return;
            loadingMe = true;
            Me me = null;
            bool ok = await Feedback.Run(async () => me = await Api.Users.MeAsync(), LoadMyCode);
            loadingMe = false;
            if (ok && Root.panel != null) SetCode(me?.FriendCode);
        }

        void SetCode(string c)
        {
            code = string.IsNullOrEmpty(c) ? null : c;
            myCode.text = code ?? "········";
            copy.SetEnabled(code != null);
            share.SetEnabled(code != null);
        }

        // G-06: 칸을 누르거나 뭔가 적혀 있으면 입력 모양(내 코드 칸 접힘). 8글자 모양이면 버튼이 진한 파랑
        internal void Refresh() // 테스트는 패널 없이 값만 바꿔 부름(값 바뀜 이벤트는 패널이 있어야 옴)
        {
            bool typing = focused || !string.IsNullOrEmpty(input.value);
            title.text = typing ? "코드를 입력해 주세요" : "친구를 추가해요";
            lead.text = typing ? $"친구에게 받은 {FriendText.CodeLength}자리 코드예요" : "내 친구 코드를 공유하거나, 받은 코드를 입력하세요";
            codeCard.SetVisible(!typing);
            inputTitle.SetVisible(!typing);
            bool ready = FriendText.NormalizeCode(input.value) != null;
            submit.EnableInClassList("btn-primary", ready);
            submit.EnableInClassList("btn-soft", !ready);
        }

        void ShowError(string message)
        {
            error.text = message ?? "";
            error.SetVisible(message != null);
            codeBox.EnableInClassList("fr-field--error", message != null);
        }

        void Copy()
        {
            if (code == null) return;
            try
            {
                Native.Share.Copy(code);
                ShowNotice("코드를 복사했어요"); // 임시 문구(상준)
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Feedback.ShowError("복사하지 못했어요", "코드를 길게 눌러 직접 복사해 주세요.");
            }
        }

        async void Share()
        {
            if (code == null || sharing) return;
            sharing = true;
            try { await Native.Share.ShareTextAsync(FriendText.ShareMessage(code)); }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Root.panel != null) Feedback.ShowError("공유하지 못했어요", "복사 버튼으로 코드를 복사해 보내 주세요.");
            }
            finally { sharing = false; }
        }

        void ShowNotice(string text)
        {
            notice.text = text;
            notice.SetVisible(true);
            hideNotice?.Pause();
            hideNotice = notice.schedule.Execute(() => notice.SetVisible(false)).StartingIn(2000);
        }

        async void Submit()
        {
            if (sending || Nav.Current != this) return;
            var local = FriendText.InputError(input.value);
            if (local != null)
            {
                ShowError(local);
                return;
            }
            string normalized = FriendText.NormalizeCode(input.value);
            if (normalized == code)
            {
                ShowError(FriendText.AddError("SELF_FRIEND_CODE")); // 내 코드면 서버에 안 보냄
                return;
            }
            sending = true;
            Friend added = null;
            bool ok = await Feedback.Run(async () => added = await Api.Friends.AddAsync(normalized), Submit, OnAddFailed);
            sending = false;
            if (!ok || added == null || Root.panel == null || Nav.Current != this) return;
            FriendDirectory.Add(added);
            input.SetValueWithoutNotify("");
            input.Blur();
            Refresh();
            Nav.Push(new FriendAddedScreen(added));
        }

        // 서버 오류 중 칸 아래에 보일 것(G-08). 나머지(네트워크 등)는 공통 안내창
        bool OnAddFailed(ApiException e)
        {
            if (e.Failure != ApiFailure.Server) return false;
            var message = FriendText.AddError(e.Code, Who(e.DetailsJson));
            if (message == null) return false;
            ShowError(message);
            return true;
        }

        static string Who(string detailsJson)
        {
            if (string.IsNullOrEmpty(detailsJson)) return null;
            try { return (string)JObject.Parse(detailsJson)["who"]; }
            catch (Exception) { return null; }
        }
    }
}
