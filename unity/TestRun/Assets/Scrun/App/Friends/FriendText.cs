using System;
using System.Globalization;
using System.Text;

namespace Scrun.App
{
    // 친구 화면(G-01–G-11, 계획 0044) 글자. Figma 문구 그대로인 것과 Figma에 없어 임시로 쓴 것(상준 확인)을 나눠 적는다.
    // 시각은 서버가 UTC로 주고, 화면엔 폰 시간대로.
    public static class FriendText
    {
        public const int CodeLength = 8;
        public const int MaxFriends = 50; // BL-27 ⑤ 11/06 확정(서버 FriendPolicy와 같음)
        // 서버 오류 코드(문자열을 곳곳에 쓰지 않게)
        public const string UserNotFound = "USER_NOT_FOUND", FriendNotRunning = "FRIEND_NOT_RUNNING";
        const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // 서버 app/core/friend_code.py와 같음

        // ---- 코드 ----

        // 입력 → 서버에 보낼 코드. 대문자로, 공백·하이픈 뺌(서버도 같은 규칙). 그래도 코드 모양이 아니면
        // 공유 문구를 통째로 붙여 넣은 것으로 보고 그 안의 8글자 낱말 하나를 고른다(둘 이상이거나 없으면 null)
        public static string NormalizeCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            return Compact(raw) ?? FindInText(raw);
        }

        static string Compact(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (char ch in raw)
            {
                if (char.IsWhiteSpace(ch) || ch == '-') continue;
                sb.Append(char.ToUpperInvariant(ch));
            }
            if (sb.Length != CodeLength) return null;
            for (int i = 0; i < sb.Length; i++)
                if (CodeAlphabet.IndexOf(sb[i]) < 0) return null;
            return sb.ToString();
        }

        // 글 안에서 앞뒤가 영문·숫자가 아닌 8글자 영문·숫자 덩어리 중 코드 글자로만 된 것(정확히 하나일 때만)
        static string FindInText(string raw)
        {
            string found = null;
            int i = 0;
            while (i < raw.Length)
            {
                if (!IsAsciiAlnum(raw[i])) { i++; continue; }
                int start = i;
                while (i < raw.Length && IsAsciiAlnum(raw[i])) i++;
                if (i - start != CodeLength) continue;
                var word = raw.Substring(start, CodeLength).ToUpperInvariant();
                bool ok = true;
                foreach (char ch in word) if (CodeAlphabet.IndexOf(ch) < 0) { ok = false; break; }
                if (!ok) continue;
                if (found != null && found != word) return null; // 후보가 둘: 고르지 않음
                found = word;
            }
            return found;
        }

        static bool IsAsciiAlnum(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        // 입력 칸 아래 오류(G-08, Figma "문구는 케이스별 분리"). 서버 오류 코드 → 문구. 모르는 코드면 null(공통 안내창)
        // FRIEND_CODE_NOT_FOUND만 Figma 문구, 나머지는 임시(상준)
        public static string AddError(string code, string who = null)
        {
            switch (code)
            {
                case "FRIEND_CODE_NOT_FOUND": return "존재하지 않는 코드예요. 다시 확인해 주세요.";
                case "SELF_FRIEND_CODE": return "내 친구 코드예요. 친구에게 받은 코드를 입력해 주세요.";
                case "ALREADY_FRIEND": return "이미 친구예요.";
                case "FRIEND_LIMIT_EXCEEDED":
                    return who == "FRIEND"
                        ? $"상대의 친구가 {MaxFriends}명이라 더 추가할 수 없어요."
                        : $"친구는 {MaxFriends}명까지 추가할 수 있어요. 친구를 정리한 뒤 다시 시도해 주세요.";
                case "RATE_LIMITED": return "코드를 여러 번 잘못 입력했어요. 10분 뒤에 다시 시도해 주세요.";
                default: return null;
            }
        }

        // 서버에 보내기 전 칸 검사(틀린 모양을 보내 "틀린 코드 횟수"를 쓰지 않게). 맞으면 null
        public static string InputError(string raw)
        {
            if (NormalizeCode(raw) != null) return null;
            int n = 0;
            foreach (char ch in raw ?? "") if (!char.IsWhiteSpace(ch) && ch != '-') n++;
            return n == CodeLength ? AddError("FRIEND_CODE_NOT_FOUND") : $"{CodeLength}자리 코드를 입력해 주세요.";
        }

        public const int InputMax = 120; // 공유 문구를 통째로 붙여 넣어도 잘리지 않게(코드는 NormalizeCode가 골라냄)

        // 공유 시트에 넣는 글(임시 문구, 상준)
        public static string ShareMessage(string code) =>
            $"스크런에서 같이 달려요! 친구 탭 → + → 내 친구 코드 {code} 를 입력해 주세요.";

        // ---- 숫자 ----

        // 3.2km(달리는 중, G-01·G-10). 소수 한 자리, 버림(아직 3.2km를 다 못 뛰었는데 3.3으로 보이지 않게)
        public static string KmShort(int meters) =>
            (Math.Floor(Math.Max(0, meters) / 100.0) / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "km";

        // 3.51km(끝난 러닝, G-01 피드·G-09·결과 E-12). 소수 두 자리, 버림. unit: false면 숫자만(결과 화면 큰 숫자)
        public static string Km(int meters, bool unit = true) =>
            (Math.Floor(Math.Max(0, meters) / 10.0) / 100.0).ToString("0.00", CultureInfo.InvariantCulture) + (unit ? "km" : "");

        // 33:02, 한 시간이 넘으면 1:05:09
        public static string Duration(int seconds)
        {
            seconds = Math.Max(0, seconds);
            int h = seconds / 3600, m = seconds / 60 % 60, s = seconds % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        }

        // 9'25"/km. 모르면 null. unit: false면 9'25"(결과 화면 칸)
        public static string Pace(int? secPerKm, bool unit = true) =>
            secPerKm is int p && p > 0 ? $"{p / 60}'{p % 60:00}\"" + (unit ? "/km" : "") : null;

        // ---- 시각 ----

        // 20분 전·1시간 전·3일 전(G-01 피드·G-10). 일주일이 넘으면 날짜(11.05)
        public static string Ago(DateTime atUtc, DateTime nowUtc, TimeZoneInfo zone = null)
        {
            var d = nowUtc - ToUtc(atUtc);
            if (d < TimeSpan.FromMinutes(1)) return "방금 전";
            if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}분 전";
            if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours}시간 전";
            if (d < TimeSpan.FromDays(7)) return $"{(int)d.TotalDays}일 전";
            return TimeZoneInfo.ConvertTimeFromUtc(ToUtc(atUtc), zone ?? TimeZoneInfo.Local).ToString("MM.dd", CultureInfo.InvariantCulture);
        }

