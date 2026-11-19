using System;
using System.Collections.Generic;
using System.Linq;

namespace Scrun.App
{
    // 홈 B-01–B-04(계획 0057) 상태 고르기와 글자. 문구는 우주 시안 B · 홈(825:571, 계획 0062). 화면 없이 EditMode로 확인한다.
    // 시안 문구 그대로인 것과 시안에 없어 임시로 쓴 것(상준 확인)을 나눠 적는다.
    public static class HomeText
    {
        public enum State
        {
            First,    // B-04 끝낸 러닝이 하나도 없음(미인증 포함, 계획 0057 정할 것 4 가)
            NoGoal,   // B-02 기록은 있고 주간 목표 없음
            Progress, // B-01 목표를 덜 채움
            Done,     // B-03 채운 횟수 ≥ 목표
        }

        public static State Pick(HomeData h)
        {
            if (h?.RecentRuns == null || h.RecentRuns.Count == 0) return State.First;
            if (!WeeklyGoalText.HasGoal(h.WeeklyGoal)) return State.NoGoal;
            return h.WeeklyGoal.DoneCount >= h.WeeklyGoal.TargetCount ? State.Done : State.Progress;
        }

        // ---- 시안 그대로 ----
        public const string FirstTitle = "첫 러닝을 시작해 볼까요?";
        public const string FirstSub = "끝나면 기록과 인증이 쌓여요";
        public const string NoGoalTitle = "목표가 없어도\n달릴 수 있어요";
        public const string NoGoalSub = "주 1–7회 중에서 골라요";
        public const string DoneTitle = "이번 주 목표 달성!\n꾸준했어요";
        public const string SetGoal = "이번 주 목표 정하기";
        public const string Start = "달리기 시작";
        public const string FriendsTitle = "지금 달리는 친구";
        public const string NoFriendsSection = "친구";
        public const string NoFriendsTitle = "친구와 같이 달려 볼까요?";
        public const string NoFriendsBody = "친구 추가";

        // ---- 시안에 없어 임시(상준 확인) ----
        public const string FailTitle = "홈을 불러오지 못했어요";

        public static string Title(State s, WeeklyGoal g) => s switch
        {
            State.First => FirstTitle,
            State.NoGoal => NoGoalTitle,
            State.Done => DoneTitle,
            _ => ProgressTitle(g),
        };

        // 시안은 "한 번만 더 달리면 / 이번 주 목표를 채워요", 두 번 이상 남으면 "N번 더 달리면"(임시)
        public static string ProgressTitle(WeeklyGoal g)
        {
            int left = Math.Max(0, (g?.TargetCount ?? 0) - (g?.DoneCount ?? 0));
            return (left <= 1 ? "한 번만 더 달리면" : $"{left}번 더 달리면") + "\n이번 주 목표를 채워요";
        }

        public static string Sub(State s) => s switch
        {
            State.First => FirstSub,
            State.NoGoal => NoGoalSub,
            _ => "",
        };

        // 위 카드 "이번 주 2/3 회"
        public static string Count(WeeklyGoal g) => $"{WeeklyGoalText.Done(g)}/{g?.TargetCount ?? 0}";

        // 고리 채움 0–1
        public static float Ring(WeeklyGoal g)
        {
            int target = g?.TargetCount ?? 0;
            return target <= 0 ? 0f : Math.Min(1f, (g?.DoneCount ?? 0) / (float)target);
        }

        // 친구 줄 "지민 · 하준"(앞 두 명, 셋 이상이면 "지민 · 하준 외 1명"은 임시), "2명 달리는 중"
        public static string FriendNames(IReadOnlyList<RunningFriend> list)
        {
            var named = (list ?? new List<RunningFriend>()).Where(f => f != null && !string.IsNullOrEmpty(f.Nickname)).ToList();
            if (named.Count == 0) return "";
            var names = string.Join(" · ", named.Take(2).Select(f => f.Nickname));
            return named.Count > 2 ? $"{names} 외 {named.Count - 2}명" : names;
        }

        public static string FriendCount(int n) => $"{Math.Max(0, n)}명 달리는 중";

        // 프로필 카드 "기능성 세트 · 다음 레벨까지 5.6km"(시안 "기능성 러닝 세트" → 마이 탭 MyText와 같게, 상준에게 하나로)
        public static string LevelLine(Progress p) =>
            p == null ? "" : MyText.Outfit(p.OutfitStage) + " · " + MyText.NextLevel(p.NextLevelRemainingM);

        // 레벨 막대 0–1: 이번 레벨 시작부터 다음 레벨까지 중 얼마나. 마지막 레벨이면 가득
        public static float LevelFill(Progress p)
        {
            if (p == null) return 0f;
            if (p.NextLevelAtM is not int next) return 1f;
            int start = LevelStart(p);
            if (next <= start) return 1f;
            return Math.Max(0f, Math.Min(1f, (p.XpM - start) / (float)(next - start)));
        }

        static int LevelStart(Progress p)
        {
            var starts = p.LevelStartsM;
            int i = p.Level - 1;
            if (starts != null && i >= 0 && i < starts.Count) return starts[i];
            i = Math.Max(0, Math.Min(MyText.LevelStartKm.Length - 1, i));
            return MyText.LevelStartKm[i] * 1000;
        }
    }
}
