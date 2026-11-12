using System;

namespace Scrun.App
{
    // 이번 달리기의 시간·거리·목표 진행(BL-12 ②, 계획 0029). 카운트다운이 끝날 때 Start(지환 BL-11), 그 전엔 0.
    // 거리는 섬 아바타의 누적 거리(RunLink.PlayerDistanceM) 차이. 일시정지·종료·서버 기록(POST /runs)은 S5.
    public sealed class RunSession
    {
        readonly Func<double> clock;
        double startedAt;
        float startDistance;

        public RunSession(RunGoal goal, Func<double> clock = null)
        {
            Goal = goal;
            this.clock = clock ?? RealtimeClock.Now;
        }

        public RunGoal Goal { get; }
        public bool Started { get; private set; }

        public void Start(float playerDistanceM)
        {
            if (Started) return;
            Started = true;
            startedAt = clock();
            startDistance = playerDistanceM;
        }

        public double ElapsedSec => Started ? Math.Max(0, clock() - startedAt) : 0;

        public float DistanceM(float playerDistanceM) => Started ? Math.Max(0f, playerDistanceM - startDistance) : 0f;

        // 0에서 1. 거리 목표는 m, 시간 목표는 초로 비교
        public float Progress(float playerDistanceM)
        {
            double done, target;
            if (Goal.Kind == GoalKind.Distance)
            {
                done = DistanceM(playerDistanceM);
                target = Goal.Units * 100.0; // 0.1 km → m
            }
            else
            {
                done = ElapsedSec;
                target = Goal.Units * 60.0;  // 분 → 초
            }
            return target <= 0 ? 0f : (float)Math.Min(1.0, done / target);
        }

        // HUD 글자: 거리 "1.24", 시간 "12:05"(한 시간 넘으면 "1:02:05")
        public static string DistanceText(float m) =>
            (Math.Floor(m / 10.0) / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        public static string TimeText(double sec)
        {
            long s = (long)Math.Floor(Math.Max(0, sec));
            long h = s / 3600, mm = s % 3600 / 60, ss = s % 60;
            return h > 0 ? $"{h}:{mm:00}:{ss:00}" : $"{mm:00}:{ss:00}";
        }
    }
}
