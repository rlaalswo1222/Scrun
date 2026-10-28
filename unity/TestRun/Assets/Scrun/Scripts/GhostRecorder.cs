using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Scrun
{
    // 플레이어 기록: 0.5초마다 (시간, 거리) 저장. 1분 넘게 달렸으면 코스를 바꾸거나 끝낼 때 저장 → 다음번 고스트
    public class GhostRecorder : MonoBehaviour
    {
        public RouteTrack route;
        RunnerController runner;
        GhostRecord current;
        float startTime, nextSample;

        void Awake() => runner = GetComponent<RunnerController>();

        void Update()
        {
            if (!route || !runner) return;
            if (current == null || current.route != route.RouteId) { Flush(); Begin(); }
            if (Time.time < nextSample) return;
            nextSample = Time.time + 0.5f;
            if (runner.SpeedKmh < 0.5f && current.t.Count == 0) { startTime = Time.time; return; }   // 출발 전 대기는 빼고
            current.t.Add(Time.time - startTime);
            current.d.Add(runner.Distance);
        }

        void Begin()
        {
            current = new GhostRecord { route = route.RouteId };
            startTime = Time.time;
        }

        void Flush()
        {
            if (current != null && current.t.Count > 120 && current.d[^1] - current.d[0] > 200f)
            {
                // 고스트는 출발선(0m)부터 시작하도록 거리를 옮김
                float d0 = current.d[0];
                for (int i = 0; i < current.d.Count; i++) current.d[i] -= d0;
                current.Save();
            }
        }

        void OnDisable() => Flush();
        void OnApplicationPause(bool pause) { if (pause) Flush(); }
    }
}
