using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Scrun
{
    // 고스트: 이 코스에서 지난번 내 달리기 기록을 그대로 다시 재생(거리를 직접 맞춤)
    public class GhostSpeed : MonoBehaviour, ISpeedSource
    {
        public RouteTrack route;
        RunnerController runner;
        GhostRecord record;
        string loadedRoute;
        float startTime;

        public float SpeedKmh { get; private set; }
        public bool HasRecord => record != null && record.t.Count > 1;

        void Awake() => runner = GetComponent<RunnerController>();

        void Update()
        {
            if (!route) return;
            if (loadedRoute != route.RouteId)
            {
                loadedRoute = route.RouteId;
                record = GhostRecord.Load(loadedRoute);
                startTime = Time.time;
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = HasRecord;
            }
            if (!HasRecord) { SpeedKmh = 0f; return; }
            float t = Time.time - startTime;
            float d = record.DistanceAt(t, out float speed);
            SpeedKmh = speed * 3.6f;
            runner.Teleport(d);
        }
    }

    [Serializable]
    public class GhostRecord
    {
        public string route;
        public List<float> t = new();   // 초
        public List<float> d = new();   // m

        static string PathFor(string route) => Path.Combine(Application.persistentDataPath, $"ghost_{route}.json");

        public static GhostRecord Load(string route)
        {
            try
            {
                var p = PathFor(route);
                return File.Exists(p) ? JsonUtility.FromJson<GhostRecord>(File.ReadAllText(p)) : null;
            }
            catch (Exception e) { Debug.LogWarning($"[Scrun] 고스트 기록 읽기 실패: {e.Message}"); return null; }
        }

        public void Save()
        {
            try { File.WriteAllText(PathFor(route), JsonUtility.ToJson(this)); }
            catch (Exception e) { Debug.LogWarning($"[Scrun] 고스트 기록 저장 실패: {e.Message}"); }
        }

        public float DistanceAt(float time, out float speed)
        {
            int n = t.Count;
            if (time >= t[n - 1]) { speed = 0f; return d[n - 1]; }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (t[mid] <= time) lo = mid; else hi = mid; }
            float dt = Mathf.Max(1e-3f, t[hi] - t[lo]);
            speed = (d[hi] - d[lo]) / dt;
            return Mathf.Lerp(d[lo], d[hi], (time - t[lo]) / dt);
        }
    }
}
