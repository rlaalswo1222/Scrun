using System;
using System.Globalization;

namespace Scrun.App
{
    // 달리는 중 구간 알림(BL-12 ⑦, Figma D-24·D-25, 계획 0041). 목표의 절반과 마지막(거리 500m, 시간 3분)에 한 번씩.
    // HUD가 달리는 동안 주기적으로 부르고, 알릴 게 있으면 문구를 돌려준다(없으면 null). 화면·시계와 무관한 순수 판단.
    // 친구 근접 알림(D-26)은 뺐다(11/17 민재: 친구 기능 S7 전, 간격 카드가 이미 보여 줌). 햅틱은 안 씀(BL-12 ⑧)
    //   var m = new RunMoments(goal);
    //   string text = m.Check(distanceM, elapsedSec, nearestPerson);   // "절반 왔어요 · 1.5km"
    public sealed class RunMoments
    {
        public const float LastDistanceM = 500f;  // 거리 목표 "마지막 500m"
        public const double LastTimeSec = 180;    // 시간 목표 "마지막 3분"(500m ≈ 10 km/h로 3분, 가설)
        public const float MentionWithinM = 50f;  // 마지막 알림에 이 안의 가장 가까운 사람을 함께 언급(D-25)

        readonly RunGoal goal;
        bool halfDone, lastDone;

        public RunMoments(RunGoal goal) => this.goal = goal;

        // distanceM: 이번 달리기 거리(m), elapsedSec: 달린 시간(초), nearest: 가장 가까운 사람(이름, 간격 m: 앞 +, 뒤 −), 없으면 null
        public string Check(float distanceM, double elapsedSec, (string name, float gapM)? nearest)
        {
            bool byDistance = goal.Kind == GoalKind.Distance;
            double target = byDistance ? goal.Units * 100.0 : goal.Units * 60.0;
            double done = byDistance ? distanceM : elapsedSec;
            double lastSpan = byDistance ? LastDistanceM : LastTimeSec;
            if (target <= 0) return null;

            if (done >= target) // 목표에 닿음: 남은 알림은 버림(목표 달성 D-19가 맡음)
            {
                halfDone = lastDone = true;
                return null;
            }
            if (!halfDone)
            {
                if (done < target / 2) return null;
                halfDone = true;
                // 짧은 목표는 절반과 마지막이 겹침(1 km면 둘 다 500m) → 마지막은 안 띄움
                if (target - target / 2 <= lastSpan) lastDone = true;
                return "절반 왔어요 · " + (byDistance ? Km(target / 2) : Minutes(target / 2));
            }
            if (lastDone || target - done > lastSpan) return null;
            lastDone = true;
            string text = byDistance ? "마지막 500m" : "마지막 3분";
            if (nearest.HasValue && Math.Abs(nearest.Value.gapM) <= MentionWithinM && !string.IsNullOrEmpty(nearest.Value.name))
            {
                int m = Math.Max(1, (int)Math.Round(Math.Abs(nearest.Value.gapM)));
                text += $" · {nearest.Value.name} 님이 {m}m {(nearest.Value.gapM >= 0 ? "앞" : "뒤")}";
            }
            return text;
        }

        // 1500 → "1.5km", 2000 → "2km", 21100 → "21.1km"
        static string Km(double m) => (Math.Round(m / 100.0) / 10.0).ToString("0.#", CultureInfo.InvariantCulture) + "km";

        // 900초 → "15분", 450초 → "7분 30초"
        static string Minutes(double sec)
        {
            int s = (int)Math.Round(sec);
            return s % 60 == 0 ? $"{s / 60}분" : $"{s / 60}분 {s % 60}초";
        }
    }
}
