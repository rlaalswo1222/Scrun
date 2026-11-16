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
    public const string ModelPath = Dir + "Y Bot@Breathing Idle.fbx";
    public const string ControllerPath = Dir + "YBotRunner.controller";
    // 컨트롤러를 만들 때 쓴 클립의 지문(이름 · 길이 · 커브 수). 같이 커밋한다. 씬 만들기 때 이 컴퓨터의 클립과 비교
    public const string FingerprintPath = Dir + "YBotRunner.clips.txt";
    const string IdleClip = "Breathing Idle";

    // 느린 것부터. 실제 순서·기준점은 측정값으로 정함
    static readonly string[] MotionClips = { "Walking", "Jogging", "Slow Run", "Standard Run", "Fast Run", "Sprint" };

    // 가장 빠른 동작의 기준점(km/h). 이보다 빠르면 RunnerAnimator가 재생 속도를 올림
    public static float TopSpeedKmh { get; private set; } = DefaultTopKmh;
    const float DefaultTopKmh = 18f; // RunnerAnimator.maxClipKmh 기본값과 같음(클립이 없을 때)

    // 씬 만들기 메뉴(Build Island·TestTrack·Course Scene)가 부른다. 컨트롤러가 이미 있으면 그대로 쓴다(계획 0036):
    // 다시 재면 컴퓨터마다 기준점이 달리 나와(11/16 맥: 걷기 3.5 → 5.0) 커밋된 발 속도 표(RunnerGait.asset, 계획 0027)와 어긋남.
    // (기준점이 달리 나온 까닭은 11/11 a7fda83 "동작 5개가 서 있기 Avatar를 공유"로 클립 분석값이 바뀐 것: 11/16 Windows에서도 같은 값으로
    //  재현. 발 속도 표는 그 뒤에 실행 중 발 속도로 쟀으므로 지금 컨트롤러 + 표가 맞음 → 다시 재면 오히려 표와 어긋남)
    // 그대로 쓰기 전에 확인한다(#50 리뷰): 클립(FBX)이 빠졌으면 오류, 클립 지문(길이·커브 수)이 만들 때와 다르면 오류
    // (FBX는 저장소에 없어 각자 받음 → 다른 동작을 받았거나 캐릭터를 같은 이름으로 바꿨으면 발이 조용히 미끄러짐).
    // 발 속도를 다시 재서 비교하지 않는 까닭: 위처럼 가져오기 설정만 바뀌어도 분석값이 달라져 모든 컴퓨터에서 거짓 경보.
    // 어느 경우든 커밋된 컨트롤러·표를 바꾸지는 않는다. 없거나 모양이 틀렸을 때만 새로 잰다. 다시 재려면 메뉴 Build Runner Animator
    public static AnimatorController EnsureBuilt()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var tree = existing != null ? FindLocomotionTree(existing) : null;
        if (tree == null || TopThreshold(tree) <= 0f) return Build();

        TopSpeedKmh = TopThreshold(tree);
        CheckClips(tree);
        Debug.Log($"[Scrun] 러너 블렌드 트리: 있는 것 그대로({ControllerPath}, 가장 빠른 기준점 {TopSpeedKmh:0.0} km/h). 다시 재려면 Scrun → Build Runner Animator");
        return existing;
    }

    // 러너 블렌드 트리: 레이어 0 기본 상태의 1D 블렌드 트리, 파라미터 SpeedKmh(RunnerAnimator가 쓰는 것). 아니면 null.
    // 발 속도 재기(RunnerGaitMeasure)도 같은 규칙으로 찾는다
    public static BlendTree FindLocomotionTree(AnimatorController ctrl)
    {
        if (ctrl == null || ctrl.layers.Length == 0) return null;
        if (!(ctrl.layers[0].stateMachine.defaultState?.motion is BlendTree tree)) return null;
        if (tree.blendType != BlendTreeType.Simple1D || tree.blendParameter != "SpeedKmh") return null;
        foreach (var p in ctrl.parameters)
            if (p.name == "SpeedKmh" && p.type == AnimatorControllerParameterType.Float) return tree;
        return null;
    }

    static float TopThreshold(BlendTree tree)
    {
        float top = 0f;
        foreach (var child in tree.children)
            if (child.motion != null) top = Mathf.Max(top, child.threshold);
        return top;
    }

    // 클립이 다 있는지, 지문이 컨트롤러를 만들 때와 같은지. 다르면 오류로 알리기만 함
    static void CheckClips(BlendTree tree)
    {
        var missing = new List<string>();
        foreach (var child in tree.children)
            if (child.motion == null) missing.Add($"기준점 {child.threshold:0.0} km/h");
        foreach (var name in MotionClips)
            if (!LoadClip($"{Dir}Y Bot@{name}.fbx")) missing.Add(name);
        if (missing.Count > 0)
            Debug.LogError($"[Scrun] 클립 없음: {string.Join(", ", missing)} — Mixamo FBX를 {Dir}에 받아야 함(저장소엔 없음). 빠진 채로 빌드하면 그 속도에서 아바타가 T자 자세");

        string now = Fingerprint(tree);
        string saved = System.IO.File.Exists(FingerprintPath) ? System.IO.File.ReadAllText(FingerprintPath).Replace("\r\n", "\n") : null;
        if (saved == null)
        {
            // 지문이 생기기 전에 만든 컨트롤러(11/16 이전): 지금 클립을 기준으로 처음 적어 둠 → 같이 커밋
            System.IO.File.WriteAllText(FingerprintPath, now);
            AssetDatabase.ImportAsset(FingerprintPath);
            Debug.Log($"[Scrun] 러너 클립 지문을 처음 적음: {FingerprintPath}(커밋할 것)");
        }
        else if (saved != now)
        {
            Debug.LogError("[Scrun] 이 컴퓨터의 클립이 러너 애니메이터를 만들 때와 다름 → 아바타 발이 미끄러질 수 있음. 컨트롤러는 그대로 둠\n" +
                           $"  만들 때:\n{saved}  지금:\n{now}" +
                           "  같은 동작 FBX를 다시 받거나, 캐릭터·동작을 바꾼 거면 Build Runner Animator → Play 중 Measure Runner Gait → 씬 다시 만들기, 넷(지문 포함)을 같이 커밋");
        }
    }

    // 블렌드 트리 클립마다 "이름 길이(초) 커브 수" 한 줄. 같은 FBX·같은 자르기면 컴퓨터가 달라도 같음
    static string Fingerprint(BlendTree tree)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in tree.children)
        {
            if (!(child.motion is AnimationClip clip)) continue;
            int curves = AnimationUtility.GetCurveBindings(clip).Length;
            sb.Append(clip.name).Append(' ')
              .Append(clip.length.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
              .Append(curves).Append('\n');
        }
        return sb.ToString();
    }

    // 클립을 다시 재서 컨트롤러를 새로 만든다(있던 것은 지움). 기준점이 바뀌면 발 속도 표도 다시 재야 함
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
        if (entries.Count == 0)
        {
            // 클립이 하나도 없으면 만들지 않음: 기준점 0이면 maxClipKmh 0(→ 0으로 나누기)이고 씬을 만들 때마다 다시 만들게 됨
            Debug.LogError($"[Scrun] 달리기 클립이 하나도 없어 러너 애니메이터를 만들지 않음 — Mixamo FBX를 {Dir}에 받기");
            var kept = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var keptTree = kept != null ? FindLocomotionTree(kept) : null;
            TopSpeedKmh = keptTree != null && TopThreshold(keptTree) > 0f ? TopThreshold(keptTree) : DefaultTopKmh;
            return kept;
        }

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
        System.IO.File.WriteAllText(FingerprintPath, Fingerprint(tree)); // 이 클립으로 만들었다는 지문(같이 커밋)
        AssetDatabase.ImportAsset(FingerprintPath);
        log.AppendLine("  기준점이 바뀌었으면 Play 중 Scrun → Measure Runner Gait로 발 속도 표를 다시 잴 것(계획 0027)");
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
