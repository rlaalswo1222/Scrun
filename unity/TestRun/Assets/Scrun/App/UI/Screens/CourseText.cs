using System.Globalization;

namespace Scrun.App
{
    // 코스 화면 문구(Figma C-01·C-02·I-11 기준, 상준 문구 확정 전까지 임시). 화면 코드와 떼어 테스트한다.
    public static class CourseText
    {
        // 서버 오류 코드(계획 0021, PR #30 feat/course-api)
        public const string CalibrationRequired = "CALIBRATION_REQUIRED";
        public const string CourseFull = "COURSE_FULL";
        public const string CourseNotFound = "COURSE_NOT_FOUND";

        // 실사 영상 코스(자리만, 아직 달릴 수 없음) → 목록에서 뺀다
        public const string VideoType = "VIDEO";

        // 6671 → "6.7km"
        public static string Km(int meters) =>
            (meters / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + "km";

        // 코스 카드·맵 윗줄: "한 바퀴 6.7km"
        public static string Lap(int meters) => "한 바퀴 " + Km(meters);

        // 맵 윗줄(D-10 자리): "한 바퀴 6.7km · 호수 한 바퀴"
        public static string MapLabel(int meters, string name) => $"{Lap(meters)} · {name}";

        // 지금 달리는 사람 칩: "지금 3명 달리는 중"
        public static string RunningNow(int count) =>
            count <= 0 ? "지금 달리는 사람 없음" : $"지금 {count}명 달리는 중";

        // 상세의 "달리는 러너" 칸: "3명"
        public static string PeopleCount(int count) => $"{(count < 0 ? 0 : count)}명";

        public static string Empty(bool liveOnly) =>
            liveOnly ? "지금 달리는 사람이 있는 코스가 없어요" : "지금 달릴 수 있는 코스가 없어요";

        // 입장·상세 실패 중 화면이 직접 안내하는 것(제목, 본문). 나머지(네트워크 등)는 null → 공통 안내(Feedback)
        public static (string title, string body)? Failure(string code)
        {
            switch (code)
            {
                case CalibrationRequired:
                    return ("속도 보정이 먼저 필요해요", "처음 한 번 러닝머신에서 속도를 맞춰야 맵에 들어갈 수 있어요.");
                case CourseFull: // I-11
                    return ("지금은 이 코스가 가득 찼어요", "잠시 후 다시 입장하거나 다른 코스를 선택해 주세요.");
                case CourseNotFound:
                    return ("코스를 찾을 수 없어요", "지금은 달릴 수 없는 코스예요. 다른 코스를 선택해 주세요.");
                default:
                    return null;
            }
        }
    }
}
