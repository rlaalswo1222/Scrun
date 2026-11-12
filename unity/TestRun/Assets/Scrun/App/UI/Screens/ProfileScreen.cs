using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-06 프로필 입력(Figma 596:232, 계획 0031): 닉네임(다른 러너 이름표에 보임)·키·몸무게 → 가입 → 권한 안내(A-07).
    // 닉네임은 입력을 멈추면 서버에 확인하고(가입과 같은 판정), 서버가 정리한 값(정규화)을 보여 주고 그 값으로 가입한다.
    // 셋 다 맞게 채워야 "다음"이 켜진다(Figma 노트).
    public sealed class ProfileScreen : AppScreen
    {
        const long CheckDelayMs = 400;   // 입력을 멈추고 이만큼 지나면 확인
        const long RetryDelayMs = 3000;  // 연결 문제로 확인을 못 했으면 이만큼 뒤 다시(입력이 그대로일 때)

        readonly List<string> agreedIds;
        TextField nickname, height, weight;
        Label nicknameStatus, bodyStatus;
        Button submit;
        IVisualElementScheduledItem pendingCheck;
        int checkSeq;          // 늦게 온 옛 확인 결과를 버리려고
        NicknameCheck checkedNick; // 지금 입력에 대한 확인 결과(입력이 바뀌면 null)
        bool submitting;
        bool showBodyError; // 키·몸무게 칸을 벗어난 뒤에만 오류를 보여 줌(적는 중 "1"에 빨간 글씨가 뜨지 않게)

        public ProfileScreen(List<string> agreedIds) => this.agreedIds = agreedIds;

        protected override string Uxml => "UI/Profile";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            Q<Label>("lead").text = ProfileForm.Lead;
            nickname = Q<TextField>("nickname");
            nicknameStatus = Q<Label>("nickname-status");
            SetNickStatus("", false);
            height = Q<TextField>("height");
            weight = Q<TextField>("weight");
            bodyStatus = Q<Label>("body-status");
            submit = Q<Button>("submit");

            nickname.maxLength = ProfileForm.NicknameInputMax;
            height.keyboardType = TouchScreenKeyboardType.NumberPad;
            weight.keyboardType = TouchScreenKeyboardType.NumberPad;
            height.maxLength = weight.maxLength = 3;
            nickname.textEdition.placeholder = "예: 러너123";
            height.textEdition.placeholder = "예: 170";
            weight.textEdition.placeholder = "예: 65";
            // 고른 칸은 테두리 2 파랑(Figma 닉네임 칸)
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
            Refresh();
        }

        protected internal override void OnDestroy() => pendingCheck?.Pause();

        void OnNicknameChanged()
        {
            checkedNick = null;
            checkSeq++;
            pendingCheck?.Pause();
            var text = nickname.value?.Trim() ?? "";
            if (text.Length == 0) SetNickStatus("", false);
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
                if (e.Failure == ApiFailure.NotLoggedIn) { Feedback.Handle(e); return; } // 가입용 토큰 만료(30분)
                bool transient = e.Failure != ApiFailure.Server || e.Status >= 500 || e.Status == 429;
                SetNickStatus(transient ? "확인하지 못했어요. 잠시 뒤 다시 확인해요" : e.Message, true);
                // 연결·서버 문제면 입력이 그대로인 동안 다시 확인(사용자가 글자를 지웠다 다시 쓰지 않아도 되게)
                if (transient) pendingCheck = Root.schedule.Execute(CheckNickname).StartingIn(RetryDelayMs);
                return;
            }
            if (seq != checkSeq || Root.panel == null) return; // 그사이 입력이 바뀜
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

        void Refresh()
        {
            var bodyProblem = ProfileForm.CheckBody(height.value, weight.value, out _, out _);
            if (bodyProblem == null) showBodyError = false; // 고치면 바로 지움
            bodyStatus.text = showBodyError ? bodyProblem ?? "" : "";
            bodyStatus.style.display = string.IsNullOrEmpty(bodyStatus.text) ? DisplayStyle.None : DisplayStyle.Flex;
            submit.SetEnabled(!submitting && checkedNick != null && checkedNick.Available && bodyProblem == null
                              && !string.IsNullOrEmpty(height.value) && !string.IsNullOrEmpty(weight.value));
        }

        async void Submit()
        {
            if (submitting || checkedNick == null || !checkedNick.Available) return;
            if (ProfileForm.CheckBody(height.value, weight.value, out int h, out int w) != null) return;
            submitting = true;
            Refresh();
            var req = new SignupRequest { AgreedIds = agreedIds, Nickname = checkedNick.Nickname, HeightCm = h, WeightKg = w };
            // 로딩 덮개가 가입이 끝날 때까지 화면(뒤로 버튼 포함)을 막는다
            bool ok = await Feedback.Run(() => Api.Auth.SignupAsync(req), retry: Submit, onError: e =>
            {
                if (e.Code != "NICKNAME_DUPLICATED" && e.Code != "NICKNAME_NOT_ALLOWED") return false;
                // 확인과 가입 사이에 다른 사람이 가져감 등 → 안내창 대신 닉네임 칸 아래에
                checkedNick = null;
                if (Root.panel != null) SetNickStatus(e.Message, true);
                return true;
            });
            submitting = false;
            if (Root.panel == null) return; // 그사이 로그인 화면 등으로
            Refresh();
            if (ok) Onboarding.Begin(Nav, newUser: true); // 권한 안내 → 이어폰 → 주간 목표 → 완료 → 홈(계획 0025·0031)
        }
    }

    // 프로필 입력 규칙(서버 backend/app/schemas/auth.py와 같은 범위). 화면 없이 테스트하려고 따로 둠
    public static class ProfileForm
    {
        public const int NicknameInputMax = 20; // 서버가 정규화한 뒤 2–12자로 판정. 입력 칸은 조금 넉넉히
        public const int HeightMin = 100, HeightMax = 230;
        public const int WeightMin = 20, WeightMax = 250;

        // A-06 설명. Figma "키와 몸무게는 속도 추정 보정에만 사용돼요"와 옛 "키는 아바타 크기에 써요"는 둘 다 사실이 아님
        // (11/12 코드 기준 키·몸무게는 서버에 저장만 하고 속도·아바타 어디에도 안 씀). 개인정보 안내라 사실대로, 상준과 확정(계획 0031)
        public const string Lead = "키와 몸무게는 앞으로 속도 추정에 쓸 예정이에요";

        // 문제가 없으면 null. 비어 있으면 아직 안 적은 것이라 null(가입 버튼만 꺼짐)
        public static string CheckBody(string heightText, string weightText, out int heightCm, out int weightKg)
        {
            heightCm = weightKg = 0;
            if (!string.IsNullOrEmpty(heightText) &&
                (!int.TryParse(heightText, out heightCm) || heightCm < HeightMin || heightCm > HeightMax))
                return $"키는 {HeightMin}–{HeightMax} cm로 적어 주세요";
            if (!string.IsNullOrEmpty(weightText) &&
                (!int.TryParse(weightText, out weightKg) || weightKg < WeightMin || weightKg > WeightMax))
                return $"몸무게는 {WeightMin}–{WeightMax} kg으로 적어 주세요";
            return null;
        }

        // 닉네임 확인 결과 → 안내 문구(임시). 서버가 바꾼 값이면 무엇으로 저장되는지 알려 줌
        public static string DescribeNickname(string typed, NicknameCheck r)
        {
            if (r.Available)
                return r.Nickname != (typed ?? "").Trim() ? $"'{r.Nickname}'(으)로 저장돼요" : "쓸 수 있어요";
            switch (r.Reason)
            {
                case "INVALID": return "2–12자, 한글·영문·숫자·_만 쓸 수 있어요";
                case "NOT_ALLOWED": return "쓸 수 없는 닉네임이에요";
                case "DUPLICATED": return "이미 쓰는 닉네임이에요";
                default: return "쓸 수 없는 닉네임이에요";
            }
        }
    }
}
