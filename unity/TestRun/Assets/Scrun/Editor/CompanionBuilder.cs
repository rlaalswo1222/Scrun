using System.Collections.Generic;
using Scrun;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ScrunEditorUtil;

// 함께 달리는 사람들: 페이스 봇(정해진 페이스), 고스트(지난번 내 기록), 이름표·근처 러너 목록, 하늘의 새 무리
public static class CompanionBuilder
{
    // (이름, 속도 km/h, 옷 색, 좌우 위치, 시작 거리)
    static readonly (string name, float kmh, Color color, float lateral, float start)[] Bots =
    {
        ("페이스 7:30 · 하늘", 8.0f, new Color(0.35f, 0.75f, 1.0f), -2.2f, 40f),
        ("페이스 6:40 · 민트", 9.0f, new Color(0.35f, 0.9f, 0.6f), 1.8f, 90f),
        ("페이스 6:00 · 코랄", 10.0f, new Color(1.0f, 0.5f, 0.4f), -1.0f, 150f),
        ("페이스 5:20 · 레몬", 11.3f, new Color(1.0f, 0.85f, 0.25f), 2.4f, 30f),
        ("페이스 4:45 · 보라", 12.6f, new Color(0.7f, 0.5f, 1.0f), -2.6f, 200f),
    };

    public static void Build(RouteTrack route, RunnerController player, RuntimeAnimatorController controller,
                             IEnumerable<(Vector3 center, float radius)> birdSpots)
    {
        var root = new GameObject("Companions").transform;

        // 플레이어 정보·기록
        var tag = player.gameObject.AddComponent<RunnerTag>();
        tag.displayName = "나";
        tag.color = new Color(1f, 0.55f, 0.15f);
        tag.kind = RunnerTag.Kind.Player;
        tag.runner = player;
        player.gameObject.AddComponent<GhostRecorder>().route = route;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerRigBuilder.CharacterModelPath);
        int i = 0;
        foreach (var (name, kmh, color, lateral, start) in Bots)
        {
            var go = new GameObject($"Bot_{i++}");
            go.transform.SetParent(root, false);
            var speed = go.AddComponent<PaceSpeed>();
            speed.targetKmh = kmh;
            var rc = go.AddComponent<RunnerController>();
            rc.Setup(speed, route, lateral, start);
            go.transform.position = new Vector3(0f, CourseRoadBuilder.RoadY, 0f);
            Body(go.transform, model, controller, rc, BotMaterial($"Runner_Bot{i}", color));
            var t = go.AddComponent<RunnerTag>();
            t.displayName = name; t.color = color; t.kind = RunnerTag.Kind.Bot; t.runner = rc;
        }

        // 함께 달리는 무리: 옷 색이 제각각인 러너들이 길을 채움(이름표 없음). 품질 단계에 따라 일부만 켬
        var shirts = new[]
        {
            new Color(0.92f, 0.26f, 0.22f), new Color(0.98f, 0.62f, 0.15f), new Color(0.95f, 0.85f, 0.25f), new Color(0.30f, 0.75f, 0.35f),
            new Color(0.20f, 0.55f, 0.90f), new Color(0.55f, 0.35f, 0.85f), new Color(0.95f, 0.45f, 0.70f), new Color(0.95f, 0.95f, 0.95f),
            new Color(0.25f, 0.25f, 0.30f), new Color(0.15f, 0.70f, 0.75f), new Color(0.80f, 0.55f, 0.35f), new Color(0.60f, 0.85f, 0.25f),
        };
        var crowdRng = new System.Random(31);
        for (int c = 0; c < shirts.Length; c++)
        {
            var go = new GameObject($"Crowd_{c}");
            go.transform.SetParent(root, false);
            var speed = go.AddComponent<PaceSpeed>();
            speed.targetKmh = 8.2f + (float)crowdRng.NextDouble() * 4.8f;
            speed.wobble = 0.4f;
            var rc = go.AddComponent<RunnerController>();
            float lateral = ((float)crowdRng.NextDouble() - 0.5f) * 6.4f;
            rc.Setup(speed, route, lateral, 4f + c * 11f + (float)crowdRng.NextDouble() * 5f);
            go.transform.position = new Vector3(0f, CourseRoadBuilder.RoadY, 0f);
            Body(go.transform, model, controller, rc, BotMaterial($"Runner_Crowd{c}", shirts[c]));
            var t = go.AddComponent<RunnerTag>();
            t.displayName = $"러너 {c + 1}"; t.color = shirts[c]; t.kind = RunnerTag.Kind.Bot; t.runner = rc; t.crowd = true;
        }

