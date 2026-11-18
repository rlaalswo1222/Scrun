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
        readonly List<string> agreedIds;
        TextField height, weight;
        Label bodyStatus;
        Button submit;
        NicknameField nick; // 확인·정규화·다시 확인은 H-11 프로필 편집과 같이 씀(계획 0049)
        bool submitting;
        bool showBodyError; // 키·몸무게 칸을 벗어난 뒤에만 오류를 보여 줌(적는 중 "1"에 빨간 글씨가 뜨지 않게)

        public ProfileScreen(List<string> agreedIds) => this.agreedIds = agreedIds;

        protected override string Uxml => "UI/Profile";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            Q<Label>("lead").text = ProfileForm.Lead;
            var nickname = Q<TextField>("nickname");
            height = Q<TextField>("height");
            weight = Q<TextField>("weight");
            bodyStatus = Q<Label>("body-status");
            submit = Q<Button>("submit");
            nick = new NicknameField(Root, nickname, Q<Label>("nickname-status"), Refresh);

            height.keyboardType = TouchScreenKeyboardType.NumberPad;
            weight.keyboardType = TouchScreenKeyboardType.NumberPad;
            height.maxLength = weight.maxLength = 3;
            nickname.textEdition.placeholder = "예: 러너123";
            height.textEdition.placeholder = "예: 170";
            weight.textEdition.placeholder = "예: 65";
            ProfileForm.WireFocus(Root, (nickname, "nickname-box"), (height, "height-box"), (weight, "weight-box"));

            height.RegisterValueChangedCallback(_ => Refresh());
            weight.RegisterValueChangedCallback(_ => Refresh());
            height.RegisterCallback<FocusOutEvent>(_ => { showBodyError = true; Refresh(); });
            weight.RegisterCallback<FocusOutEvent>(_ => { showBodyError = true; Refresh(); });
            submit.clicked += Submit;
            Refresh();
        }

        protected internal override void OnDestroy() => nick.Stop();

        void Refresh()
        {
            var bodyProblem = ProfileForm.BodyMessage(height.value, weight.value, showBodyError);
            if (ProfileForm.BodyFilled(height.value, weight.value)) showBodyError = false; // 고치면 바로 지움
            bodyStatus.text = bodyProblem ?? "";
            bodyStatus.style.display = string.IsNullOrEmpty(bodyStatus.text) ? DisplayStyle.None : DisplayStyle.Flex;
            submit.SetEnabled(!submitting && nick.Checked != null && nick.Checked.Available && ProfileForm.BodyFilled(height.value, weight.value));
        }

        async void Submit()
        {
            var checkedNick = nick.Checked;
            if (submitting || checkedNick == null || !checkedNick.Available) return;
            if (ProfileForm.CheckBody(height.value, weight.value, out int h, out int w) != null) return;
            submitting = true;
            Refresh();
            var req = new SignupRequest { AgreedIds = agreedIds, Nickname = checkedNick.Nickname, HeightCm = h, WeightKg = w };
            // 로딩 덮개가 가입이 끝날 때까지 화면(뒤로 버튼 포함)을 막는다
            bool ok = await Feedback.Run(() => Api.Auth.SignupAsync(req), retry: Submit, onError: e =>
            {
                if (!NicknameField.IsRejection(e)) return false;
                // 확인과 가입 사이에 다른 사람이 가져감 등 → 안내창 대신 닉네임 칸 아래에
                if (Root.panel != null) nick.Reject(e.Message);
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

        // 키·몸무게가 둘 다 맞게 적혔는지(가입·저장 버튼)
        public static bool BodyFilled(string heightText, string weightText) =>
            !string.IsNullOrEmpty(heightText) && !string.IsNullOrEmpty(weightText) && CheckBody(heightText, weightText, out _, out _) == null;

        // 칸 아래 문구. 칸을 벗어난 뒤에만(show): 범위 밖이면 그 이유, 비어 있으면 적어 달라고(버튼이 왜 꺼졌는지 알게, 계획 0049 리뷰)
        public static string BodyMessage(string heightText, string weightText, bool show)
        {
            if (!show) return null;
            return CheckBody(heightText, weightText, out _, out _)
                   ?? (string.IsNullOrEmpty(heightText) || string.IsNullOrEmpty(weightText) ? "키와 몸무게를 모두 적어 주세요" : null);
        }

        // 고른 칸은 테두리 2 파랑(Figma 닉네임 칸)
        public static void WireFocus(VisualElement root, params (TextField field, string box)[] pairs)
        {
            foreach (var (field, box) in pairs)
            {
                var b = root.Q<VisualElement>(box);
                field.RegisterCallback<FocusInEvent>(_ => b.AddToClassList("fig-field--focus"));
                field.RegisterCallback<FocusOutEvent>(_ => b.RemoveFromClassList("fig-field--focus"));
            }
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
