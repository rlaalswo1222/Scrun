using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Scrun.App.Editor
{
    // 아이폰 Xcode 프로젝트 만들기(계획 0018의 5번). 결과는 unity/Builds/iOS-*(git 밖) → Xcode로 열어 실행.
    // Info.plist 권한 문구·Swift 버전·서명 팀은 IosPostprocess가 빌드 직후 채운다.
    // 빌드 대상이 iOS가 아니면 먼저 전환한다(스크립트를 다시 컴파일하므로 끝나면 메뉴를 한 번 더 누름).
    public static class IosBuild
    {
        [MenuItem("Scrun/iOS/Xcode 만들기 (아이폰, 개발)")]
        static void Device() => Build(iOSSdkVersion.DeviceSDK, development: true, "iOS-Device");

        [MenuItem("Scrun/iOS/Xcode 만들기 (시뮬레이터, 개발)")]
        static void Simulator() => Build(iOSSdkVersion.SimulatorSDK, development: true, "iOS-Simulator");

        [MenuItem("Scrun/iOS/Xcode 만들기 (아이폰, 출시)")]
        static void Release() => Build(iOSSdkVersion.DeviceSDK, development: false, "iOS-Release");

        [MenuItem("Scrun/iOS/로컬 설정 파일 열기")]
        static void OpenLocal()
        {
            ScrunLocalSettings.EnsureFile();
            EditorUtility.RevealInFinder(ScrunLocalSettings.FilePath);
        }

        static void Build(iOSSdkVersion sdk, bool development, string folder)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                if (!EditorUtility.DisplayDialog("iOS로 전환", "빌드 대상을 iOS로 바꿉니다. 스크립트를 다시 컴파일하니 끝나면 메뉴를 한 번 더 눌러 주세요.", "전환", "취소"))
                    return;
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
                return;
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("빌드 씬이 없습니다. 메뉴 Scrun → Build App Scene 먼저.");

            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds", folder));
            var oldSdk = PlayerSettings.iOS.sdkVersion;
            var oldSimArch = PlayerSettings.iOS.simulatorSdkArchitecture;
            try
            {
                PlayerSettings.iOS.sdkVersion = sdk;
                if (sdk == iOSSdkVersion.SimulatorSDK)
                    PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64; // 애플 실리콘 맥

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outPath,
                    target = BuildTarget.iOS,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                });
                var s = report.summary;
                Debug.Log($"[iOS] {s.result} {s.totalTime.TotalSeconds:0}초 → {outPath}\n" +
                          "Xcode에서 Unity-iPhone.xcodeproj를 열고 실행(▶). 서명 팀이 비었으면 Signing & Capabilities에서 고르거나 로컬 설정 파일에 appleTeamId.");
                if (s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorUtility.RevealInFinder(outPath);
            }
            finally
            {
                PlayerSettings.iOS.sdkVersion = oldSdk;
                PlayerSettings.iOS.simulatorSdkArchitecture = oldSimArch;
                // 빌드 중에 시뮬레이터 값으로 ProjectSettings.asset이 저장되므로 되돌린 값으로 다시 저장한다
                // (SaveAssetIfDirty로는 ProjectSettings가 안 써짐, 11/10 확인). 다른 에셋이 함께 바뀌면 git에서 확인
                AssetDatabase.SaveAssets();
            }
        }
    }

    // 사람마다 다르고 저장소에 올리면 안 되는 값(git 밖 unity/TestRun/ScrunLocal.json, 예시는 ScrunLocal.example.json).
    [Serializable]
    public class ScrunLocalSettings
    {
        public string appleTeamId = "";       // Xcode → Settings → Accounts의 팀 ID(10자리). 비우면 Xcode에서 직접 고름
        public string kakaoNativeAppKey = ""; // 카카오 개발자 앱의 네이티브 앱 키(계획 0018의 6번)

        public static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../ScrunLocal.json"));

        public static ScrunLocalSettings Load()
        {
            if (!File.Exists(FilePath)) return new ScrunLocalSettings();
            return JsonUtility.FromJson<ScrunLocalSettings>(File.ReadAllText(FilePath)) ?? new ScrunLocalSettings();
        }

        public static void EnsureFile()
        {
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, JsonUtility.ToJson(new ScrunLocalSettings(), true));
        }
    }
}
