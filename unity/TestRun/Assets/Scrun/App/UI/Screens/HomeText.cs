using System;
using System.Collections.Generic;
using System.Linq;

namespace Scrun.App
{
    // 홈 B-01–B-04(계획 0057, Figma 601:321·417·497·587) 상태 고르기와 글자. 화면 없이 EditMode로 확인한다.
    // Figma 문구 그대로인 것과 Figma에 없어 임시로 쓴 것(상준 확인)을 나눠 적는다.
    public static class HomeText
    {
        public enum State
        {
            First,    // B-04 끝낸 러닝이 하나도 없음(미인증 포함, 정할 것 4 가)
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

        // ---- Figma 그대로 ----
        public const string Logo = "SCRUN";
        public const string WeeklyKicker = "이번 주 목표";
        public const string FirstKicker = "첫 러닝";
        public const string FirstTitle = "첫 러닝을\n시작해 볼까요?";
        public const string FirstSub = "끝나면 기록과 인증이 쌓여요";
        public const string NoGoalTitle = "이번 주 몇 번\n달려 볼까요?";
        public const string NoGoalSub = "주 1~7회 중에서 골라요";
        public const string SetGoal = "목표 정하기";
        public const string DoneLine = "목표 달성! 이번 주도 꾸준했어요";
        public const string StartFirst = "첫 러닝 시작하기";
        public const string Continue = "이어서 달리기";
        public const string CoursesTitle = "오늘 달릴 코스";
        public const string SeeAll = "전체 보기";
        public const string FriendsTitle = "지금 달리는 친구";
        public const string RunTogether = "같이 달리기";

        // ---- Figma에 없어 임시(상준 확인) ----
        public const string StartPlain = "달리기 시작하기";                      // 홈을 아직 못 받았을 때 큰 버튼
        public const string NoFriendsTitle = "지금 달리는 친구가 없어요";        // 친구 0명(정할 것 1 가)
        public const string NoFriendsBody = "친구를 추가하면 달리는 친구와 바로 같이 달릴 수 있어요";
        public const string AddFriend = "친구 추가";
        public const string FailTitle = "홈을 불러오지 못했어요";

        public static string Greeting(string nickname) => string.IsNullOrEmpty(nickname) ? "" : nickname + " 님";

        public static string Button(State s) => s == State.First ? StartFirst : Continue;

        // B-01 "한 번만 더 달리면 달성!"(Figma), 두 번 이상 남으면 "2번 더 달리면 달성!"(임시)
        public static string RemainingLine(WeeklyGoal g)
        {
            int left = Math.Max(0, (g?.TargetCount ?? 0) - (g?.DoneCount ?? 0));
            return left <= 1 ? "한 번만 더 달리면 달성!" : $"{left}번 더 달리면 달성!";
        }

        // 레벨 줄 "기능성 세트 · 다음 레벨까지 5.6km"(Figma B는 "기능성 러닝 세트" → 마이 탭 MyText와 같게, 상준에게 하나로)
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

        // 홈 코스 카드는 앞의 두 개(서버가 사람 많은 순으로 줌). 실사 영상 코스는 아직 달릴 수 없어 뺌(C-01과 같음)
        public const int CourseCards = 2;

        public static List<Course> Courses(HomeData h) =>
            CourseListScreen.Filter(h?.Courses, liveOnly: false).Take(CourseCards).ToList();

        // 코스 카드 "6.7km · 12명 달리는 중"(Figma "3km · 12명 달리는 중"). 0명도 같은 꼴(반 칸 카드에서 한 줄에 들어가게)
        public static string CourseMeta(Course c) =>
            CourseText.Km(c.LengthM) + " · " + Math.Max(0, c.RunnersNow) + "명 달리는 중";
    }
}
