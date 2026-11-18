using System;
using System.Globalization;

namespace Scrun.App
{
    // 마이 탭(H-01·H-04·H-09·H-10·H-11, 계획 0049) 글자. Figma 문구 그대로인 것과 Figma에 없어 임시로 쓴 것(상준 확인)을 나눠 적는다.
    public static class MyText
    {
        // 아직 없는 기능(계획 0049 정할 것 1 나): 메뉴·칸은 보이고 누르면 이 안내
        public const string SoonTitle = "준비 중이에요";                                 // 임시
        public const string SoonBody = "곧 쓸 수 있게 만들고 있어요.";                     // 임시
        public const string Soon = "준비 중";                                          // 메뉴 오른쪽 설명(임시)

        // 러닝복 세트(BL-34 ②, Figma H-04 "[세트 구성: 💡 제안, 팀 확인]"). 단계 = 서버 progress.outfitStage(1–6)
        static readonly string[] Outfits = { "기본 러닝복", "기능성 세트", "스포츠 세트", "퍼포먼스 세트", "레이스 세트", "엘리트 세트" };

        // 레벨 표(H-04)의 각 레벨 시작 거리(km). 서버 GROWTH_LEVEL_M(기본 3·10·25·50·100 km, BL-34 ③ 11/06 확정)과 같게:
        // 서버 임계값을 바꾸면 여기도(지금 레벨·다음 레벨까지는 서버 값을 그대로 씀)
        public static readonly int[] LevelStartKm = { 0, 3, 10, 25, 50, 100 };

        public static string Outfit(int stage) => Outfits[Math.Max(1, Math.Min(Outfits.Length, stage)) - 1];

        public static string Level(int level) => "Lv." + Math.Max(1, level);

        // H-01 "인증 마크 3개"
        public static string Marks(int verifiedCount) => $"인증 마크 {Math.Max(0, verifiedCount)}개";

        // H-01 "다음 레벨까지 5.6km", 마지막 레벨이면 Figma H-05 "러닝복 완성"(임시)
        public static string NextLevel(int? remainingM) =>
            remainingM.HasValue ? "다음 레벨까지 " + Km(remainingM.Value) : "러닝복을 모두 모았어요";

        // 4.4km(누적 거리·남은 거리): 소수 한 자리, 버림(올려 보이면 아직 안 된 레벨이 된 것처럼 보임)
        public static string Km(int meters) =>
            (Math.Floor(Math.Max(0, meters) / 100.0) / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "km";

        public static string Count(int n) => $"{Math.Max(0, n)}회";

        // H-04 줄 "Lv.2 · 기능성 세트 (3km)"
        public static string LevelRow(int level) => $"Lv.{level} · {Outfit(level)} ({LevelStartKm[Math.Max(1, Math.Min(LevelStartKm.Length, level)) - 1]}km)";

        // H-04 위 "누적 4.4km · 다음 레벨까지 5.6km"
        public static string LevelLead(Progress p) =>
            "누적 " + Km(p.XpM) + (p.NextLevelRemainingM.HasValue ? " · 다음 레벨까지 " + Km(p.NextLevelRemainingM.Value) : "");

        // H-01·H-09 "마지막 보정 11.01", H-09 값 "11.01 (토)". 폰 시간대 날짜. 보정이 없으면 "아직 안 했어요"(임시)
        public static string LastCalibration(DateTime? atUtc, TimeZoneInfo tz = null) =>
            atUtc.HasValue ? "마지막 보정 " + Date(atUtc.Value, tz) : "아직 보정하지 않았어요";

        public static string Date(DateTime atUtc, TimeZoneInfo tz = null) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc), tz ?? TimeZoneInfo.Local).ToString("MM.dd", CultureInfo.InvariantCulture);

        public static string DateWithDay(DateTime atUtc, TimeZoneInfo tz = null)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc), tz ?? TimeZoneInfo.Local);
            return local.ToString("MM.dd", CultureInfo.InvariantCulture) + " (" + "일월화수목금토"[(int)local.DayOfWeek] + ")";
        }

        // H-10 연결 "이어폰 (AirPods)": 지금 상태
        public static string Headphones(HeadphoneStatus s, string name) => s switch
        {
            HeadphoneStatus.Connected => (string.IsNullOrEmpty(name) ? "이어폰" : name) + " 연결됨",
            HeadphoneStatus.ConnectedNoMotion => (string.IsNullOrEmpty(name) ? "이어폰" : name) + " 연결됨 · 움직임 센서 없음", // 임시
            _ => "연결 안 됨", // 임시
        };

        // H-11 안내. Figma "키·몸무게를 바꾸면 … 재보정하면 속도 추정이 더 정확해져요"는 지금 사실이 아님(속도 추정은 보정값만 쓰고
        // 키·몸무게는 안 씀, 계획 0049 정할 것 2) → 위 설명은 가입 화면과 같은 문구, 재보정 안내는 H-09 Figma 문구에서(상준 확인)
        public const string RecalibrateTitle = "속도가 맞지 않게 느껴지나요?";              // 임시
        public const string RecalibrateBody = "기기나 이어폰을 바꿨거나 속도가 맞지 않는다고 느껴질 때 다시 보정해요."; // H-09 Figma
    }
}
