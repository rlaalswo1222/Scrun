using System;
using UnityEditor;
using UnityEngine;

namespace Scrun.App.Editor
{
    // 메뉴 Scrun → Fake Native: 에디터에서 권한·이어폰 상태를 흉내 낸다.
    // Play 전: 처음 상태와 팝업에서 누를 답을 정함(이 컴퓨터에 저장).
    // Play 중: 지금 상태를 바꾸면 화면 코드에 Changed 이벤트가 간다.
    public class FakeNativeWindow : EditorWindow
    {
        const string PrefKey = "Scrun.FakeNative.Config";

        static readonly string[] StatusLabels = { "안 물어봄", "허용", "거부", "제한(못 바꿈)" };
        static readonly string[] AnswerLabels = { "허용", "거부" };
        static readonly string[] HeadphoneLabels = { "없음", "모션 없는 이어폰", "에어팟(모션 있음)" };

        static string KindLabel(PermissionKind k) => k switch
        {
            PermissionKind.Motion => "모션·피트니스 (필수)",
            PermissionKind.Camera => "카메라 (필수)",
            PermissionKind.HeadphoneMotion => "헤드폰 모션 (선택)",
            PermissionKind.Notifications => "알림 (선택)",
            _ => k.ToString(), // 새 권한을 더하면 이름만 여기 추가
        };

        [MenuItem("Scrun/Fake Native (가짜 권한·에어팟)")]
        static void Open() => GetWindow<FakeNativeWindow>("Fake Native");

