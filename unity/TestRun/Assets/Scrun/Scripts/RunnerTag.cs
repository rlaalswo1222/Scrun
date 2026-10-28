using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Scrun
{
    // 함께 달리는 러너 표시용 정보(플레이어·페이스 봇·고스트). 이름표와 "근처 러너" 목록이 이 목록을 본다.
    public class RunnerTag : MonoBehaviour
    {
        public enum Kind { Player, Bot, Ghost }

        public string displayName;
        public Color color = Color.white;
        public Kind kind;
        public RunnerController runner;
        public bool crowd;   // 함께 달리는 무리(이름표·목록 없이 길을 채움)

        public static readonly List<RunnerTag> All = new();
        public static RunnerTag Player { get; private set; }

        public bool Visible => runner && runner.isActiveAndEnabled && gameObject.activeInHierarchy;

        void OnEnable()
        {
            All.Add(this);
            if (kind == Kind.Player) Player = this;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (Player == this) Player = null;
        }

        // 순간이동한 플레이어 주변으로 봇을 다시 모음(개발 도구·코스 변경)
        public static void GatherBotsAround(float distance)
        {
            int i = 0, c = 0;
            foreach (var t in All)
            {
                if (t.kind != Kind.Bot || !t.runner) continue;
                // 무리는 가까이(뒤 40m~앞 110m에 고르게), 페이스 봇은 앞뒤로 띄엄띄엄
                if (t.crowd) { c++; t.runner.Teleport(Mathf.Max(0f, distance - 40f + c * 12.5f + Mathf.Sin(c * 7.3f) * 4f)); }
                else { i++; t.runner.Teleport(Mathf.Max(0f, distance + (i % 2 == 0 ? 1 : -1) * (25f + 30f * i))); }
            }
        }
    }
}
