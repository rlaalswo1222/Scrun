using UnityEditor;
using UnityEngine;

namespace Scrun.App.Editor
{
    // 메뉴 Scrun → API 서버: 에디터가 붙을 서버 주소(이 컴퓨터에 저장)와 저장된 로그인 지우기.
    // 기본은 로컬 서버(가짜 카카오 k_123). 공용 개발 서버는 진짜 카카오만 받아서 에디터 가짜 로그인은 거절된다.
    public class ApiServerWindow : EditorWindow
    {
        const string PrefKey = "Scrun.ApiBaseUrl";

        [MenuItem("Scrun/API 서버")]
        static void Open() => GetWindow<ApiServerWindow>("API 서버");

        [InitializeOnLoadMethod]
        static void Load() => ApiConfig.BaseUrl = EditorPrefs.GetString(PrefKey, ApiConfig.LocalServer);

        static void Set(string url)
        {
            url = (url ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) url = ApiConfig.LocalServer;
            ApiConfig.BaseUrl = url;
            EditorPrefs.SetString(PrefKey, url);
        }

        string draft;

        void OnGUI()
        {
            draft ??= ApiConfig.BaseUrl;
            EditorGUILayout.LabelField("지금", ApiConfig.BaseUrl);
            EditorGUILayout.Space();
            draft = EditorGUILayout.TextField("주소", draft);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("적용")) Set(draft);
                if (GUILayout.Button("로컬(8000)")) Set(draft = ApiConfig.LocalServer);
                if (GUILayout.Button("공용 개발 서버")) Set(draft = ApiConfig.SharedDevServer);
            }
            EditorGUILayout.HelpBox("로컬 서버: backend 폴더에서 uvicorn(README). 가짜 카카오 토큰 k_123으로 로그인.\n" +
                                    "공용 개발 서버: 진짜 카카오만 받음(아이폰용).", MessageType.Info);
            EditorGUILayout.Space();
            if (GUILayout.Button("저장된 로그인 지우기(처음 실행처럼)"))
            {
                Api.ClearSavedLogin();
                Debug.Log("[Api] 에디터에 저장된 로그인 토큰을 지웠습니다. Play를 다시 시작하세요.");
            }
        }
    }
}
