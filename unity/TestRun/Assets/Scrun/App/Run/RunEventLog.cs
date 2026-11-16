using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Scrun.App
{
    // 달리는 중 측정 예외(끊김·이상·발열) 기록(계획 0039 4번). 헬스장 뒤 문턱(25 km/h, 2초 5 km/h, 배터리 15%) 조정 근거.
    // 콘솔엔 늘, 개발 빌드(아이폰)는 Documents/run-log/measure-events.csv에도(0034 avatar-delays.csv 옆, 맥에서 devicectl로 받음)
    public static class RunEventLog
    {
        static string runId = "";

        // 달리기 하나 시작(줄마다 이 ID를 붙여 어느 달리기인지 가름)
        public static void Begin(string clientRunId) => runId = clientRunId ?? "";

        // kind: 측정끊김·측정복구·다시측정·에어팟끊김·측정이상시작·측정이상끝·발열배터리. t: 달린 시간(초)
        public static void Write(string kind, double t, string detail)
        {
            Debug.Log($"[측정] {kind} {t:0.0}초 {detail}");
            if (Application.isEditor || !Debug.isDebugBuild) return;
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "run-log");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "measure-events.csv");
                if (!File.Exists(file)) File.WriteAllText(file, "time,run,kind,run_sec,detail\n");
                string line = string.Join(",",
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), runId, kind,
                    t.ToString("0.0", CultureInfo.InvariantCulture), (detail ?? "").Replace(',', ' '));
                File.AppendAllText(file, line + "\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[측정] 기록을 못 씀: " + e.Message);
            }
        }
    }
}
