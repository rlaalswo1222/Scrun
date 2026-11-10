using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 개발 빌드 전용 점검 화면: 권한 4종 요청·설정 앱 열기, 이어폰 연결 상태, 키체인 저장·읽기·지우기,
    // 속도 센서(흐름·초당 샘플·걸음 상태·속도·누적 걸음, 걸음이 잡히기까지 걸린 초, 단계 하나 재기)를 눌러 본다(계획 0024 5번).
    // 로그인 화면 아래 "네이티브 점검" 버튼(개발 빌드에서만 보임)으로 들어온다. 계획 0018의 5번 확인용,
    // 지환의 권한·에어팟 화면이 나오면 그 화면으로 같은 것을 확인하고 이 화면은 개발 도구로만 남긴다.
    public sealed class NativeCheckScreen : AppScreen
    {
        protected override string Uxml => "UI/NativeCheck";
        public override bool HidesTabBar => true;

        readonly Dictionary<PermissionKind, Label> statusLabels = new Dictionary<PermissionKind, Label>();
        Label headphones, log;
        readonly List<string> lines = new List<string>();

        // 속도 센서
        Label speed;
        Button speedToggle, speedStage;
        ISpeedSensor sensor;        // 이 화면이 켠 센서(끌 때·화면을 떠날 때 끔)
        bool ownCalibration;        // 단계 재기를 위해 이 화면이 연 보정(끝나면 버림, 지금 보정은 그대로)
        IVisualElementScheduledItem stageTimer;
        int lastSteps;
        double firstStepAt = -1;    // 걸음 없음 상태에서 누적 걸음이 처음 늘어난 시각 → 걸음 상태가 바뀌면 걸린 초를 남김
        const float StageKmh = 8f;  // 책상 확인용 가정 속도(4 km/h 이하면 달리기 판정이 Irregular가 되므로 달리기 속도)
        const long StageMs = 15000; // CalibrationLadder.StageSec과 같음

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => Nav.Pop();
            var perms = Q<VisualElement>("perms");
            foreach (PermissionKind k in Enum.GetValues(typeof(PermissionKind)))
            {
                var row = new VisualElement();
                row.AddToClassList("field-row");
                var label = new Label();
                label.AddToClassList("body-text");
                label.style.flexGrow = 1;
                var ask = new Button(() => Request(k)) { text = "요청" };
                ask.AddToClassList("btn");
                ask.AddToClassList("btn-secondary");
                row.Add(label);
                row.Add(ask);
                perms.Add(row);
                statusLabels[k] = label;
            }
            headphones = Q<Label>("headphones");
            log = Q<Label>("log");
            Q<Button>("settings").clicked += () =>
            {
                Native.Permissions.OpenSettings();
                Log("설정 앱 열기");
            };
            Q<Button>("keychain").clicked += CheckKeychain;

            speed = Q<Label>("speed");
            speedToggle = Q<Button>("speed-toggle");
            speedStage = Q<Button>("speed-stage");
            speedToggle.clicked += ToggleSpeed;
            speedStage.clicked += MeasureStage;
            Root.schedule.Execute(RefreshSpeed).Every(100);
        }

        protected internal override void OnShow()
        {
            Native.Permissions.Changed += OnPermission;
            Native.Headphones.Changed += OnHeadphones;
            RefreshAll();
        }

        protected internal override void OnHide()
        {
            Native.Permissions.Changed -= OnPermission;
            Native.Headphones.Changed -= OnHeadphones;
            StopSpeed();
        }

        void RefreshAll()
        {
            foreach (var kv in statusLabels) kv.Value.text = $"{kv.Key}: {Native.Permissions.GetStatus(kv.Key)}";
            var h = Native.Headphones;
            headphones.text = $"이어폰: {h.Status}" + (h.DeviceName != null ? $" ({h.DeviceName})" : "");
        }

        async void Request(PermissionKind k)
        {
            Log($"{k} 요청…");
            try
            {
                var s = await Native.Permissions.RequestAsync(k);
                Log($"{k} 요청 결과: {s}");
            }
            catch (Exception e)
            {
                Log($"{k} 요청 실패: {e.Message}");
            }
            if (Root.panel != null) RefreshAll();
        }

        void OnPermission(PermissionKind k, PermissionStatus s)
        {
            Log($"이벤트 권한 {k} → {s}");
            RefreshAll();
        }

        void OnHeadphones(HeadphoneStatus s)
        {
            Log($"이벤트 이어폰 → {s}");
            RefreshAll();
        }

        void CheckKeychain()
        {
            const string key = "native-check";
            try
            {
                var store = Native.SecureStore;
                string before = store.Get(key);
                string value = "확인 " + DateTime.Now.ToString("HH:mm:ss");
                store.Set(key, value);
                string read = store.Get(key);
                store.Delete(key);
                string after = store.Get(key);
                bool ok = read == value && after == null;
                Log($"키체인 {(ok ? "정상" : "이상")}: 전 {before ?? "없음"} / 읽음 {read} / 지운 뒤 {after ?? "없음"}");
            }
            catch (Exception e)
            {
                Log("키체인 실패: " + e.Message);
            }
        }

        // ---- 속도 센서 ----

        void ToggleSpeed()
        {
            if (sensor != null)
            {
                StopSpeed();
                Log("속도 센서 끔");
                return;
            }
            try
            {
                var s = Native.Speed;
                if (s.IsRunning)
                {
                    Log("속도 센서가 이미 다른 곳에서 켜져 있음");
                    return;
                }
                s.GaitChanged += OnGait;
                s.FlowChanged += OnFlow;
                s.Start();
                sensor = s;
                lastSteps = 0;
                firstStepAt = -1;
                Log("속도 센서 켬");
            }
            catch (Exception e)
            {
                Log("속도 센서 실패: " + e.Message);
            }
            RefreshSpeed();
        }

        void StopSpeed()
        {
            stageTimer?.Pause();
            stageTimer = null;
            if (sensor == null) return;
            if (ownCalibration) sensor.AbortCalibration();
            ownCalibration = false;
            sensor.GaitChanged -= OnGait;
            sensor.FlowChanged -= OnFlow;
            sensor.Stop();
            sensor = null;
            RefreshSpeed();
        }

        void OnGait(Gait g)
        {
            string took = g != Gait.None && firstStepAt >= 0
                ? $" (첫 걸음 뒤 {Time.realtimeSinceStartupAsDouble - firstStepAt:F1}초)"
                : "";
            Log($"이벤트 걸음 상태 → {g}{took}");
            firstStepAt = -1;
        }

        void OnFlow() => Log($"이벤트 흐름 폰 {sensor?.Phone} · 에어팟 {sensor?.AirPods}");

        // 단계 하나 재기: 보정 흐름과 같은 순서(BeginCalibrationStage → 15초 → EndCalibrationStage). 지금 보정은 바꾸지 않는다
        void MeasureStage()
        {
            if (sensor == null)
            {
                Log("속도 센서를 먼저 켜세요");
                return;
            }
            if (sensor.StageOpen) return;
            try
            {
                if (!sensor.Calibrating)
                {
                    sensor.BeginCalibration();
                    ownCalibration = true;
                }
                sensor.BeginCalibrationStage(StageKmh);
            }
            catch (Exception e)
            {
                Log("단계 시작 실패: " + e.Message);
                return;
            }
            Log($"단계 재기 시작({StageKmh} km/h로 가정, 15초 중 앞 5초는 버림)");
            stageTimer = Root.schedule.Execute(EndStage);
            stageTimer.ExecuteLater(StageMs);
        }

        void EndStage()
        {
            stageTimer = null;
            if (sensor == null || !sensor.StageOpen) return;
            try
            {
                var r = sensor.EndCalibrationStage();
                string point = "";
                if (r == CalStageResult.Ok && sensor is SpeedSensorBase b && b.DraftPoints?.Count > 0)
                {
                    var p = b.DraftPoints[b.DraftPoints.Count - 1];
                    point = $" ln충격 {p.LnImpact:F3} · {p.Cadence:F1} spm · 에어팟 폭 {(p.AirAmp.HasValue ? p.AirAmp.Value.ToString("F3") : "없음")} · {(p.Running ? "달리기" : "걷기")}";
                }
                Log($"단계 결과 {r}{point}");
            }
            catch (Exception e)
            {
                Log("단계 끝 실패: " + e.Message);
            }
            if (ownCalibration) sensor.AbortCalibration();
            ownCalibration = false;
        }

        void RefreshSpeed()
        {
            if (speed == null) return;
            speedToggle.text = sensor != null ? "끄기" : "켜기";
            speedStage.SetEnabled(sensor != null && stageTimer == null);
            if (sensor == null)
            {
                speed.text = "꺼짐";
                return;
            }
            var r = sensor.Current;
            if (r.Gait == Gait.None && r.Steps > lastSteps && firstStepAt < 0) firstStepAt = Time.realtimeSinceStartupAsDouble;
            lastSteps = r.Steps;
            var diag = sensor as ISpeedSensorDiagnostics;
            string phoneRate = diag != null ? $" {diag.PhoneSamplesPerSec}/초" : "";
            string airRate = diag != null ? $" {diag.AirPodsSamplesPerSec}/초" : "";
            speed.text =
                $"폰 {sensor.Phone}{phoneRate} · 에어팟 {sensor.AirPods}{airRate}\n" +
                $"걸음 {r.Gait} · {(r.HasSpeed ? r.SpeedKmh.ToString("F2") + " km/h" : "속도 없음")} · {r.CadenceSpm:F0} spm · 누적 {r.Steps}걸음" +
                (r.UsingAirPods ? " · 에어팟 섞음" : "") + "\n" +
                $"{r.Time:F1}초 · 보정 {(sensor.HasCalibration ? "있음" : "없음")}" + (sensor.StageOpen ? " · 단계 재는 중" : "");
        }

        void Log(string line)
        {
            Debug.Log("[NativeCheck] " + line);
            lines.Insert(0, $"{DateTime.Now:HH:mm:ss} {line}");
            if (lines.Count > 30) lines.RemoveAt(lines.Count - 1);
            if (log != null) log.text = string.Join("\n", lines);
        }
    }
}
