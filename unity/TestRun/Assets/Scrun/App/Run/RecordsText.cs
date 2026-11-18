using System;
using System.Collections.Generic;
using System.Globalization;

namespace Scrun.App
{
    // 기록 탭(F-01·F-02, 계획 0053) 글자와 계산. 화면 없이 EditMode로 확인한다.
    // 시간대는 폰 시간대(TimeZoneInfo.Local). 서버의 month는 가입 때 보낸 사용자 시간대 기준이라, 폰 시간대를 바꾸지 않았으면 같다(가설)
    public static class RecordsText
    {
        public const string Title = "기록";
        public const string EmptyTitle = "아직 기록이 없어요";
        public const string EmptyBody = "첫 러닝을 마치고 계기판을 인증하면\n여기에 기록이 쌓여요"; // Figma F-02 줄바꿈(끝 글자만 넘어가지 않게)
        public const string EmptyAction = "달리기 시작하기"; // Figma F-02 → C-01
        public const string DetailTitle = "기록 상세";
        public const string GhostButton = "이 기록으로 고스트 달리기";
        public const string AllLabel = "전체";

        // 달 칩(F-01 "11월 · 10월 · 전체"): 이번 달·지난 달·전체. month는 서버 GET /runs?month=YYYY-MM, 전체는 null
        public static List<(string label, string month)> MonthChips(DateTime nowUtc, TimeZoneInfo zone = null)
        {
            var local = Local(nowUtc, zone);
            var thisMonth = new DateTime(local.Year, local.Month, 1);
            var lastMonth = thisMonth.AddMonths(-1); // 1월이면 지난해 12월
            return new List<(string, string)>
            {
                (MonthLabel(thisMonth), MonthParam(thisMonth)),
                (MonthLabel(lastMonth), MonthParam(lastMonth)),
                (AllLabel, null),
            };
        }

        public static string MonthLabel(DateTime month) => month.Month + "월";

        public static string MonthParam(DateTime month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        // 고른 달에 기록이 없을 때(전체가 비면 F-02 빈 화면). month가 null이면 전체
        public static string EmptyMonth(string label, string month) => month == null ? EmptyTitle : $"{label}에는 기록이 없어요";

        // 이번 주 처음(월요일 0시, 폰 시간대)을 UTC로
        public static DateTime WeekStartUtc(DateTime nowUtc, TimeZoneInfo zone = null)
        {
            var z = zone ?? TimeZoneInfo.Local;
            var local = Local(nowUtc, z);
            int back = ((int)local.DayOfWeek + 6) % 7; // 월 0 … 일 6
            var monday = DateTime.SpecifyKind(local.Date.AddDays(-back), DateTimeKind.Unspecified);
            // 자정에 서머타임이 시작되는 곳은 그 0시가 없음 → 그날 처음 있는 시각(ConvertTimeToUtc가 예외를 내지 않게)
            for (int i = 0; i < 4 && z.IsInvalidTime(monday); i++) monday = monday.AddMinutes(30);
            return TimeZoneInfo.ConvertTimeToUtc(monday, z);
        }

        // 이번 주 요약(정할 것 1 가): 횟수·거리(공식 거리 합, 인증 완료 + 미인증 + 대기. 대기는 마감 10분 안의 방금 기록이라 같이 셈)·인증 완료 횟수
        public struct Week
        {
            public int Count;
            public int DistanceM;
            public int Verified;
        }

        public static Week Summarize(IEnumerable<RunSummary> runs, DateTime weekStartUtc)
        {
            var w = new Week();
            if (runs == null) return w;
            foreach (var r in runs)
            {
                if (ToUtc(r.StartedAt) < weekStartUtc) continue;
                w.Count++;
                w.DistanceM += Math.Max(0, r.DistanceM);
                if (r.Verification == VerifyStatus.Verified) w.Verified++;
            }
            return w;
        }

        // 최신순 목록을 받는 중: 받은 마지막 기록이 아직 이번 주 안이고 다음 쪽이 있으면 더 받아야 이번 주를 다 셈
        public static bool WeekNeedsMore(IReadOnlyList<RunSummary> runs, string nextCursor, DateTime weekStartUtc) =>
            nextCursor != null && runs != null && runs.Count > 0 && ToUtc(runs[runs.Count - 1].StartedAt) >= weekStartUtc;

        // 요약 칸 글자(F-01 "3회 · 8.9km · 3")
        public static string Count(int n) => n + "회";
        public static string WeekKm(int meters) => FriendText.KmShort(meters);

        // 카드 아래 줄 "33:02 · 평균 9'25"/km"(G-09 최근 러닝 줄과 같게)
        public static string CardDetail(RunSummary r) => GhostText.Detail(r); // D-03 고스트 줄과 같은 글자(한 곳에서)

        static DateTime Local(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(ToUtc(utc), zone ?? TimeZoneInfo.Local);

        static DateTime ToUtc(DateTime t) =>
            t.Kind == DateTimeKind.Utc ? t : t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);
    }
}
