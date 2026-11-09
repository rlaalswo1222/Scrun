using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Scrun.App.Editor
{
    // 메뉴 Scrun → Build App Scene: 앱 화면에 필요한 에셋과 앱 씬을 코드로 다시 만든다(손으로 만든 설정이 없게).
    //  - Pretendard 글꼴 3종 → UI Toolkit 글꼴 에셋(동적: 쓰는 글자만 그때그때 그림, 한글 11,172자를 미리 굽지 않음)
    //  - 테마(AppTheme.tss: 기본 테마 + 기본 글꼴 Pretendard), 패널 설정(390×844 기준 크기 맞춤)
    //  - 씬 Assets/Scrun/Scenes/App/App.unity(UIDocument + AppRoot), 빌드 씬 목록 맨 앞
    public static class AppSceneBuilder
    {
        const string UiDir = "Assets/Scrun/App/UI";
        const string FontDir = UiDir + "/Fonts";
        const string ThemePath = UiDir + "/AppTheme.tss";
        const string PanelPath = UiDir + "/AppPanelSettings.asset";
        const string ShellPath = "Assets/Scrun/App/Resources/UI/AppShell.uxml";
        const string SceneDir = "Assets/Scrun/Scenes/App";
        const string ScenePath = SceneDir + "/App.unity";
        static readonly string[] Weights = { "Regular", "SemiBold", "Bold" };

        [MenuItem("Scrun/Build App Scene")]
        public static void Build()
        {
            if (SceneManager.GetActiveScene().isDirty)
            {
                Debug.LogWarning("[AppScene] 지금 씬에 저장 안 한 변경이 있어 멈춤. 저장하거나 되돌린 뒤 다시 실행하세요.");
                return;
            }
            foreach (var w in Weights) EnsureFontAsset(w);
            var theme = EnsureTheme();
            EnsurePanelSettings(theme);
            // USS가 글꼴 에셋보다 먼저 읽혔으면 글꼴 연결이 비므로 다시 읽힌다
            AssetDatabase.ImportAsset("Assets/Scrun/App/Resources/UI/App.uss", ImportAssetOptions.ForceUpdate);
            BuildScene();
            Debug.Log($"[AppScene] 완료: {ScenePath} (빌드 씬 목록 맨 앞). Play로 확인, 기기 모양은 Window → General → Device Simulator");
        }

        static void EnsureFontAsset(string weight)
        {
            var assetPath = $"{FontDir}/Pretendard-{weight}.asset";
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath) != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/Pretendard-{weight}.otf");
            if (font == null)
            {
                Debug.LogError($"[AppScene] 글꼴 파일이 없음: {FontDir}/Pretendard-{weight}.otf");
                return;
            }
            var fa = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                               AtlasPopulationMode.Dynamic, true);
            fa.name = $"Pretendard-{weight}";
            AssetDatabase.CreateAsset(fa, assetPath);
            foreach (var tex in fa.atlasTextures.Where(t => t != null))
            {
                tex.name = fa.name + " Atlas";
                AssetDatabase.AddObjectToAsset(tex, fa);
            }
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssetIfDirty(fa);
            AssetDatabase.ImportAsset(assetPath);
        }

        static ThemeStyleSheet EnsureTheme()
        {
            const string tss =
                "/* 앱 기본 테마: Unity 기본 런타임 테마 + 기본 글꼴 Pretendard(한글). AppSceneBuilder가 만듦 */\n" +
                "@import url(\"unity-theme://default\");\n\n" +
                ":root {\n" +
                "    -unity-font-definition: url(\"project://database/" + FontDir + "/Pretendard-Regular.asset\");\n" +
                "}\n";
            var full = Path.GetFullPath(ThemePath);
            if (!File.Exists(full) || File.ReadAllText(full) != tss)
            {
                File.WriteAllText(full, tss);
                AssetDatabase.ImportAsset(ThemePath);
            }
            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        }

        static PanelSettings EnsurePanelSettings(ThemeStyleSheet theme)
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelPath);
            }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(390, 844); // 세로 기준, 가로 화면에선 AppRoot가 뒤집음
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0f; // 세로 화면은 폭 기준(기종이 달라도 좌우 배치가 같게)
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssetIfDirty(ps);
            return ps;
        }

        static void BuildScene()
        {
            if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets/Scrun/Scenes", "App");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0xF6, 0xF5, 0xF1, 0xFF);

            var go = new GameObject("App");
            var doc = go.AddComponent<UIDocument>();
            // 새 씬을 열면 Unity가 쓰지 않는 에셋을 메모리에서 내려서 앞에서 받은 참조가 끊길 수 있다 → 경로로 다시 불러 연결
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (doc.panelSettings == null) Debug.LogError("[AppScene] 패널 설정을 못 불러옴: " + PanelPath);
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ShellPath);
            go.AddComponent<AppRoot>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
