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
    //  - Pretendard 글꼴 4종(Medium은 Figma UI Kit 본문) → UI Toolkit 글꼴 에셋(동적: 쓰는 글자만 그때그때 그림, 한글 11,172자를 미리 굽지 않음)
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
        static readonly string[] Weights = { "Regular", "Medium", "SemiBold", "Bold" };

        // 글꼴 에셋 경로(AppFontReset도 이 목록을 씀)
        public static readonly string[] FontAssetPaths = System.Array.ConvertAll(Weights, w => $"{FontDir}/Pretendard-{w}.asset");

        [MenuItem("Scrun/Build App Scene")]
        public static void Build()
        {
            // 새 씬을 열면 열려 있는 씬이 모두 닫힌다 → 저장 안 한 씬이 하나라도 있으면 멈춤
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!SceneManager.GetSceneAt(i).isDirty) continue;
                Debug.LogWarning($"[AppScene] '{SceneManager.GetSceneAt(i).name}'에 저장 안 한 변경이 있어 멈춤. 저장하거나 되돌린 뒤 다시 실행하세요.");
                return;
            }
            EnsureOrientationSettings();
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

        // 앱은 세로 고정 + 달리기 화면만 가로. 아이폰은 Info.plist에 적힌 방향으로만 돌 수 있으므로
        // 세로·가로(왼쪽·오른쪽)를 모두 허용해 두고, 실제 고정은 AppRoot가 화면마다 한다(거꾸로 세로는 막음)
        static void EnsureOrientationSettings()
        {
            if (PlayerSettings.defaultInterfaceOrientation == UIOrientation.AutoRotation
                && PlayerSettings.allowedAutorotateToPortrait && !PlayerSettings.allowedAutorotateToPortraitUpsideDown
                && PlayerSettings.allowedAutorotateToLandscapeLeft && PlayerSettings.allowedAutorotateToLandscapeRight)
                return;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            // 프로젝트 설정 파일만 저장(전체 저장은 다른 에셋까지 다시 쓸 수 있음)
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset"))
                AssetDatabase.SaveAssetIfDirty(o);
            Debug.Log("[AppScene] 화면 방향 설정: 세로·가로 허용(거꾸로 세로 제외), 실제 고정은 AppRoot");
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
            ps.referenceResolution = AppRoot.PortraitReference; // 세로 기준, 가로 화면에선 AppRoot가 뒤집음
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
