namespace Scrun.App
{
    // 보정 화면 문구(계획 0023). 상준이 문구를 다듬을 때 이 파일만 고친다.
    // Figma A-11–A-15를 바탕으로, 사다리 2–14 km/h·단계 15초(ADR-0005 추가 결정)와 다시·도움·멈춤 카드(Figma에 없음)를 더했다.
    public static class CalibrationCopy
    {
        // 사다리에서 걷기·달리기 문구를 가르는 속도(사람마다 다르지만 문구만의 기준. 센서 판정과 상관없음)
        const float RunFrom = 7f;

        public static string Kmh(float kmh) => $"{kmh:0.#} km/h";
        static string Move(float kmh) => kmh < RunFrom ? "걸어" : "달려";

        // A-11 안내
        public const string IntroTitle = "속도를 내 몸에 맞춰요";
        public static string IntroLead(int stages, float from, float to, float stageSec) =>
            $"러닝머신 속도를 {Kmh(from)}부터 {Kmh(to)}까지 차례로 올리며 {stageSec:0}초씩 걷고 달리면, " +
            $"폰이 내 발걸음과 속도를 맞춰 기억해요. {stages}단계, 2분쯤 걸려요.";
        public static readonly string[] IntroSteps =
        {
            "폰을 러닝머신 계기판 위에 가로로 올려요",
            "안내하는 속도로 러닝머신을 맞춰요",
            "단계마다 맞춘 속도로 걷거나 달려요",
        };
        public const string IntroNote = "처음 달리기 전에 한 번 해요. 나중에 내 정보에서 다시 할 수 있어요.";
        public const string IntroStart = "보정 시작";

        // A-12 거치
        public const string MountTitle = "폰을 계기판 위에\n가로로 올려 주세요";
        public const string MountBody = "화면이 보이게 두면 돼요. 보정하는 동안 폰은 만지지 않아도 돼요.";
        public const string MountOk = "올려 뒀어요";
        public const string Cancel = "취소";

        // A-13 진행
        public static string StageLabel(int index, int count) => $"{index + 1} / {count} 단계";
        public static string Instruction(float kmh) => $"러닝머신 속도를\n{Kmh(kmh)}로 맞추고\n{Move(kmh)} 주세요";
        public static string NextSoon(float kmh) => $"곧 {Kmh(kmh)}로 올려요";
        public const string BadgeWaiting = "걸음을 기다리는 중";
        public const string BadgeWalking = "걷는 중";
        public const string BadgeRunning = "달리는 중";

        // A-14 단계 전환(버리는 5초 동안)
        public static string StageDoneTitle(float kmh) => $"{Kmh(kmh)} 완료!";
        public static string StageDoneBody(float next) => $"이제 러닝머신을 {Kmh(next)}로 올려 주세요";
        public static string StartsIn(int sec, int index, int count) => $"{sec}초 뒤 {index + 1} / {count} 단계 재기 시작";

        // 다시(Figma에 없음): 한 번 실패하면 바로 그 단계를 다시
        public const string RetryTitle = "이 단계를 다시 잴게요";
        public static string Reason(CalStageResult r, float kmh) => r switch
        {
            CalStageResult.NoSteps => "걸음이 잡히지 않았어요. 벨트 위에서 걷거나 달려 주세요.",
            CalStageResult.Irregular => "걸음이 고르지 않았어요. 손잡이를 놓고 일정하게 걷거나 달려 주세요.",
            CalStageResult.NotFaster => $"속도가 그대로인 것 같아요. 러닝머신을 {Kmh(kmh)}로 올렸는지 확인해 주세요.",
            CalStageResult.SensorLost => "센서가 잠깐 끊겼어요. 폰을 계기판 위에 그대로 두세요.",
            CalStageResult.TooShort => "잴 시간이 모자랐어요.",
            _ => "",
        };

        // 도움(Figma에 없음): 같은 단계가 연달아 실패
        public const string HelpTitle = "잠깐 확인해 주세요";
        public static string Help(CalStageResult r, float kmh) => r switch
        {
            CalStageResult.NoSteps => "• 폰이 계기판 위에 놓여 있나요? 손에 들면 걸음을 못 잡아요.\n• 옆 발판이 아니라 벨트 위에 있나요?",
            CalStageResult.Irregular => "• 손잡이를 잡지 말고 팔을 자연스럽게 흔들어 주세요.\n• 폰이 흔들리거나 미끄러지지 않게 평평한 곳에 두세요.",
            CalStageResult.NotFaster => $"• 계기판 속도가 {Kmh(kmh)}인지 확인해 주세요.\n• 단계가 바뀔 때마다 속도를 2씩 올려요.",
            CalStageResult.SensorLost => "• 보정하는 동안 앱을 닫거나 화면을 끄지 말아 주세요.",
            _ => "• 폰을 계기판 위에 두고, 안내하는 속도로 맞춰 주세요.",
        };
        public static string HelpAgain(float kmh) => $"{Kmh(kmh)}부터 다시";
        public const string Quit = "그만두기";

        // 멈춤(Figma에 없음)
        public const string StoppedTitle = "걸음이 멈췄어요";
        public static string StoppedBody(float kmh) => $"러닝머신을 {Kmh(kmh)}로 맞추고 다시 {Move(kmh)} 주세요. 잰 단계는 그대로 두고 이어서 할게요.";

        // 그만두기 확인
        public const string QuitTitle = "보정을 그만둘까요?";
        public const string QuitBody = "지금까지 잰 단계는 저장되지 않아요. 다음에 처음부터 다시 해요.";
        public const string KeepGoing = "계속하기";

        // 저장·A-15 완료
        public const string Saving = "보정을 저장하는 중…";
        public const string DoneTitle = "보정 완료!";
        public const string SensorLabel = "센서";
        public const string PhoneSensor = "폰 센서";
        public const string AirPodsOn = "AirPods 연결됨";
        public const string AirPodsOff = "이어폰 없음";
        public const string SavedLocalOnly = "서버에 아직 못 올렸어요. 이 폰에 남겨 두고, 코스에 들어갈 때 다시 올릴게요.";

        // 다시·도움 카드: 곧 올린다는 안내를 듣고 미리 올렸을 수 있을 때(그 단계를 다시 재므로 되돌려야 함)
        public static string RevertSpeed(float kmh) => $"다음 속도로 올렸다면 {Kmh(kmh)}로 되돌려 주세요.";

        // 도움 카드: 속도를 올렸는데도 계속 "그대로"면 앞 단계 점이 잘못 잡힌 것일 수 있음
        public static string HelpBack(float prevKmh) => $"{Kmh(prevKmh)}부터 다시";
        public const string HelpBackNote = "\n• 맞게 올렸는데도 계속 이러면 앞 단계가 잘못 재졌을 수 있어요. 앞 단계부터 다시 재요.";

        // 센서 안 들어옴(Figma에 없음): 걸음을 기다리는데 폰 센서 값이 계속 안 옴
        public const string NoSensorTitle = "폰 센서가 들어오지 않아요";
        public const string NoSensorBody = "앱을 껐다 켜 보세요. 그래도 안 되면 설정 > 개인정보 보호 및 보안 > 동작 및 피트니스에서 Scrun이 켜져 있는지 확인해 주세요.";
        public const string DoneNext = "계속";
    }
}
