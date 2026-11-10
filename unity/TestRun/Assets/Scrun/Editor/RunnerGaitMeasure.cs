using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Scrun;
using Scrun.App;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 아바타 발 속도 표 만들기(계획 0027). 아무 씬이나 Play한 뒤 메뉴 Scrun → Measure Runner Gait(몇 초).
// 러너용 Animator(블렌드 트리)를 붙인 아바타를 하나 따로 만들어 제자리에 두고, 블렌드 값 p마다 Animator를 1/240초씩 직접 돌리며
// "땅에 닿은 점(발목·발끝 네 점 중 가장 낮은 것)이 몸 기준으로 뒤로 밀리는 속도"의 중앙값을 잰다 = 그 블렌드를 1배로 틀 때 발이 땅을 미는 속도.
// 결과는 Assets/Scrun/App/Resources/RunnerGait.asset(RunnerAnimator가 읽음)과 data/avatar/<시각>_gait.csv(원본).
// Play 중에만 되는 이유: 에디터에서는 블렌드 트리를 돌려도 뼈가 움직이지 않는다(단일 클립 SampleAnimation만 됨).
// 클립 분석(GaitAnalysis, SampleAnimation)과 값이 다르다(11/11: 걷기 3.5 vs 6.2 km/h, 원인 미확인) → 화면에 나오는 실행 중 Animator 값을 쓴다.
// 캐릭터·동작·블렌드 기준점(Build Runner Animator)이 바뀌면 다시 잰다.
public static class RunnerGaitMeasure
{
    const string AssetPath = "Assets/Scrun/App/Resources/RunnerGait.asset"; // Resources.Load("RunnerGait")
    const float Step = 0.5f;        // 블렌드 값 간격(km/h)
    const float Dt = 1f / 240f;
    const int Warm = 240;           // 블렌드가 자리 잡을 때까지(1초)
    const int Samples = 720;        // 재는 길이(3초, 걸음 여러 번)

