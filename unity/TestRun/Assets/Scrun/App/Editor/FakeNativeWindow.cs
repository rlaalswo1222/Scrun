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

            if (EditorGUI.EndChangeCheck())
            {
                SaveConfig();
                if (!playing) fake.LoadStartState(); // 에디트 모드에서도 바로 반영
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("기본값으로"))
            {
                FakeNative.Config = new FakeNativeConfig();
                SaveConfig();
                if (playing) Later(fake.SimulateStartState);
                else fake.LoadStartState();
            }
        }
    }
}
