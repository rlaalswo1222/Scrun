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

        static readonly string[] KindLabels = { "모션·피트니스 (필수)", "카메라 (필수)", "헤드폰 모션 (선택)", "알림 (선택)" };
        static readonly string[] StatusLabels = { "안 물어봄", "허용", "거부", "제한(못 바꿈)" };
        static readonly string[] AnswerLabels = { "허용", "거부" };
        static readonly string[] HeadphoneLabels = { "없음", "모션 없는 이어폰", "에어팟(모션 있음)" };

        [MenuItem("Scrun/Fake Native (가짜 권한·에어팟)")]
        static void Open() => GetWindow<FakeNativeWindow>("Fake Native");

        // 도메인 리로드(컴파일·Play 진입) 때마다 저장된 설정을 FakeNative에 넣는다.
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
        }

        static void SaveConfig() => EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(FakeNative.Config));

        void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying) Repaint(); // 팝업 응답 같은 바뀜을 바로 보여 줌
        }

        void OnGUI()
        {
            var cfg = FakeNative.Config;
            var fake = FakeNative.Instance;
            bool playing = EditorApplication.isPlaying;

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
            for (int i = 0; i < KindLabels.Length; i++)
            {
                var kind = (PermissionKind)i;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(KindLabels[i], GUILayout.Width(150));
                    if (playing)
                    {
                        int now = (int)fake.GetStatus(kind);
                        int picked = EditorGUILayout.Popup(now, StatusLabels);
                        if (picked != now) fake.SimulateStatus(kind, (PermissionStatus)picked);
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
                    fake.SimulateHeadphones((HeadphoneStatus)picked, name);
                }
            }
            else
            {
                cfg.headphones = (HeadphoneStatus)EditorGUILayout.Popup("처음 상태", (int)cfg.headphones, HeadphoneLabels);
                cfg.headphoneName = EditorGUILayout.TextField("이름", cfg.headphoneName);
            }

            if (EditorGUI.EndChangeCheck()) SaveConfig();

            EditorGUILayout.Space();
            if (GUILayout.Button("기본값으로"))
            {
                FakeNative.Config = new FakeNativeConfig();
                SaveConfig();
                if (playing) fake.SimulateStartState();
            }
        }
    }
}
