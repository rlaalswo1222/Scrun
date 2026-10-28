using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Scrun.SpeedLab;

// 러닝머신 속도 실험 앱: 씬 만들기 + 따로 설치되는 APK(com.scrun.speedlab) 빌드.
// 빌드 동안만 플레이어 설정을 바꾸고 끝나면 원래(Scrun 본 앱 설정)로 되돌린다.
// 빠른 빌드를 위해 Mono + ARMv7 (IL2CPP C++ 컴파일 없음). 본 앱은 그대로 IL2CPP·ARM64.
public static class SpeedLabBuilder
{
    const string ScenePath = "Assets/Scrun/SpeedLab/SpeedLab.unity";

    [MenuItem("Scrun/Build SpeedLab Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(16, 22, 28, 255);
        var go = new GameObject("SpeedLab", typeof(TreadmillSensor), typeof(SpeedLab), typeof(SpeedLabUI));
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("SpeedLab 씬 저장: " + ScenePath);
    }

    [MenuItem("Scrun/Build SpeedLab APK")]
    public static void BuildApk()
    {
        if (!File.Exists(ScenePath)) BuildScene();
        var g = NamedBuildTarget.Android;
        string id = PlayerSettings.GetApplicationIdentifier(g), product = PlayerSettings.productName;
        var backend = PlayerSettings.GetScriptingBackend(g);
        var arch = PlayerSettings.Android.targetArchitectures;
        var orient = PlayerSettings.defaultInterfaceOrientation;
        bool p = PlayerSettings.allowedAutorotateToPortrait, pu = PlayerSettings.allowedAutorotateToPortraitUpsideDown,
            l = PlayerSettings.allowedAutorotateToLandscapeLeft, r = PlayerSettings.allowedAutorotateToLandscapeRight;
        try
        {
            PlayerSettings.SetApplicationIdentifier(g, "com.scrun.speedlab");
            PlayerSettings.productName = "Scrun 속도실험";
            PlayerSettings.SetScriptingBackend(g, ScriptingImplementation.Mono2x);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = PlayerSettings.allowedAutorotateToLandscapeRight = true;

            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/SpeedLab.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"SpeedLab APK: {s.result} {s.totalSize / 1e6:0.0}MB {s.totalTime.TotalSeconds:0}s → {outPath}");
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(g, id);
            PlayerSettings.productName = product;
            PlayerSettings.SetScriptingBackend(g, backend);
            PlayerSettings.Android.targetArchitectures = arch;
            PlayerSettings.defaultInterfaceOrientation = orient;
            PlayerSettings.allowedAutorotateToPortrait = p; PlayerSettings.allowedAutorotateToPortraitUpsideDown = pu;
            PlayerSettings.allowedAutorotateToLandscapeLeft = l; PlayerSettings.allowedAutorotateToLandscapeRight = r;
            AssetDatabase.SaveAssets();
        }
    }
}
