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
    //  - 로고 글꼴 Paytone One(스플래시 "SCRUN", 계획 0030, OFL) → 같은 방식의 글꼴 에셋. 스플래시 그림 가져오기 설정
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
        const string ImageDir = UiDir + "/Images";

        // 글꼴 파일(확장자 포함) → 같은 이름의 글꼴 에셋. Pretendard 4종 + 로고 Paytone One
        static readonly string[] FontFiles =
            Weights.Select(w => $"Pretendard-{w}.otf").Append("PaytoneOne-Regular.ttf").ToArray();

        // 글꼴 에셋 경로(AppFontReset도 이 목록을 씀)
        public static readonly string[] FontAssetPaths =
            FontFiles.Select(f => $"{FontDir}/{Path.GetFileNameWithoutExtension(f)}.asset").ToArray();

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
            EnsureFontAssets();
            EnsureAppImages();
            EnsureLaunchScreen();
            var theme = EnsureTheme();
            EnsurePanelSettings(theme);
            // USS가 글꼴 에셋보다 먼저 읽혔으면 글꼴 연결이 비므로 다시 읽힌다
            AssetDatabase.ImportAsset("Assets/Scrun/App/Resources/UI/App.uss", ImportAssetOptions.ForceUpdate);
            BuildScene();
            Debug.Log($"[AppScene] 완료: {ScenePath} (빌드 씬 목록 맨 앞). Play로 확인, 기기 모양은 Window → General → Device Simulator");
        }

        public static void EnsureFontAssets()
        {
            foreach (var f in FontFiles) EnsureFontAsset(f);
        }

        static void EnsureFontAsset(string file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var assetPath = $"{FontDir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath) != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/{file}");
            if (font == null)
            {
                Debug.LogError($"[AppScene] 글꼴 파일이 없음: {FontDir}/{file}");
                return;
            }
            // 로고 글꼴은 "SCRUN" 다섯 글자만 그리므로 지도(아틀라스)를 작게(메모리). 한글 본문 Pretendard는 1024
            int atlas = name.StartsWith("Pretendard") ? 1024 : 256;
            var fa = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, atlas, atlas,
                                               AtlasPopulationMode.Dynamic, true);
            fa.name = name;
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

        // 앱 그림(스플래시 계획 0030, 온보딩 0031): 그라데이션은 압축하면 띠가 보여 무압축·늘릴 때 가장자리 번짐 없게 Clamp,
        // 캐릭터는 투명 배경 그대로. 둘 다 UI에 원래 크기 근처로만 그려서 밉맵 없음
        public static void EnsureAppImages()
        {
            SetImage($"{ImageDir}/SplashGradient.png", uncompressed: true);
            SetImage($"{ImageDir}/CharacterRun.png", uncompressed: false);
            // 온보딩 그림(계획 0031): 서 있는·스트레칭 캐릭터(로그인 실패·홈·결과에서 씀)
            foreach (var name in new[] { "CharacterStand", "CharacterStretch" })
                SetImage($"{ImageDir}/{name}.png", uncompressed: false);
            // 우주 시안 온보딩(계획 0061, unity/tools/space_onboarding_assets.py): 바탕은 그라데이션이라 무압축(2배 크기로 줄여 둠),
            // 장면·아이콘은 투명 배경 렌더라 고품질 압축
            SetImage($"{ImageDir}/Space/SpaceBg.png", uncompressed: true);
            // (소개 A-02–A-04 그림·깃발·완료 장면·보정 그림·권한 3D 아이콘은 계획 0068에서 미리보기 모양으로 바꾸며 지움)
            foreach (var name in new[] { "LoginLogo", "LoginPlanet", "KakaoSymbol" })
                SetImage($"{ImageDir}/Space/{name}.png", uncompressed: false);
            // 우주 시안 홈·코스(계획 0062, unity/tools/space_home_assets.py): 바탕 둘은 무압축(2배), 나머지는 고품질 압축
            foreach (var name in new[] { "HomeBg", "CourseDetailBg" })
                SetImage($"{ImageDir}/Space/{name}.png", uncompressed: true);
            foreach (var name in new[] { "HomeHero", "HomeHeroShort", "CourseThumb", "CourseHero", "Face", "Zone1", "Zone2", "Zone3",
                                         "Zone4", "Zone5", "DockHome", "DockFriends", "DockRecords", "DockMe" })
                SetImage($"{ImageDir}/Space/{name}.png", uncompressed: false);
            // 우주 디자인 전체(계획 0068, Figma 원본): 바탕 넷은 무압축(2배), 탭 키 아이콘은 고품질 압축
            foreach (var name in new[] { "BgBase", "BgHome", "BgCelebrate", "BgLogin" })
                SetImage($"{ImageDir}/Space/{name}.png", uncompressed: true);
            foreach (var tab in new[] { "Home", "Friends", "Records", "My" })
                foreach (var state in new[] { "Idle", "Active" })
                    SetImage($"{ImageDir}/Space/Tab{tab}{state}.png", uncompressed: false);
            // E 인증·결과, F 기록(계획 0068, Figma 원본): 계기판 사진 둘(미리보기처럼 위 30% 기준으로
            // 잘라 둠)·상태 3D·인증 메달·포즈 셋. 모두 고품질 압축
            foreach (var name in new[] { "ConsoleSample", "ConsoleGlare", "StatusWarn", "StatusErr", "StatusCamera", "BadgeVerified",
                                         "PoseSad", "PosePuzzled", "PoseWait" })
                SetImage($"{ImageDir}/Space/{name}.png", uncompressed: false);
            // G 친구·H 마이(계획 0068, Figma 원본): 러닝복 전신 set_lv1–6(캐릭터만 잘라 높이 432), 레벨 얼굴 Set face Lv.1–6(128),
            // 러닝머신 안내(guide_treadmill). 투명 배경이라 고품질 압축
            foreach (var name in new[] { "SetLv1", "SetLv2", "SetLv3", "SetLv4", "SetLv5", "SetLv6",
                                         "FaceLv1", "FaceLv2", "FaceLv3", "FaceLv4", "FaceLv5", "FaceLv6", "GuideTreadmill" })
                SetImage($"{ImageDir}/Space/{name}.png", uncompressed: false);
            // A 온보딩·B 홈·C 코스·I 예외(계획 0068): 상태 3D 셋·기쁜 포즈는 Figma 원본, 나머지는 미리보기 webp를 PNG로 바꾼 임시 그림
            // (이어폰 안내 GuideEarbuds, 준비 끝 우주인 AstroReady, 로켓 우주인 AstroRocket, 흐린 맵 하늘 MapSkyBlur, 목표 달성 행성 HomeHeroDone →
            // Figma 원본으로 바꿀 것). 흐린 하늘은 그라데이션이라 무압축, 나머지는 투명 배경이라 고품질 압축
            foreach (var name in new[] { "StatusOk", "StatusSensor", "StatusCrowd", "PoseJoy",
                                         "GuideEarbuds", "AstroReady", "AstroRocket", "HomeHeroDone" })
                SetImage($"{ImageDir}/Space/{name}.png", uncompressed: false);
            SetImage($"{ImageDir}/Space/MapSkyBlur.png", uncompressed: true);
        }

        static void SetImage(string path, bool uncompressed)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti)
            {
                Debug.LogError($"[AppScene] 그림이 없음: {path}");
                return;
            }
            var compression = uncompressed ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            if (ti.textureType == TextureImporterType.Default && !ti.mipmapEnabled && ti.alphaIsTransparency &&
                ti.wrapMode == TextureWrapMode.Clamp && ti.npotScale == TextureImporterNPOTScale.None &&
                ti.textureCompression == compression)
                return;
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = compression;
            ti.SaveAndReimport();
        }

        // 아이폰 실행 화면(Unity가 뜨기 전 iOS가 그리는 화면)을 스플래시 위쪽 색으로(계획 0030):
        // 기본값이면 검은 화면 → 파란 스플래시로 번쩍임. "그림 + 배경(상대 크기)"에 그림 없이 배경색만.
        // Unity 시작 화면 배경색도 같게(Unity 로고 화면은 꺼져 있음)
        static readonly Color LaunchColor = new Color32(0x2F, 0x6F, 0xE4, 0xFF); // App.uss --color-primary(sky/700)와 같게

        public static void EnsureLaunchScreen()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settings == null) return;
            var so = new SerializedObject(settings);
            bool changed = false;
            void SetInt(string prop, int v)
            {
                var p = so.FindProperty(prop);
                if (p == null) { Debug.LogWarning($"[AppScene] 설정 없음: {prop}"); return; }
                if (p.intValue != v) { p.intValue = v; changed = true; }
            }
            void SetColor(string prop, Color v)
            {
                var p = so.FindProperty(prop);
                if (p == null) { Debug.LogWarning($"[AppScene] 설정 없음: {prop}"); return; }
                if (p.colorValue != v) { p.colorValue = v; changed = true; }
            }
            SetInt("iOSLaunchScreenType", 1); // iOSLaunchScreenType.ImageAndBackgroundRelative
            SetColor("iOSLaunchScreenBackgroundColor", LaunchColor);
            SetInt("iOSLaunchScreeniPadType", 1); // 대상 기기가 아이폰·아이패드(targetDevice 2)라 아이패드도
            SetColor("iOSLaunchScreeniPadBackgroundColor", LaunchColor);
            SetColor("m_SplashScreenBackgroundColor", LaunchColor);
            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            // 프로젝트 설정 파일은 SaveAssetIfDirty·SaveToSerializedFileAndForget로는 안 써짐(11/12 확인) → 전체 저장.
            // 값이 다를 때(처음 한 번)만 돈다. 그때 편집 중이던 다른 에셋이 함께 써질 수 있으니 실행 뒤 git 변경을 확인
            AssetDatabase.SaveAssets();
            Debug.Log("[AppScene] 아이폰 실행 화면: 스플래시 파랑(#2F6FE4)");
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
