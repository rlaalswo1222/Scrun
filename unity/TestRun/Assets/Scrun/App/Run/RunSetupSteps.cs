namespace Scrun.App
{
    public enum SetupStep
    {
        Goal,   // D-01 러닝 설정
        Mount,  // D-05 거치 안내(매번)
        Manual, // D-06 달리는 매뉴얼("다시 보지 않기"면 건너뜀)
        Done,   // 맵으로(S3: D-07 센서 준비)
        Exit,   // D-01에서 뒤로 → 맵 나가기
    }

    // 맵 입장 뒤 안내 순서(계획 0026, Figma D-01 → D-05 → D-06 → D-07). 화면 없이 순서만 → 테스트
    public sealed class RunSetupSteps
    {
        readonly bool skipManual;

        public RunSetupSteps(bool skipManual) => this.skipManual = skipManual;

        public SetupStep Current { get; private set; } = SetupStep.Goal;

        public SetupStep Next()
        {
            switch (Current)
            {
                case SetupStep.Goal: Current = SetupStep.Mount; break;
                case SetupStep.Mount: Current = skipManual ? SetupStep.Done : SetupStep.Manual; break;
                case SetupStep.Manual: Current = SetupStep.Done; break;
            }
            return Current;
        }

        public SetupStep Back()
        {
            switch (Current)
            {
                case SetupStep.Goal: Current = SetupStep.Exit; break;
                case SetupStep.Mount: Current = SetupStep.Goal; break;
                case SetupStep.Manual: Current = SetupStep.Mount; break;
            }
            return Current;
        }
    }

    // 러닝 설정에서 고른 것. S3 센서 준비·카운트다운이 받아 POST /runs에 쓴다(고스트는 S7 BL-24)
    public sealed class RunSetup
    {
        public RunGoal Goal { get; }

        public RunSetup(RunGoal goal) => Goal = goal;
    }

    public static class RunSetupText
    {
        // "이번 주 목표까지 N회 남았어요". 목표가 없거나 다 채웠으면 null(그 줄을 숨김)
        public static string WeeklyLeft(WeeklyGoal g)
        {
            if (g?.TargetCount == null) return null;
            int left = g.TargetCount.Value - g.DoneCount;
            return left > 0 ? $"이번 주 목표까지 {left}회 남았어요" : null;
        }

        // D-01 코스 칩: "호숫가 코스 · 지금 12명"
        public static string CourseChip(string name, int runnersNow) => $"{name} · 지금 {runnersNow}명";

        public static string InputTitle(GoalKind kind) => kind == GoalKind.Distance ? "목표 거리" : "목표 시간";
    }
}
