using System;
using System.Collections.Generic;
using System.Globalization;

namespace Scrun.App
{
    public enum GoalKind { Distance, Time }

    // 오늘 달릴 목표(D-01 러닝 설정, BL-8, 계획 0026). 화면 단위로 들고 서버에는 m·초로 보낸다(POST /runs의 goal, 카운트다운 끝, S3).
    // 소수 오차가 없게 정수로 든다: 거리는 0.1 km 단위(30 = 3.0 km), 시간은 분.
    //   var g = RunGoal.Default(GoalKind.Distance).Plus();   // 3.0 → 3.5 km
    //   g.ToRequest()                                         // { type = "DISTANCE", value = 3500 }
    public readonly struct RunGoal : IEquatable<RunGoal>
    {
        // 거리: 0.5 km씩, 0.5에서 42.2 km(풀코스), 퀵 2·3·5 km(Figma D-01). 시간: 5분씩, 5에서 180분, 퀵 20·30·45분(11/11 지환)
        // 서버 값 = Units × ToServer(거리 0.1 km → m 100, 시간 분 → 초 60)
        static readonly Spec DistanceSpec = new Spec(step: 5, min: 5, max: 422, initial: 30, quick: new[] { 20, 30, 50 },
                                                     unit: "km", serverType: "DISTANCE", toServer: 100);
        static readonly Spec TimeSpec = new Spec(step: 5, min: 5, max: 180, initial: 30, quick: new[] { 20, 30, 45 },
                                                 unit: "분", serverType: "TIME", toServer: 60);

        public GoalKind Kind { get; }
        public int Units { get; } // 거리 0.1 km, 시간 분

        public RunGoal(GoalKind kind, int units)
        {
            Kind = kind;
            var s = SpecOf(kind);
            Units = Math.Max(s.Min, Math.Min(s.Max, units));
        }

        public static RunGoal Default(GoalKind kind) => new RunGoal(kind, SpecOf(kind).Initial);
        public static IReadOnlyList<int> QuickUnits(GoalKind kind) => SpecOf(kind).Quick;

        public bool CanMinus => Units > SpecOf(Kind).Min;
        public bool CanPlus => Units < SpecOf(Kind).Max;

        // 칸에 맞춰 한 칸 위·아래(직접 입력한 3.3 km에서 +는 3.5, −는 3.0)
        public RunGoal Plus()
        {
            int step = SpecOf(Kind).Step;
            return new RunGoal(Kind, (Units / step + 1) * step);
        }

        public RunGoal Minus()
        {
            int step = SpecOf(Kind).Step;
            int down = Units % step == 0 ? Units - step : Units / step * step;
            return new RunGoal(Kind, down);
        }

        // 큰 숫자와 단위: "3.0" "km", "30" "분"
        public string ValueText => Kind == GoalKind.Distance
            ? (Units / 10).ToString(CultureInfo.InvariantCulture) + "." + (Units % 10).ToString(CultureInfo.InvariantCulture)
            : Units.ToString(CultureInfo.InvariantCulture);

        public string UnitText => UnitOf(Kind);
        public static string UnitOf(GoalKind kind) => SpecOf(kind).Unit;

        // 퀵 칩 글자: "2km", "30분"
        public static string QuickText(GoalKind kind, int units) => new RunGoal(kind, units).ShortText;
        public string ShortText => Kind == GoalKind.Distance
            ? (Units % 10 == 0 ? (Units / 10).ToString(CultureInfo.InvariantCulture) : ValueText) + "km"
            : ValueText + "분";

        // 서버 POST /runs의 goal(DISTANCE면 m, TIME이면 초)
        public RunGoalBody ToRequest()
        {
            var s = SpecOf(Kind);
            return new RunGoalBody { Type = s.ServerType, Value = Units * s.ToServer };
        }

        // 직접 입력 읽기. 맞으면 goal, 아니면 사람이 읽을 이유(빈칸은 이유 없이 실패 → 확인 버튼만 막음)
        public static bool TryParse(GoalKind kind, string text, out RunGoal goal, out string problem)
        {
            goal = default;
            problem = null;
            var t = (text ?? "").Trim().Replace(',', '.');
            if (t.Length == 0) return false;

            var s = SpecOf(kind);
            int units;
            if (kind == GoalKind.Distance)
            {
                int dot = t.IndexOf('.');
                if (dot >= 0 && t.Length - dot - 1 > 1)
                {
                    problem = "0.1 km 단위까지 입력해 주세요";
                    return false;
                }
                if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var km) || km > 1000m)
                {
                    problem = "숫자로 입력해 주세요";
                    return false;
                }
                units = (int)(km * 10m);
            }
            else if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out units) || units > 100000)
            {
                problem = "분 단위 숫자로 입력해 주세요";
                return false;
            }

            if (units < s.Min || units > s.Max)
            {
                problem = $"{QuickText(kind, s.Min)}에서 {QuickText(kind, s.Max)} 사이로 입력해 주세요";
                return false;
            }
            goal = new RunGoal(kind, units);
            return true;
        }

        public bool Equals(RunGoal other) => Kind == other.Kind && Units == other.Units;
        public override bool Equals(object obj) => obj is RunGoal g && Equals(g);
        public override int GetHashCode() => ((int)Kind * 397) ^ Units;
        public override string ToString() => ShortText;

        static Spec SpecOf(GoalKind kind) => kind == GoalKind.Distance ? DistanceSpec : TimeSpec;

        sealed class Spec
        {
            public readonly int Step, Min, Max, Initial, ToServer;
            public readonly int[] Quick;
            public readonly string Unit, ServerType;

            public Spec(int step, int min, int max, int initial, int[] quick, string unit, string serverType, int toServer)
            {
                Step = step; Min = min; Max = max; Initial = initial; Quick = quick;
                Unit = unit; ServerType = serverType; ToServer = toServer;
            }
        }
    }
}
