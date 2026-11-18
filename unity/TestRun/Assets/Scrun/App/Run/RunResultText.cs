using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Scrun.App
{
    // 운동 결과(E-12·E-14)·인증 후 반영(E-11)·레벨업(E-13)·대조 시트(E-15) 글자와 계산(계획 0051). 화면 없이 테스트.
    // Figma 문구 그대로인 것과 Figma에 없어 임시로 쓴 것(상준 확인)을 나눠 적는다
    public static class RunResultText
    {
        public const string Title = "운동 결과";                       // E-12
        public const string RewardsTitle = "인증 완료";                // E-11 nav
        public const string RewardsHead = "기록이 인증됐어요";          // E-11
        public const string RewardsNext = "운동 결과 보기";             // E-11 버튼
        public const string LevelUp = "LEVEL UP!";                    // E-13
        public const string OutfitUpgraded = "러닝복이 업그레이드됐어요"; // E-13
        public const string Continue = "계속하기";                     // E-13
        public const string CompanionsLabel = "함께 달린 러너";          // E-12
        public const string ShareTitle = "친구 피드에 공유";             // E-12
        public const string CompareLink = "센서·계기판 대조 자세히 보기"; // E-12
        public const string CompareTitle = "센서와 계기판 대조";          // E-15
        public const string Home = "홈으로";
        public const string Records = "기록 보기";
        public const string GraphTitle = "속도 변화";
        // E-14 Figma 그대로
        public const string UnverifiedNote = "경험치·인증 마크·주간 목표에는 반영되지 않아요. 다음 러닝에서 인증하면 반영돼요.";
        // 기록을 서버에 아직 못 올림(오프라인 종료, 정할 것 1 가). 임시
        public const string OfflineNote = "아직 서버에 올리지 못했어요. 연결되면 미인증 기록으로 올라가요.";
        // E-15 판정 문구. 인증 완료는 Figma("[팀 기준]" 자리는 서버 설정이라 빼고), 미인증은 임시
        public const string CompareOk = "허용 오차 이내로 판정했어요. 인증은 조작을 완전히 막는다고 보장하지는 않아요.";
        public const string CompareBad = "허용 오차를 넘어 미인증으로 저장했어요.";

        public static bool IsVerified(string verification) => verification == "VERIFIED";

        // 인증 칩: 인증 완료·미인증, 인증 대기(마무리를 서버에 아직 못 알림)는 "인증 대기"(임시)
        public static (string text, bool verified) Chip(string verification) =>
            FriendText.VerificationChip(verification) ?? ("인증 대기", false);

        // 칩 주황(경고)은 미인증만. 인증 대기는 회색(아직 결과가 아님). 결과·기록 상세·기록 목록이 같이 씀(계획 0053 리뷰)
        public static bool ChipWarn(string verification) => verification == VerifyStatus.Unverified;

        // 큰 숫자 "3.02"(기록 목록·피드와 같은 반올림)
        public static string BigKm(int meters) => FriendText.Km(meters, unit: false);

        // 단위: 인증 완료면 "km", 아니면 E-14 "km · 센서 값"
        public static string Unit(string verification) => IsVerified(verification) ? "km" : "km · 센서 값";

        // 목표 칸: "3.0km"·"30분", 목표 없음은 "없음"(임시)
        public static string Goal(RunGoalBody g)
        {
            if (g?.Value == null || g.Type == "NONE") return "없음";
            int v = g.Value.Value;
            return g.Type == "TIME"
                ? (v / 60).ToString(CultureInfo.InvariantCulture) + "분"
                : (Math.Round(v / 100.0) / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "km";
        }

        // 평균 페이스 칸 "9'30\"", 모르면 "-"
        public static string Pace(int? secPerKm) => FriendText.Pace(secPerKm, unit: false) ?? "-";

        // E-12 "비락식혜 외 2명": 친구 먼저, 이름 없는 봇은 세기만. 아무도 없으면 null(줄 숨김)
        public static string CompanionLine(IReadOnlyList<Companion> all)
        {
            if (all == null || all.Count == 0) return null;
            var first = Ordered(all).FirstOrDefault(c => !string.IsNullOrEmpty(c.Nickname));
            if (first == null) return $"{all.Count}명";
            return all.Count == 1 ? first.Nickname : $"{first.Nickname} 외 {all.Count - 1}명";
        }

        // 머리 그림 순서(친구 → 러너 → 봇)
        public static IEnumerable<Companion> Ordered(IEnumerable<Companion> all) =>
            (all ?? Enumerable.Empty<Companion>()).OrderBy(c => c.Kind == "FRIEND" ? 0 : c.Kind == "RUNNER" ? 1 : 2);

        // E-12 공유 줄 설명(인증 완료 Figma E-12, 미인증 Figma E-14)
        public static string ShareSub(string verification) =>
            IsVerified(verification) ? "인증 마크와 함께 공유돼요" : "미인증 마크와 함께 공유돼요";

        // ---- E-11 인증 후 반영 ----

        // "3 / 3회 달성", 아직이면 "2 / 3회", 주간 목표가 없으면 null(카드 숨김)
        public static string Weekly(WeeklyGoalReflect w)
        {
            if (w?.TargetCount == null) return null;
            int t = w.TargetCount.Value;
            return w.DoneCount >= t ? $"{t} / {t}회 달성" : $"{w.DoneCount} / {t}회";
        }

        // "Lv.1 → Lv.2 레벨업!", 그대로면 "Lv.2"
        public static string LevelLine(RunRewards r) =>
            r.LeveledUp ? $"{MyText.Level(r.LevelBefore)} → {MyText.Level(r.LevelAfter)} 레벨업!" : MyText.Level(r.LevelAfter);

        // "경험치 +3.02km"
        public static string XpGain(RunRewards r) => "경험치 +" + FriendText.Km(Math.Max(0, r.XpAfterM - r.XpBeforeM));

        // "다음 Lv.3까지 7.0km", 마지막 레벨이면 H-04와 같은 문구
        public static string NextLevel(RunRewards r) =>
            r.NextLevelRemainingM is int m ? $"다음 {MyText.Level(r.LevelAfter + 1)}까지 {MyText.KmLeft(m)}" : MyText.NextLevel(null);

        // 경험치 바(그 레벨 시작 → 다음 레벨 시작 사이 비율 0–1). 레벨 경계는 서버가 준 값(progress.levelStartsM, 서버 설정),
        // 못 받았으면 H-04 표와 같은 값(MyText.LevelStartKm, 서버 기본값)
        public static float Bar(int xpM, int level, IReadOnlyList<int> startsM = null)
        {
            var starts = startsM != null && startsM.Count >= 2 ? startsM : MyText.LevelStartKm.Select(k => k * 1000).ToList();
            int lv = Math.Max(1, Math.Min(starts.Count, level));
            if (lv >= starts.Count) return 1f;
            float from = starts[lv - 1], to = starts[lv];
            return to <= from ? 1f : Math.Max(0f, Math.Min(1f, (xpM - from) / (to - from)));
        }

        // 바 연출 구간(E-11 노트 "또로로 차오름"): 레벨업이면 지난 레벨 칸을 끝까지 → E-13, 아니면 이번 레벨 안에서 앞 → 뒤
        public static (float from, float to) BarRange(RunRewards r, IReadOnlyList<int> startsM = null) =>
            r.LeveledUp ? (Bar(r.XpBeforeM, r.LevelBefore, startsM), 1f)
                        : (Bar(r.XpBeforeM, r.LevelAfter, startsM), Bar(r.XpAfterM, r.LevelAfter, startsM));

        // 인증 마크 "+1 · 총 3회"
        public static string Marks(int verifiedCount) => $"+1 · 총 {Math.Max(1, verifiedCount)}회";

        // "다음 임팩트까지 인증 2회", 다 열었으면 null
        public static string NextImpact(Progress p)
        {
            var next = p?.Impacts?.Where(i => !i.Unlocked).OrderBy(i => i.UnlockAtCount).FirstOrDefault();
            if (next == null) return null;
            return $"다음 임팩트까지 인증 {Math.Max(1, next.UnlockAtCount - p.VerifiedCount)}회";
        }

        // "새 임팩트 해금: 스파크 트레일"(Figma "첫 임팩트 해금"은 첫 번째일 때만이라 "새"로, 임시)
        public static string ImpactUnlocked(Impact i) => "새 임팩트 해금: " + i.Name;

        // ---- E-15 대조 ----

        // (항목, 센서, 계기판, 차이). 계기판 값이 없으면 빈 목록
        public static List<(string item, string sensor, string console, string diff)> Compare(RunStatsBody sensor, RunStatsBody console)
        {
            var rows = new List<(string, string, string, string)>();
            if (sensor == null || console == null) return rows;
            rows.Add(("거리", FriendText.Km(sensor.DistanceM), FriendText.Km(console.DistanceM), FriendText.Km(Math.Abs(sensor.DistanceM - console.DistanceM))));
            rows.Add(("시간", FriendText.Duration(sensor.DurationS), FriendText.Duration(console.DurationS), Seconds(Math.Abs(sensor.DurationS - console.DurationS))));
            return rows;
        }

        // "1초", "2분 5초"
        public static string Seconds(int s) => s < 60 ? $"{s}초" : s % 60 == 0 ? $"{s / 60}분" : $"{s / 60}분 {s % 60}초";

        // ---- 그래프 ----

        // 그래프 점이 너무 많으면 평균으로 줄임(서버가 이미 120칸 안으로 줄여 주지만, 오프라인 기록은 1초 칸 그대로)
        public static List<float> Downsample(IReadOnlyList<float> speeds, int maxPoints)
        {
            var out_ = new List<float>();
            if (speeds == null || speeds.Count == 0 || maxPoints <= 0) return out_;
            if (speeds.Count <= maxPoints)
            {
                out_.AddRange(speeds);
                return out_;
            }
            double per = (double)speeds.Count / maxPoints;
            for (int k = 0; k < maxPoints; k++)
            {
                int a = (int)(k * per), b = Math.Max(a + 1, (int)((k + 1) * per));
                float sum = 0f;
                for (int i = a; i < b && i < speeds.Count; i++) sum += speeds[i];
                out_.Add(sum / (b - a));
            }
            return out_;
        }
    }
}