        // 도메인 리로드(컴파일) 때마다 저장된 설정을 FakeNative에 넣고, 에디트 모드에서도 그 상태가 보이게 한다.
        [InitializeOnLoadMethod]
        static void LoadConfig()
        {
            var json = EditorPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, FakeNative.Config); }
                catch (Exception e) { Debug.LogWarning($"[FakeNative] 저장된 설정을 못 읽어 기본값으로: {e.Message}"); }
            }
            FakeNative.Config.Normalize();
            if (!EditorApplication.isPlayingOrWillChangePlaymode) FakeNative.Instance.LoadStartState();
        }

        static void SaveConfig() => EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(FakeNative.Config));

        // 화면 코드의 Changed 구독자가 OnGUI 레이아웃 도중에 돌지 않게 다음 틱으로 미룬다.
        static void Later(Action a) => EditorApplication.delayCall += () => a();

        void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying) Repaint(); // 팝업 응답 같은 바뀜을 바로 보여 줌
        }

        void OnGUI()
        {
            var cfg = FakeNative.Config;
            var fake = FakeNative.Instance;
            bool playing = EditorApplication.isPlaying;
            cfg.Normalize();

            EditorGUILayout.HelpBox(playing
                ? "Play 중: '지금 상태'를 바꾸면 팝업 응답이나 설정 앱에서 바꾸고 돌아온 것처럼 이벤트가 갑니다."
                : "Play 전: 처음 상태와 팝업에서 누를 답을 정합니다. Play를 시작할 때마다 이 값으로 되돌아갑니다.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("권한", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("", GUILayout.Width(150));
                EditorGUILayout.LabelField(playing ? "지금 상태" : "처음 상태", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("팝업에서", EditorStyles.miniLabel);
            }
            foreach (PermissionKind kind in Enum.GetValues(typeof(PermissionKind)))
            {
                int i = (int)kind;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(KindLabel(kind), GUILayout.Width(150));
                    if (playing)
                    {
                        int now = (int)fake.GetStatus(kind);
                        int picked = EditorGUILayout.Popup(now, StatusLabels);
                        if (picked != now) Later(() => fake.SimulateStatus(kind, (PermissionStatus)picked));
                    }
                    else
                    {
                        cfg.startStatus[i] = (PermissionStatus)EditorGUILayout.Popup((int)cfg.startStatus[i], StatusLabels);
                    }
                    cfg.grantOnRequest[i] = EditorGUILayout.Popup(cfg.grantOnRequest[i] ? 0 : 1, AnswerLabels) == 0;
                }
            }
            cfg.requestDelaySec = EditorGUILayout.Slider("팝업 누르는 시간(초)", cfg.requestDelaySec, 0f, 3f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("이어폰", EditorStyles.boldLabel);
            if (playing)
            {
                int now = (int)fake.Status;
                int picked = EditorGUILayout.Popup("지금 상태", now, HeadphoneLabels);
                var name = EditorGUILayout.TextField("이름", cfg.headphoneName);
                if (picked != now || name != cfg.headphoneName)
                {
                    cfg.headphoneName = name;
                    Later(() => fake.SimulateHeadphones((HeadphoneStatus)picked, name));
                }
            }
            else
            {
                cfg.headphones = (HeadphoneStatus)EditorGUILayout.Popup("처음 상태", (int)cfg.headphones, HeadphoneLabels);
                cfg.headphoneName = EditorGUILayout.TextField("이름", cfg.headphoneName);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("카카오 로그인", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                cfg.kakaoToken = EditorGUILayout.TextField("토큰", cfg.kakaoToken);
                if (GUILayout.Button("새 사람", GUILayout.Width(60)))
                {
                    cfg.kakaoToken = "k_" + UnityEngine.Random.Range(100000, 999999999);
                    GUI.changed = true;
                }
            }
            cfg.kakaoCancels = EditorGUILayout.Toggle("취소한 것처럼", cfg.kakaoCancels);
            EditorGUILayout.HelpBox("로컬 서버(가짜 카카오)에서 k_ 뒤 숫자가 카카오 회원번호. 처음 보는 숫자면 가입 화면으로 갑니다.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("속도 센서", EditorStyles.boldLabel);
            cfg.phoneData = EditorGUILayout.Toggle("폰 데이터 들어옴", cfg.phoneData);
            cfg.airPodsData = EditorGUILayout.Toggle("에어팟 데이터 들어옴", cfg.airPodsData);
            cfg.hasCalibration = EditorGUILayout.Toggle("보정값 있음", cfg.hasCalibration);
            cfg.sensorStartSec = EditorGUILayout.Slider("켜고 들어오기까지(초)", cfg.sensorStartSec, 0f, 5f);
            cfg.detectDelaySec = EditorGUILayout.Slider("걸음 감지 지연(초)", cfg.detectDelaySec, 0f, 10f);
            if (!playing) cfg.startKmh = EditorGUILayout.Slider("켤 때 속도(km/h)", cfg.startKmh, 0f, 20f);

            if (EditorGUI.EndChangeCheck())
            {
                SaveConfig();
                if (!playing) fake.LoadStartState(); // 에디트 모드에서도 바로 반영
            }

            if (playing) SpeedSensorLive();
            else EditorGUILayout.HelpBox("속도 센서 칸의 체크는 Play 중에 바꿔도 바로 반영됩니다(끄면 2초 뒤 '끊김'). " +
                "에어팟은 위 이어폰이 '에어팟(모션 있음)'이고 헤드폰 모션이 '허용'일 때만 씁니다. " +
                "Play 중 키보드 ↑/↓ = ±0.5 km/h, Space = 정지(센서가 켜져 있을 때).", MessageType.None);

            EditorGUILayout.Space();
            if (GUILayout.Button("기본값으로"))
            {
                FakeNative.Config = new FakeNativeConfig();
                SaveConfig();
                if (playing) Later(fake.SimulateStartState);
                else fake.LoadStartState();
            }
        }

        static readonly string[] FlowLabels = { "꺼짐", "기다림", "정상", "끊김" };
        static readonly string[] GaitLabels = { "걸음 없음", "걷기", "달리기" };

        // Play 중: 사람이 러닝머신 위에서 하는 것을 바꾸고, 화면 코드가 읽는 값을 보여 준다.
        static void SpeedSensorLive()
        {
            var s = FakeSpeedSensor.Instance;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(s.IsRunning ? "센서 켜짐" : "센서 꺼짐(화면이 Native.Speed.Start()를 부르면 켜짐)");
                if (GUILayout.Button(s.IsRunning ? "끄기" : "켜기", GUILayout.Width(50)))
                    Later(() => { if (s.IsRunning) s.Stop(); else s.Start(); });
            }

            float kmh = EditorGUILayout.Slider("사람 속도(km/h)", s.TargetKmh, 0f, 20f);
            if (!Mathf.Approximately(kmh, s.TargetKmh)) Later(() => s.SetTargetKmh(kmh));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("서 있음")) Later(() => s.SetTargetKmh(0f));
                if (GUILayout.Button("걷기 4")) Later(() => s.SetTargetKmh(4f));
                if (GUILayout.Button("달리기 8")) Later(() => s.SetTargetKmh(8f));
            }
            if (GUILayout.Button(s.ScenarioPlaying ? "시나리오 진행 중…" : "시나리오: 3초 뒤 걷기 4 → 10초 뒤 달리기 8"))
                Later(s.PlayScenario);

            var r = s.Current;
            EditorGUILayout.LabelField("화면이 읽는 값", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("폰 / 에어팟", $"{FlowLabels[(int)s.Phone]} / {FlowLabels[(int)s.AirPods]}");
            EditorGUILayout.LabelField("걸음 상태", GaitLabels[(int)r.Gait]);
            EditorGUILayout.LabelField("속도", r.HasSpeed ? $"{r.SpeedKmh:0.0} km/h{(r.UsingAirPods ? " (에어팟 포함)" : "")}" : "없음");
            EditorGUILayout.LabelField("걸음 수 · 케이던스", $"{r.Steps} · {r.CadenceSpm:0} spm");
            EditorGUILayout.LabelField("센서 시각", $"{r.Time:0.0} 초");
        }
    }
}
