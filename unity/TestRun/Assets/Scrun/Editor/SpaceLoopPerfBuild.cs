using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Scrun.EditorTools
{
    // 우주 루프 아이폰 성능 측정 빌드(계획 0063). SpaceLoop 장면 하나 + SpaceLoopPerfProbe(SCRUN_PERF)로 개발 빌드를 만든다.
    // 앱(com.scrun.app)을 덮어쓰지 않게 번들 ID를 따로 쓰고, 빌드가 끝나면 플레이어 설정을 되돌린다.
    // 결과 unity/Builds/iOS-SpaceLoopPerf(git 밖) → Xcode로 아이폰에 설치·실행하면 약 6분 동안 구간을 돌며 재고 [SLPerf] 로그·Documents/slperf_latest.csv에 남김
    // 분석판(무거운 곳에서 하늘·후처리·반투명 등을 하나씩 꺼 보고, 아바타 20명으로 재기)은 메뉴 "분석" 또는 환경 변수 SLPERF_MODE=analyze
    // 명령줄: Unity -batchmode -buildTarget iOS -projectPath unity/TestRun -executeMethod Scrun.EditorTools.SpaceLoopPerfBuild.BuildCli -quit
    public static class SpaceLoopPerfBuild
    {
        const string Scene = "Assets/Scrun/Scenes/SpaceLoop.unity";
        const string BundleId = "com.scrun.spacelooptest";

        [MenuItem("Scrun/Space Loop/iOS 성능 측정 빌드 (아이폰)")]
        static void Menu()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                if (!EditorUtility.DisplayDialog("iOS로 전환", "빌드 대상을 iOS로 바꿉니다. 스크립트를 다시 컴파일하니 끝나면 메뉴를 한 번 더 눌러 주세요.", "전환", "취소"))
                    return;
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
                return;
            }
            if (Build(false, out var path)) EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Scrun/Space Loop/iOS 성능 측정 빌드 (아이폰, 분석)")]
        static void MenuAnalyze()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS) { Menu(); return; }
            if (Build(true, out var path)) EditorUtility.RevealInFinder(path);
        }

        public static void BuildCli() =>
            EditorApplication.Exit(Build(System.Environment.GetEnvironmentVariable("SLPERF_MODE") == "analyze", out _) ? 0 : 1);

        static bool Build(bool analyze, out string outPath)
        {
            outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/iOS-SpaceLoopPerf"));
            var nt = NamedBuildTarget.iOS;
            string oldId = PlayerSettings.GetApplicationIdentifier(nt);
            string oldName = PlayerSettings.productName;
            bool oldTiming = PlayerSettings.enableFrameTimingStats;
            var oldSdk = PlayerSettings.iOS.sdkVersion;
            try
            {
                PlayerSettings.SetApplicationIdentifier(nt, BundleId);
                PlayerSettings.productName = "SL 측정";
                PlayerSettings.enableFrameTimingStats = true;   // CPU·GPU 프레임 시간(FrameTimingManager)
                PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = outPath,
                    target = BuildTarget.iOS,
                    options = BuildOptions.Development,      // 그리기 호출·삼각형 카운터는 개발 빌드에서만 읽힘
                    extraScriptingDefines = analyze ? new[] { "SCRUN_PERF", "SCRUN_PERF_ANALYZE" } : new[] { "SCRUN_PERF" },
                });
                var s = report.summary;
                Debug.Log($"[SLPerf] 빌드({(analyze ? "분석" : "기본")}) {s.result} {s.totalTime.TotalSeconds:0}초 → {outPath}");
                return s.result == BuildResult.Succeeded;
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(nt, oldId);
                PlayerSettings.productName = oldName;
                PlayerSettings.enableFrameTimingStats = oldTiming;
                PlayerSettings.iOS.sdkVersion = oldSdk;
                AssetDatabase.SaveAssets();
            }
        }
    }
}
