using System;
using System.Collections.Generic;

namespace Scrun.App
{
    // 고리 코스(한 바퀴 lap m) 위 거리 계산. 위치는 코스 시작점부터 [0, lap).
    public static class LoopMath
    {
        public static float Wrap(float d, float lap)
        {
            if (lap <= 0f) return 0f;
            float r = d % lap;
            return r < 0f ? r + lap : r;
        }

        // from에서 to까지 가장 짧은 방향의 거리. 앞(+)이면 (0, lap/2], 뒤(−)면 (−lap/2, 0)
        public static float Delta(float from, float to, float lap)
        {
            if (lap <= 0f) return 0f;
            float d = Wrap(to - from, lap);
            return d > lap * 0.5f ? d - lap : d;
        }

        public readonly struct Neighbor
        {
            public readonly string Name;
            public readonly float GapM; // 늘 0 이상(앞이면 앞으로, 뒤면 뒤로 이만큼)

            public Neighbor(string name, float gapM)
            {
                Name = name;
                GapM = gapM;
            }
        }

        // 내 앞·뒤로 가장 가까운 러너(BL-12 ④ 간격 카드). 고리라 반 바퀴까지가 앞, 넘으면 뒤. 아무도 없으면 null
        public static (Neighbor? ahead, Neighbor? behind) Nearest(float me, IEnumerable<(string name, float d)> others, float lap)
        {
            Neighbor? ahead = null, behind = null;
            foreach (var (name, d) in others)
            {
                float delta = Delta(me, d, lap);
                if (delta >= 0f)
                {
                    if (ahead == null || delta < ahead.Value.GapM) ahead = new Neighbor(name, delta);
                }
                else if (behind == null || -delta < behind.Value.GapM) behind = new Neighbor(name, -delta);
            }
            return (ahead, behind);
        }

        // 바퀴로 접지 않은 진짜 거리 차(delta, 앞 +)를 아는 러너를 앞·뒤 가장 가까운 자리에 넣음. 고스트(계획 0050)는 같은 출발선이라
        // 반 바퀴 넘게 앞서도 앞(배너와 같은 방향·크기)
        public static (Neighbor? ahead, Neighbor? behind) WithExact((Neighbor? ahead, Neighbor? behind) n, string name, float delta)
        {
            var (ahead, behind) = n;
            if (delta >= 0f)
            {
                if (ahead == null || delta < ahead.Value.GapM) ahead = new Neighbor(name, delta);
            }
            else if (behind == null || -delta < behind.Value.GapM) behind = new Neighbor(name, -delta);
            return (ahead, behind);
        }

        // 간격 글자: "12m 앞", "1.2km 뒤"
        public static string GapText(float gapM, bool ahead)
        {
            string side = ahead ? "앞" : "뒤";
            if (gapM < 1000f) return $"{(int)Math.Round(gapM)}m {side}";
            return (gapM / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + $"km {side}";
        }
    }
}