        // 고스트(기록이 있을 때만 보임)
        {
            var go = new GameObject("Ghost");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, CourseRoadBuilder.RoadY, 0f);
            var gs = go.AddComponent<GhostSpeed>();
            gs.route = route;
            var rc = go.AddComponent<RunnerController>();
            rc.Setup(gs, route, 1.0f, 0f);
            Body(go.transform, model, controller, rc, GhostMaterial());
            var t = go.AddComponent<RunnerTag>();
            t.displayName = "지난번의 나"; t.color = new Color(0.4f, 0.95f, 1f); t.kind = RunnerTag.Kind.Ghost; t.runner = rc;
        }

        new GameObject("RunnerLabels").AddComponent<RunnerLabels>();

        // 새 무리
        var birdMat = VegetationBuilder.StyMat("Sty_Bird", 0f, 1f, true, new Color(0.15f, 0.15f, 0.17f));
        foreach (var (c, r) in birdSpots)
        {
            var f = new GameObject("BirdFlock").AddComponent<BirdFlock>();
            f.transform.SetParent(root, false);
            f.transform.position = c;
            f.radius = r;
            f.material = birdMat;
        }
    }

    static void Body(Transform parent, GameObject model, RuntimeAnimatorController controller, RunnerController rc, Material mat)
    {
        if (!model || !controller) return;
        var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
        body.name = "Body";
        body.transform.SetParent(parent, false);
        var anim = body.GetComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullCompletely;   // 화면 밖이면 애니메이션 계산 안 함(모바일)
        var ra = body.AddComponent<RunnerAnimator>();
        SetRef(ra, "runner", rc);
        SetFloat(ra, "maxClipKmh", RunnerAnimatorBuilder.TopSpeedKmh);
        var smrs = body.GetComponentsInChildren<SkinnedMeshRenderer>();
        var full = new Mesh[smrs.Length];
        var low = new Mesh[smrs.Length];
        var mid = new Mesh[smrs.Length];
        for (int k = 0; k < smrs.Length; k++)
        {
            var smr = smrs[k];
            var mats = smr.sharedMaterials;
            for (int j = 0; j < mats.Length; j++) mats[j] = mat;
            smr.sharedMaterials = mats;
            smr.lightProbeUsage = LightProbeUsage.Off;
            full[k] = smr.sharedMesh;
            low[k] = LowMesh(smr.sharedMesh);
            mid[k] = LowMesh(smr.sharedMesh, 0.02f, "_Mid");
        }
        body.AddComponent<SkinnedLod>().Set(smrs, full, mid, low);
    }

    static readonly Dictionary<Mesh, Mesh> lowCache = new();

    // 멀리용 단순화 메쉬(약 5% 삼각형). 같은 원본은 한 번만 만든다
    static readonly Dictionary<Mesh, Mesh> midCache = new();

    // cell: 정점을 합치는 격자 크기(m). 멀리용 4.5cm, 중간 2cm
    static Mesh LowMesh(Mesh src, float cell = 0.045f, string suffix = "_Low")
    {
        var cache = suffix == "_Low" ? lowCache : midCache;
        if (cache.TryGetValue(src, out var m) && m) return m;
        var dec = SkinnedDecimator.Decimate(src, cell, $"{src.name}{suffix}");
        m = SaveMesh(dec, Root + "/Characters/YBot", $"{src.name}{suffix}");
        Debug.Log($"[Scrun] 단순화 {src.name}: {src.triangles.Length / 3} → {m.triangles.Length / 3} 삼각형");
        return cache[src] = m;
    }

    static Material BotMaterial(string name, Color color) => MakeMat(name, color, smoothness: 0.35f);

    // 고스트: 반투명 하늘색 + 약한 발광
    static Material GhostMaterial()
    {
        string path = $"{Root}/Materials/Runner_Ghost.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", new Color(0.45f, 0.95f, 1f, 0.38f));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
