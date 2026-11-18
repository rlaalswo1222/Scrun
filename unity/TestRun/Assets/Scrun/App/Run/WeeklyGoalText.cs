using System;
using System.Collections.Generic;

namespace Scrun.App
{
    // 주간 목표 현황(H-07)·변경 시트(H-08) 글자와 계산(계획 0054, BL-30). 화면 없이 EditMode로 확인한다.
    // 집계(설정한 날부터 7일, 인증 완료 + 5분 이상)는 서버가 하고 앱은 받은 값을 그린다
    public static class WeeklyGoalText
    {
        public const int Min = 1, Max = 7, Default = 3; // A-16과 같음(확정 11/06)

        // 화면에 고정된 글자(제목·범례·빈 카드 "아직 주간 목표가 없어요")는 WeeklyStatus.uxml에. 여기는 코드가 바꾸는 글자만
        public const string Change = "목표 변경";
        public const string SetFirst = "목표 정하기";
        public const string SheetTitle = "한 주에 몇 번 달릴까요?";
        public const string SheetNoteChange = "변경한 목표는 이번 주가 끝난 뒤부터 적용돼요.";
        public const string SheetNoteFirst = "목표를 정한 날부터 7일이 한 주예요.";
        public const string Save = "저장";
        public const string Cancel = "취소";
        public const string CurrentLabel = "이번";

        public static bool HasGoal(WeeklyGoal g) => g?.TargetCount != null;

        // 이번 주 카드 "2" + "/ 3회"
        public static string Done(WeeklyGoal g) => (g?.DoneCount ?? 0).ToString();
        public static string Of(WeeklyGoal g) => $"/ {g?.TargetCount ?? 0}회";

        // 목표 수만큼 동그라미, 채운 수(목표를 넘겨도 목표 수까지)
        public static int Dots(WeeklyGoal g) => Math.Max(0, g?.TargetCount ?? 0);
        public static int Filled(WeeklyGoal g) => Math.Min(Math.Max(0, g?.DoneCount ?? 0), Dots(g));

        // 바꾼 목표가 다음 주기부터일 때 카드 아래 한 줄(정할 것 2 가, 임시 문구 상준 확인). 없으면 null
        public static string NextLine(WeeklyGoal g) => g?.NextTargetCount is int n ? $"다음 주부터 주 {n}회" : null;

        public enum BarKind { Achieved, Missed, Current }

        public struct Bar
        {
            public float Height; // 0–1(횟수/목표, 0이어도 조금 보이게)
            public BarKind Kind;
            public string Label;
        }

        public const float MinBar = 0.12f;

        // 최근 8주 막대: 지난 주기(옛 것부터) + 이번. 이름은 1, 2, … , "이번"(Figma H-07)
        public static List<Bar> Bars(WeeklyGoal g)
        {
            var bars = new List<Bar>();
            if (!HasGoal(g)) return bars;
            int i = 1;
            foreach (var c in g.History ?? new List<WeeklyGoalCycle>())
            {
                bars.Add(new Bar
                {
                    Height = Ratio(c.DoneCount, c.TargetCount),
                    Kind = c.DoneCount >= c.TargetCount ? BarKind.Achieved : BarKind.Missed,
                    Label = (i++).ToString(),
                });
            }
            bars.Add(new Bar { Height = Ratio(g.DoneCount, g.TargetCount ?? 0), Kind = BarKind.Current, Label = CurrentLabel });
            return bars;
        }

        static float Ratio(int done, int target) =>
            target <= 0 ? MinBar : Math.Max(MinBar, Math.Min(1f, done / (float)target));

        // 시트를 열 때 고른 값: 예약이 있으면 그 값, 없으면 지금 목표, 처음이면 기본 3
        public static int SheetStart(WeeklyGoal g) => Clamp(g?.NextTargetCount ?? g?.TargetCount ?? Default);

        public static int Clamp(int n) => Math.Max(Min, Math.Min(Max, n));

        public static string SheetNote(WeeklyGoal g) => HasGoal(g) ? SheetNoteChange : SheetNoteFirst;
    }
}
