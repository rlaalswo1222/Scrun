using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 러너용 Animator Controller 생성: 파라미터 SpeedKmh 하나, 1D 블렌드 트리 하나.
// 기준점·박자는 클립을 직접 재서 정한다(GaitAnalysis) → 캐릭터/동작을 바꿔도 다시 실행만 하면 됨.
//  1) 걸음 주기가 여러 번 든 클립은 왼발 착지~다음 왼발 착지 1주기로 자름(박자 맞춤의 전제)
//  2) 기준점 = 디딘 발 속도 → 그 속도에서 발이 바닥에 붙어 보임
//  3) 각 클립을 왼발 착지에서 시작하도록 cycleOffset → 블렌드 중 다리 꼬임 방지
public static class RunnerAnimatorBuilder
{
    const string Dir = MixamoImportSetup.Folder + "YBot/";
    const string ModelPath = Dir + "Y Bot@Breathing Idle.fbx";
    const string ControllerPath = Dir + "YBotRunner.controller";
    const string IdleClip = "Breathing Idle";

    // 느린 것부터. 실제 순서·기준점은 측정값으로 정함
    static readonly string[] MotionClips = { "Walking", "Jogging", "Slow Run", "Standard Run", "Fast Run", "Sprint" };

    // 가장 빠른 동작의 기준점(km/h). 이보다 빠르면 RunnerAnimator가 재생 속도를 올림
    public static float TopSpeedKmh { get; private set; }

    [MenuItem("Scrun/Build Runner Animator")]
    public static AnimatorController Build()
    {
        var rig = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        rig.hideFlags = HideFlags.HideAndDontSave;
        rig.GetComponent<Animator>().runtimeAnimatorController = null;

        var entries = new List<(AnimationClip clip, float kmh, float offset)>();
        var log = new System.Text.StringBuilder("[Scrun] 러너 블렌드 트리\n");
        try
        {
            foreach (var name in MotionClips)
            {
                string path = $"{Dir}Y Bot@{name}.fbx";
                var clip = LoadClip(path);
                if (!clip) { Debug.LogError($"[Scrun] 클립 없음: {name}"); continue; }

                var g = GaitAnalysis.Analyze(rig, clip);
                if (g.LeftStrikes.Count >= 2 && TrimToOneCycle(path, g.LeftStrikes[0], g.LeftStrikes[1]))
                {
                    clip = LoadClip(path);
                    g = GaitAnalysis.Analyze(rig, clip);
                }

                float offset = g.LeftStrikes.Count > 0 ? Mathf.Repeat(g.LeftStrikes[0] / g.Length, 1f) : 0f;
                entries.Add((clip, g.FootSpeedKmh, offset));
                log.AppendLine($"  {name}: 발 속도 {g.FootSpeedKmh:0.0} km/h, 길이 {g.Length:0.000}s, 왼발 착지 {offset:0.00}");
            }
        }
        finally
        {
            Object.DestroyImmediate(rig);
        }

        entries.Sort((a, b) => a.kmh.CompareTo(b.kmh));

        AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("SpeedKmh", AnimatorControllerParameterType.Float);

        ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "SpeedKmh";
        tree.useAutomaticThresholds = false;

        var idle = LoadClip($"{Dir}Y Bot@{IdleClip}.fbx");
        if (idle) tree.AddChild(idle, 0f);

        float last = 0f;
        foreach (var (clip, kmh, _) in entries)
        {
            // 기준점은 반드시 증가해야 함
            float th = Mathf.Max(kmh, last + 0.1f);
            tree.AddChild(clip, th);
            last = th;
        }

        // cycleOffset은 children 배열을 통째로 다시 넣어야 반영됨
        var children = tree.children;
        int start = idle ? 1 : 0;
        for (int i = 0; i < entries.Count; i++) children[start + i].cycleOffset = entries[i].offset;
        tree.children = children;

        TopSpeedKmh = last;
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
        return ctrl;
    }

    // 원본 그대로인 클립을 [from, to]초 구간 하나로 자른다. 이미 잘려 있으면 false
    static bool TrimToOneCycle(string path, float from, float to)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        var original = mi.defaultClipAnimations[0];
        var clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
        if (!Mathf.Approximately(clips[0].firstFrame, original.firstFrame) ||
            !Mathf.Approximately(clips[0].lastFrame, original.lastFrame)) return false;

        const float fps = 30f;   // Mixamo 내보내기 fps
        clips[0].firstFrame = original.firstFrame + from * fps;
        clips[0].lastFrame = original.firstFrame + to * fps;
        mi.clipAnimations = clips;
        mi.SaveAndReimport();
        return true;
    }

    static AnimationClip LoadClip(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }
}