    [MenuItem("Scrun/Measure Runner Gait")]
    public static void Measure()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("[RunnerGait] 아무 씬이나 Play한 뒤에 실행(블렌드 트리는 Play 중에만 돌아감)");
            return;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerAnimatorBuilder.ModelPath);
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(RunnerAnimatorBuilder.ControllerPath);
        if (model == null || ctrl == null)
        {
            Debug.LogError($"[RunnerGait] 아바타나 컨트롤러가 없음: {RunnerAnimatorBuilder.ModelPath}, {RunnerAnimatorBuilder.ControllerPath}");
            return;
        }

        var rig = UnityEngine.Object.Instantiate(model, new Vector3(0f, -1000f, 0f), Quaternion.identity);
        rig.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var an = rig.GetComponent<Animator>();
            an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 카메라 밖이어도 돌게
            an.Rebind();
            var contacts = FootSlipProbe.ContactPoints(an); // 땅에 닿음 규칙은 FootSlipProbe와 같게
            if (contacts.Length == 0)
            {
                Debug.LogError("[RunnerGait] 아바타에 발목·발끝 뼈가 없음(휴머노이드 아바타여야 함) → 표를 바꾸지 않음");
                return;
            }
            var points = GridFromController(ctrl);
            if (points == null)
            {
                Debug.LogError($"[RunnerGait] {RunnerAnimatorBuilder.ControllerPath}의 기본 상태가 1D 블렌드 트리가 아님 → 표를 바꾸지 않음(Build Runner Animator로 다시 만들기)");
                return;
            }
            var feet = new float[points.Count];
            var csv = new StringBuilder("blend_param_kmh,foot_kmh,contact_frames\n");
            for (int i = 0; i < points.Count; i++)
            {
                feet[i] = FootKmh(an, rig.transform, contacts, points[i], out int n);
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2}", points[i], feet[i], n));
            }
            Save(points.ToArray(), feet, csv.ToString());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig);
        }
    }

    [MenuItem("Scrun/Measure Runner Gait", true)]
    static bool CanMeasure() => EditorApplication.isPlaying;

    // 블렌드 값 p를 1배로 틀 때 땅에 닿은 점이 뒤로 밀리는 속도(km/h, 중앙값)
    static float FootKmh(Animator an, Transform root, Transform[] contacts, float p, out int count)
    {
        an.speed = 1f;
        an.SetFloat(RunnerAnimator.SpeedParam, p);
        for (int i = 0; i < Warm; i++) an.Update(Dt);

        var prev = new Vector3[contacts.Length];
        for (int k = 0; k < contacts.Length; k++) prev[k] = root.InverseTransformPoint(contacts[k].position);
        float minY = float.MaxValue;
        var speeds = new List<float>();
        for (int i = 0; i < Samples; i++)
        {
            an.Update(Dt);
            int low = 0;
            var cur = new Vector3[contacts.Length];
            for (int k = 0; k < contacts.Length; k++)
            {
                cur[k] = root.InverseTransformPoint(contacts[k].position);
                if (cur[k].y < cur[low].y) low = k;
            }
            minY = Mathf.Min(minY + FootSlipProbe.MinRise * Dt, cur[low].y);
            if (cur[low].y < minY + FootSlipProbe.PlantedBand) speeds.Add(-(cur[low].z - prev[low].z) / Dt);
            prev = cur;
        }
        count = speeds.Count;
        if (count == 0) return 0f;
        speeds.Sort();
        return speeds[count / 2] * 3.6f;
    }

    // 블렌드 트리 기준점(서 있기 0 빼고)과 그 사이 0.5 km/h 간격. 기본 상태가 블렌드 트리가 아니면 null
    static List<float> GridFromController(AnimatorController ctrl)
    {
        if (ctrl.layers.Length == 0 || !(ctrl.layers[0].stateMachine.defaultState?.motion is BlendTree tree)) return null;
        var set = new SortedSet<float>();
        float lo = float.MaxValue, hi = 0f;
        foreach (var c in tree.children)
        {
            if (c.threshold <= 0f) continue;
            set.Add((float)Math.Round(c.threshold, 3));
            lo = Mathf.Min(lo, c.threshold);
            hi = Mathf.Max(hi, c.threshold);
        }
        for (float p = Mathf.Ceil(lo / Step) * Step; p < hi; p += Step)
            if (p - lo > 0.1f && hi - p > 0.1f) set.Add(p);
        return new List<float>(set);
    }

    static void Save(float[] points, float[] feet, string csv)
    {
        // 잘못된 표(발 속도 0 이하·순서 꼬임)로 좋은 표를 덮어쓰지 않는다(모든 러너가 표 없이 돌아가게 됨)
        var check = ScriptableObject.CreateInstance<RunnerGait>();
        check.Set(points, feet);
        bool valid = check.IsValid;
        UnityEngine.Object.DestroyImmediate(check);
        if (!valid)
        {
            Debug.LogError($"[RunnerGait] 잰 표가 잘못됨(발 속도 0 이하이거나 순서가 꼬임) → 저장하지 않음\n{csv}");
            return;
        }

        var gait = AssetDatabase.LoadAssetAtPath<RunnerGait>(AssetPath);
        bool created = gait == null;
        if (created)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            gait = ScriptableObject.CreateInstance<RunnerGait>();
        }
        gait.Set(points, feet);
        if (created) AssetDatabase.CreateAsset(gait, AssetPath);
        EditorUtility.SetDirty(gait);
        AssetDatabase.SaveAssets();

        // 원본은 저장소 data/avatar/에(CLAUDE.md: 실험 기록 원본은 data/)
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../data/avatar"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_gait.csv");
        File.WriteAllText(file, csv);
        Debug.Log($"[RunnerGait] 저장: {AssetPath}, 원본 {file}\n{csv}");
    }
}