        static readonly string[] Weekdays = { "일", "월", "화", "수", "목", "금", "토" };

        // 11.05 (수) 오후 8:10(G-09 최근 러닝)
        public static string RunDate(DateTime atUtc, TimeZoneInfo zone = null)
        {
            var t = TimeZoneInfo.ConvertTimeFromUtc(ToUtc(atUtc), zone ?? TimeZoneInfo.Local);
            int h12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
            return $"{t:MM.dd} ({Weekdays[(int)t.DayOfWeek]}) {(t.Hour < 12 ? "오전" : "오후")} {h12}:{t.Minute:00}";
        }

        static DateTime ToUtc(DateTime t) =>
            t.Kind == DateTimeKind.Utc ? t : t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);

        // ---- 줄 ----

        // G-01 지금 달리는 친구 줄: "3.2km 달리는 중 · 노을 호수 한 바퀴"
        public static string RunningLine(RunningFriend f) =>
            string.IsNullOrEmpty(f.CourseName) ? $"{KmShort(f.DistanceM)} 달리는 중" : $"{KmShort(f.DistanceM)} 달리는 중 · {f.CourseName}";

        // G-10 관리 줄: 달리는 중이면 거리, 아니면 마지막 러닝 때. 기록이 없으면 임시 문구(상준)
        public static string StatusLine(Friend f, DateTime nowUtc, TimeZoneInfo zone = null)
        {
            if (f.RunningNow) return f.RunningDistanceM is int m ? $"{KmShort(m)} 달리는 중" : "달리는 중";
            if (f.LastRunAt is DateTime at) return Ago(at, nowUtc, zone) + " 러닝";
            return "아직 러닝 기록이 없어요";
        }

        // 남과 비교는 인증 기록만(중간발표 피드백 3순위, 계획 0078): 친구 기록의 거리·시간·페이스는 인증 완료일 때만.
        // 인증 대기·미인증은 카드는 보이고 숫자 자리에 이 문구(임시, 상준 확인). 서버는 숫자를 그대로 줌(옛 앱이 null을 못 읽음)
        public const string HiddenStats = "인증한 러닝만 기록이 보여요";
        public const string HiddenKm = "기록 비공개";
        public static bool ShowsStats(FriendRun r) => RunResultText.IsVerified(r.Verification);

        // G-01 피드 카드 아래 "3.51km  ·  33:02"
        public static string RunStats(FriendRun r) =>
            ShowsStats(r) ? $"{Km(r.DistanceM)}  ·  {Duration(r.DurationS)}" : HiddenStats;

        // G-09 최근 러닝 큰 글자 "3.51km"
        public static string RecordKm(FriendRun r) => ShowsStats(r) ? Km(r.DistanceM) : HiddenKm;

        // G-09 최근 러닝 줄 둘째 줄 "33:02 · 평균 9'25"/km"
        public static string RunDetail(FriendRun r)
        {
            if (!ShowsStats(r)) return HiddenStats;
            var pace = Pace(r.AvgPaceSPerKm);
            return pace == null ? Duration(r.DurationS) : $"{Duration(r.DurationS)} · 평균 {pace}";
        }

        // 인증 칩(G-01·G-09): (글자, 완료인지). 인증 대기(PENDING)는 칩 없음(null)
        public static (string text, bool verified)? VerificationChip(string verification)
        {
            switch (verification)
            {
                case "VERIFIED": return ("인증 완료", true);
                case "UNVERIFIED": return ("미인증", false);
                default: return null;
            }
        }

        // 같이 달리기 달리기 시작 안내(HUD 3초, 계획 0047). Figma D-20·D-21 문구 확인 전 임시(상준 확인)
        public static string StartBehindFriend(string nickname) => $"{nickname} 님 바로 뒤에서 시작해요";
        public static string FriendFinished(string nickname) => $"{nickname} 님이 러닝을 마쳐서 혼자 시작해요";
        public static string CannotRunTogether(string nickname) => $"{nickname} 님과 같이 달릴 수 없어서 혼자 시작해요"; // 그새 친구가 아니게 됨

        // G-03 같이 달리기 시트 둘째 줄 "노을 호수 한 바퀴 · 3.2km · 5'50"/km"
        public static string RunTogetherLine(RunningFriend f)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(f.CourseName)) parts.Add(f.CourseName);
            parts.Add(KmShort(f.DistanceM));
            var pace = Pace(f.PaceSPerKm);
            if (pace != null) parts.Add(pace);
            return string.Join(" · ", parts);
        }
    }
}
