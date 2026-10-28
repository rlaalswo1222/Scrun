using System.Collections.Generic;
using Scrun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static ScrunEditorUtil;

// 메뉴 Scrun → Build Course Scene : 첫 가상 코스 "노을 호수 코스"(3km 루프)를 처음부터 만든다(덮어씀)
// 설계: docs/unity/course_preview.html
public static class CourseSceneBuilder
{
    const string ScenePath = Root + "/Scenes/Course01_SunsetLake.unity";
    internal const string GenDir = Root + "/Course/Generated";
    const float TargetLap = 3000f;
    const float ForestFrom = 1526f, ForestTo = 2180f;   // 숲길 구간(설계안 4번)

    // 제어점(x,z, m). TargetLap에 맞게 전체를 같은 비율로 키운다. 반시계 방향.
    static readonly Vector2[] RawPoints =
    {
        new(0, 0), new(200, 0), new(360, 40), new(470, 150), new(500, 300), new(440, 440),
        new(300, 520), new(150, 540), new(40, 610), new(-80, 600), new(-140, 500), new(-260, 440),
        new(-390, 330), new(-430, 180), new(-360, 50), new(-200, -20), new(-90, -20),
    };

    [MenuItem("Scrun/Build Course Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureFolderPath(GenDir);
        EnsureFolder(Root, "Scenes");
        EnsureFolder(Root, "Settings");

        var runnerController = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerRigBuilder.CharacterModelPath)
            ? RunnerAnimatorBuilder.Build() : null;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ---- 코스 곡선 ----
        var courseGo = new GameObject("Course");
        var track = courseGo.AddComponent<SplineTrack>();
        track.SetPoints(ToPoints(1f));
        float scale = TargetLap / track.LapLength;
        track.SetPoints(ToPoints(scale));

        // ---- 지형 ----
        var bounds = TrackBounds(track);
        var terrain = new CourseTerrainBuilder
        {
            Center = new Vector2(bounds.center.x, bounds.center.z),
        };
        // 호숫가 구간(설계 0.44~1.19km): 왼쪽 물가가 도로 중앙에서 16m
        terrain.AddLakeAlong(track, 470f, 1160f, 16f, 45f, 140f);
        terrain.BuildDistanceField(track);
        var terrainMat = TerrainMaterial(terrain);
        var terrainRoot = new GameObject("Terrain").transform;
        AddTerrainChunks(terrainRoot, terrain.BuildChunks(), terrainMat, GenDir + "/Terrain");

        // ---- 도로(갓길은 지형 색으로) ----
        var roadMats = RoadMaterials();
        var roadRoot = new GameObject("Road").transform;
        roadRoot.SetParent(courseGo.transform, false);
        foreach (var (mesh, origin) in CourseRoadBuilder.Build(track, "Road", 0f, -1f, false, p => terrain.GroundColorLinear(p.x, p.z)))
        {
            var saved = SaveMesh(mesh, GenDir, mesh.name);
            var go = new GameObject(saved.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(roadRoot, false);
            go.transform.position = origin;
            go.GetComponent<MeshFilter>().sharedMesh = saved;
            go.GetComponent<MeshRenderer>().sharedMaterials = roadMats;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // ---- 호수 수면 ----
        var lakeRect = terrain.LakeBounds();
        var heights = WaterBuilder.BakeHeights("Course01", terrain);
        var lakeMat = WaterBuilder.Material("Water_Lake01", heights, -0.2f, WaterBuilder.LakeShallow, WaterBuilder.LakeDeep, 3.5f, 0.06f, maxDepth: 8f);
        WaterBuilder.Plane("Lake", new Vector3(lakeRect.center.x, -0.2f, lakeRect.center.y), lakeRect.width + 40f, lakeRect.height + 40f, lakeMat);

        // ---- 출발선 · 아치 ----
        BuildStart(track);

        // ---- 하늘 · 빛 · 안개 · 후처리 ----
        var sun = SetupLighting();
        SkyBuilder.Setup(sun);
        var post = SetupPostProcessing();

        // ---- 러너 · 카메라 · HUD ----
        // ---- 식생 ----
        var forestPts = new List<Vector3>();
        for (float d = ForestFrom; d <= ForestTo; d += 10f) forestPts.Add(track.GetPoint(d));
        VegetationBuilder.Build(new IPath[] { track }, terrain,
                                p => forestPts.Exists(f => (f - p).sqrMagnitude < 70f * 70f) ? "forest" : "lake",
                                GenDir + "/Vegetation_Course01.bytes");

        RunnerRigBuilder.Create(track, runnerController, CourseRoadBuilder.RoadY);
        SetRef(new GameObject("Quality").AddComponent<ScrunQuality>(), "postVolume", post);
        var cam = Camera.main;
        cam.farClipPlane = 4000f;
        cam.nearClipPlane = 0.2f;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.None;   // MSAA(URP 에셋 4x)가 계단을 처리

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        BakeEnvironment();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        Debug.Log($"[Scrun] {ScenePath} 생성 완료. 한 바퀴 {track.LapLength:0} m");
    }

    static IEnumerable<Vector3> ToPoints(float scale)
    {
        foreach (var p in RawPoints) yield return new Vector3(p.x * scale, 0f, p.y * scale);
    }

    static Bounds TrackBounds(Track track)
    {
        var b = new Bounds(track.GetPoint(0f), Vector3.zero);
        for (float d = 0; d < track.LapLength; d += 10f) b.Encapsulate(track.GetPoint(d));
        return b;
    }

    // ---------- 도로 재질 (서브메쉬 순서 = CourseRoadBuilder.Part) ----------
    internal const string PHTex = PolyHavenImportSetup.Folder + "Textures/";

    internal static Material[] RoadMaterials()
    {
        // 손그림풍 아스팔트: 푸른 기운 도는 짙은 회색 + 잔얼룩, 흰 가장자리선 + 노란 중앙 겹선. u=폭 8m, v=길이 6m
        // 바퀴 자국 두 줄은 살짝 밝게, 가장자리 30cm는 흙먼지로 어둡고 누렇게(갓길 흙과 이어짐)
        const int W = 1024, H = 768;
        var col = new Color[W * H];
        var baseC = new Color(0.29f, 0.31f, 0.35f);
        var white = new Color(0.93f, 0.93f, 0.90f);
        var yellow = new Color(0.97f, 0.78f, 0.18f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float n = TileNoise(u, v, 6, 3f) * 0.6f + TileNoise(u, v, 40, 11f) * 0.4f;
                var c = baseC * (0.92f + 0.12f * n);
                // 바퀴 자국처럼 살짝 밝은 두 줄
                float xm = (u - 0.5f) * 8f;
                c *= 1f + 0.07f * (Mathf.Exp(-Mathf.Pow((Mathf.Abs(xm) - 1.8f) / 0.55f, 2)));
                float grit = Mathf.InverseLerp(3.7f, 4f, Mathf.Abs(xm)) * (0.6f + 0.4f * TileNoise(u, v, 30, 7f));
                c = Color.Lerp(c, new Color(0.33f, 0.30f, 0.25f), grit * 0.6f);
                var (edge, center) = LaneMark(u, v);
                c = Color.Lerp(c, white, edge);
                c = Color.Lerp(c, yellow, center);
                c.a = 1f;
                col[y * W + x] = c;
            }
        var asphalt = VegetationBuilder.StyMat("Course_Asphalt", 0f, 1f, false);
        asphalt.SetTexture("_BaseMap", SaveTexture("Course_Asphalt", W, H, col));
        asphalt.SetFloat("_Wrap", 0.3f);
        asphalt.SetFloat("_RimStrength", 0f);
        // 월드 좌표 얼룩 두 겹(40m 보수 자국, 7m 잔얼룩) → 6m 타일 반복이 보이지 않게
        asphalt.SetTexture("_DetailMap", PatchNoise());
        asphalt.SetFloat("_DetailScale", 0.025f);
        asphalt.SetFloat("_DetailStrength", 0.35f);
        asphalt.SetFloat("_DetailScale2", 0.14f);
        asphalt.SetFloat("_DetailStrength2", 0.18f);

        // 예전 주황 발광선 자리: 아스팔트 가장자리 색(보이지 않게)
        var glow = VegetationBuilder.StyMat("Course_Glow", 0f, 1f, false, baseC);
        glow.SetTexture("_BaseMap", null);
        // 연석: 밝은 회백색
        var curb = VegetationBuilder.StyMat("Course_Curb", 0f, 1f, false, new Color(0.82f, 0.82f, 0.80f));
        curb.SetTexture("_BaseMap", null);
        // 갓길: 정점 색(흙 → 그 자리 지형 색) + 지형과 같은 잔결 → 바깥 끝이 지형과 이어져 경계가 안 보임
        var shoulder = VegetationBuilder.StyMat("Course_Gravel", 0f, 1f, false, Color.white);
        shoulder.SetTexture("_BaseMap", null);
        shoulder.SetTexture("_DetailMap", DetailNoise());
        shoulder.SetFloat("_DetailScale", 0.18f);
        shoulder.SetFloat("_DetailStrength", 0.6f);
        shoulder.SetFloat("_DetailScale2", 0.9f);
        shoulder.SetFloat("_DetailStrength2", 0.3f);
        shoulder.SetFloat("_Wrap", 0.3f);
        shoulder.SetFloat("_RimStrength", 0f);
        foreach (var m in new[] { asphalt, glow, curb, shoulder }) EditorUtility.SetDirty(m);
        return new[] { asphalt, glow, curb, shoulder };
    }

    // 나무 데크 노면: 길을 가로지르는 판자(폭 25cm, 판마다 색·결 다름, 판 사이 틈·못 자국). 나머지 부분 재질은 도로와 같음
    internal static Material[] BoardwalkMaterials(Material[] road)
    {
        const int W = 512, H = 768;
        var col = new Color[W * H];
        var woodA = new Color(0.58f, 0.42f, 0.28f); var woodB = new Color(0.46f, 0.32f, 0.21f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float along = v * 24f;                       // 6m에 판 24장
                int board = Mathf.FloorToInt(along);
                float fb = along - board;
                float tone = TileNoise(Mathf.Repeat(board * 0.37f, 1f), 0.5f, 4, 3f);
                var c = Color.Lerp(woodB, woodA, 0.3f + 0.6f * tone);
                float grain = TileNoise(u, v * 0.25f + board * 0.13f, 32, 11f);
                c *= 0.88f + 0.2f * grain;
                if (fb < 0.07f) c *= 0.35f;                  // 판 사이 틈
                float xm = u * 8f;
                if (fb > 0.4f && fb < 0.6f && (Mathf.Abs(xm - 1.2f) < 0.04f || Mathf.Abs(xm - 6.8f) < 0.04f)) c *= 0.6f;   // 못 자리
                c.a = 1f;
                col[y * W + x] = c;
            }
        var deck = VegetationBuilder.StyMat("Course_Boardwalk", 0f, 1f, false);
        deck.SetTexture("_BaseMap", SaveTexture("Course_Boardwalk", W, H, col));
        deck.SetTexture("_DetailMap", PatchNoise());
        deck.SetFloat("_DetailScale", 0.05f);
        deck.SetFloat("_DetailStrength", 0.25f);
        deck.SetFloat("_Wrap", 0.35f);
        EditorUtility.SetDirty(deck);
        var mats = (Material[])road.Clone();
        mats[(int)CourseRoadBuilder.Part.Asphalt] = deck;
        return mats;
    }

    // 차선: 양쪽 흰 차선(중앙에서 3.5m, 폭 12cm) + 노란 중앙 겹선(±0.12m, 폭 10cm). u=폭 8m, v=길이 6m
    internal static (float edge, float center) LaneMark(float u, float v)
    {
        float x = (u - 0.5f) * 8f;
        float Line(float dist, float halfW) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfW, halfW + 0.02f, dist));
        float edge = Line(Mathf.Abs(Mathf.Abs(x) - 3.5f), 0.06f);
        float center = Line(Mathf.Abs(Mathf.Abs(x) - 0.12f), 0.05f);
        float wear = 0.85f + 0.15f * TileNoise(u, v, 24, 40f);
        return (edge * wear, center * wear);
    }

    // 아스팔트 얼룩(평균 0.5): 보수한 자리처럼 경계가 또렷한 큰 덩어리 + 잔얼룩
    internal static Texture2D PatchNoise() => MakeTexture("Sty_PatchNoise", 256, 256, (u, v) =>
    {
        float big = TileNoise(u, v, 4, 3f) * 0.6f + TileNoise(u, v, 9, 8f) * 0.4f;
        float patch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.64f, big));
        float fine = TileNoise(u, v, 48, 21f);
        float g = 0.5f + 0.09f * (fine - 0.5f) * 2f - 0.07f * patch + 0.05f * (TileNoise(u, v, 16, 2f) - 0.5f) * 2f;
        return new Color(g, g, g, 1f);
    }, TexKind.Linear);

    // 지형 색 지도에 그늘 굽기: 나무·덤불·바위 밑을 어둡고 약간 차갑게(가장자리는 부드럽게). 실행 중 비용 없음
    // (풀은 같은 색 지도로 물들므로 그늘 속 풀도 함께 어두워짐)
    internal static void BakeGroundShade(Material terrainMat, CourseTerrainBuilder terrain, IEnumerable<(Vector2 p, float r, float k)> shades)
    {
        var tex = terrainMat.GetTexture("_BaseMap") as Texture2D;
        if (!tex) return;
        string path = AssetDatabase.GetAssetPath(tex);
        var img = RawImage.Load(path);
        int w = img.W, h = img.H;
        var gr = terrain.GroundRect;   // xMin, zMin, 1/size, 1/size
        float texel = 1f / (gr.z * w);   // 텍셀 하나의 m
        var dark = new float[w * h];
        for (int i = 0; i < dark.Length; i++) dark[i] = 1f;
        int count = 0;
        foreach (var (p, r, k) in shades)
        {
            count++;
            float u = (p.x - gr.x) * gr.z * w, v = (p.y - gr.y) * gr.w * h;
            float rt = r * 1.25f / texel;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(u - rt)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(u + rt));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(v - rt)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(v + rt));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - u) * (x + 0.5f - u) + (y + 0.5f - v) * (y + 0.5f - v)) / Mathf.Max(0.5f, rt);
                    if (d >= 1f) continue;
                    float f = 1f - k * (1f - Mathf.SmoothStep(0f, 1f, d));
                    int i = y * w + x;
                    dark[i] *= f;   // 겹치는 숲은 더 짙게
                }
        }
        var px = img.Px;
        var cool = new Color(0.78f, 0.86f, 1f);
        for (int i = 0; i < px.Length; i++)
        {
            float f = Mathf.Max(0.42f, dark[i]);
            if (f >= 0.999f) continue;
            var c = px[i];
            var shaded = Color.Lerp(c * cool, c, 0.4f) * f;
            px[i] = new Color(shaded.r, shaded.g, shaded.b, 1f);
        }
        // RawImage는 선형으로 읽음 → 같은 값을 그대로 다시 저장(색 공간 유지)
        var t2 = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        t2.SetPixels(px);
        System.IO.File.WriteAllBytes(path, t2.EncodeToPNG());
        Object.DestroyImmediate(t2);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Scrun] 땅 그늘 굽기: {count:N0}개");
    }

    // 붓결 잔결(평균 0.5): 짧게 비스듬한 풀 붓 터치 → 가까운 땅이 매끈한 잔디밭이 아니라 손으로 칠한 풀밭처럼
    internal static Texture2D GrassStrokes()
    {
        const int N = 256;
        var px = new float[N * N];
        for (int i = 0; i < px.Length; i++) px[i] = 0.5f;
        var rnd = new System.Random(77);
        float R() => (float)rnd.NextDouble();
        for (int k = 0; k < 2600; k++)
        {
            float x = R() * N, y = R() * N, len = 5f + R() * 9f, ang = (70f + (R() - 0.5f) * 40f) * Mathf.Deg2Rad;
            float val = 0.5f + (R() - 0.5f) * 0.42f;
            float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
            for (float t = 0; t < len; t += 0.5f)
            {
                float wdt = 1.6f * (1f - t / len) + 0.4f;
                for (float o = -wdt; o <= wdt; o += 0.5f)
                {
                    int ix = ((int)(x + dx * t - dy * o) % N + N) % N, iy = ((int)(y + dy * t + dx * o) % N + N) % N;
                    px[iy * N + ix] = Mathf.Lerp(px[iy * N + ix], val, 0.7f);
                }
            }
        }
        return MakeTexture("Sty_GrassStrokes", N, N, (u, v) =>
        {
            float g = px[Mathf.Min(N - 1, (int)(v * N)) * N + Mathf.Min(N - 1, (int)(u * N))];
            return new Color(g, g, g, 1f);
        }, TexKind.Linear);
    }

    // 회색 잔결(평균 0.5, Stylized 셰이더의 x2 디테일용)
    internal static Texture2D DetailNoise() => MakeTexture("Sty_DetailNoise", 256, 256, (u, v) =>
    {
        float n = TileNoise(u, v, 32, 1f) * 0.5f + TileNoise(u, v, 8, 5f) * 0.3f + TileNoise(u, v, 64, 13f) * 0.2f;
        float g = 0.35f + 0.3f * n;
        return new Color(g, g, g, 1f);
    }, TexKind.Linear);

    // 지형 청크: 가까이 자세한 메쉬, 멀리 거친 메쉬(LODGroup)
    internal static void AddTerrainChunks(Transform root, List<(Mesh mesh, Mesh low, Vector3 origin)> chunks, Material mat, string dir)
    {
        foreach (var (mesh, low, origin) in chunks)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(root, false);
            go.transform.position = origin;
            var hi = new GameObject("LOD0", typeof(MeshFilter), typeof(MeshRenderer));
            hi.transform.SetParent(go.transform, false);
            hi.GetComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, dir, mesh.name);
            hi.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var lo = new GameObject("LOD1", typeof(MeshFilter), typeof(MeshRenderer));
            lo.transform.SetParent(go.transform, false);
            lo.GetComponent<MeshFilter>().sharedMesh = SaveMesh(low, dir, low.name);
            var loR = lo.GetComponent<MeshRenderer>();
            loR.sharedMaterial = mat;
            loR.shadowCastingMode = ShadowCastingMode.Off;
            var group = go.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.45f, new Renderer[] { hi.GetComponent<MeshRenderer>() }),
                new LOD(0.0005f, new Renderer[] { loR }),
            });
            group.RecalculateBounds();
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
    }

    // ---------- 지형 재질: 전체 색 지도 + 3m 단위 잔디 결 디테일 ----------
    internal static Material TerrainMaterial(CourseTerrainBuilder terrain, string prefix = "Course", int colorMapSize = 1024)
    {
        var colorMap = MakeTexture($"{prefix}_TerrainColor", colorMapSize, colorMapSize, terrain.ColorAt);
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(colorMap));
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();

        var mat = VegetationBuilder.StyMat($"{prefix}_Terrain", 0f, 1f, false);
        mat.SetTexture("_BaseMap", colorMap);
        mat.SetTextureScale("_BaseMap", Vector2.one);
        mat.SetTexture("_DetailMap", DetailNoise());
        mat.SetFloat("_DetailScale", 0.18f);
        mat.SetFloat("_DetailStrength", 0.6f);
        mat.SetFloat("_DetailScale2", 0.9f);    // 1m 단위 풀결
        mat.SetFloat("_DetailStrength2", 0.3f);
        mat.SetTexture("_DetailMap2", GrassStrokes());   // 가까이 보이는 붓결 풀(두 번째 잔결)
        mat.SetFloat("_StrataStrength", 0.45f); // 절벽·협곡 벽·둑의 가로 지층
        mat.SetFloat("_StrataScale", 0.18f);
        mat.SetFloat("_Wrap", 0.3f);
        mat.SetFloat("_RimStrength", 0f);
        NatureTextures.ApplyRock(mat, 0.08f, 0f, steep: 0.32f, normal: 0.8f);   // 가파른 절벽·협곡 벽에만 사진 바위 결
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---------- 출발선 · 아치 ----------
    internal static void BuildStart(Track track)
    {
        var root = new GameObject("StartArch").transform;
        var p = track.GetPoint(0f);
        root.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), Quaternion.LookRotation(track.GetForward(0f)));

        var white = MakeMat("Course_LineWhite", new Color(0.93f, 0.93f, 0.92f), smoothness: 0.2f);
        var dark = MakeMat("Course_ArchDark", new Color(0.13f, 0.13f, 0.14f), smoothness: 0.35f);
        var glow = MakeMat("Course_ArchGlow", new Color(1f, 0.45f, 0.12f), smoothness: 0.3f,
                           emission: new Color(1f, 0.42f, 0.1f) * 3.2f);

        Box(root, "StartLine", new Vector3(0f, CourseRoadBuilder.RoadY + 0.002f, 0f), new Vector3(CourseRoadBuilder.RoadWidth, 0.004f, 0.5f), white);
        float x = CourseRoadBuilder.ShoulderOuter + 0.6f;
        Box(root, "PillarL", new Vector3(-x, 3.2f, 0f), new Vector3(0.7f, 6.4f, 0.7f), dark);
        Box(root, "PillarR", new Vector3(x, 3.2f, 0f), new Vector3(0.7f, 6.4f, 0.7f), dark);
        Box(root, "Beam", new Vector3(0f, 6.6f, 0f), new Vector3(2f * x + 0.7f, 1.4f, 0.7f), dark);
        Box(root, "BeamGlowFront", new Vector3(0f, 6.0f, -0.36f), new Vector3(2f * x + 0.2f, 0.08f, 0.02f), glow);
        Box(root, "BeamGlowBack", new Vector3(0f, 6.0f, 0.36f), new Vector3(2f * x + 0.2f, 0.08f, 0.02f), glow);
        Box(root, "PillarGlowL", new Vector3(-x + 0.36f, 3.2f, 0f), new Vector3(0.02f, 5.6f, 0.08f), glow);
        Box(root, "PillarGlowR", new Vector3(x - 0.36f, 3.2f, 0f), new Vector3(0.02f, 5.6f, 0.08f), glow);
    }

    internal static void Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    // ---------- 하늘 · 빛 · 안개 ----------
    internal static Light SetupLighting()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Course_Sky.mat");
        if (!sky)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, Root + "/Materials/Course_Sky.mat");
        }
        sky.SetFloat("_SunDisk", 2f);
        sky.SetFloat("_SunSize", 0.045f);
        sky.SetFloat("_SunSizeConvergence", 4f);
        sky.SetFloat("_AtmosphereThickness", 1.35f);
        sky.SetColor("_SkyTint", new Color(0.56f, 0.55f, 0.62f));
        sky.SetColor("_GroundColor", new Color(0.42f, 0.40f, 0.38f));
        sky.SetFloat("_Exposure", 1.25f);
        EditorUtility.SetDirty(sky);

        var sun = Object.FindAnyObjectByType<Light>();
        sun.name = "Sun";
        // 출발 직선(+x) 정면 약간 왼쪽 위에 해가 보이도록: 빛은 -x 쪽으로 진행
        sun.transform.rotation = Quaternion.Euler(18f, -70f, 0f);
        sun.color = new Color(1f, 0.80f, 0.60f);
        sun.intensity = 1.6f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0009f;
        RenderSettings.fogColor = new Color(0.80f, 0.71f, 0.64f);
        return sun;
    }

    // 하늘에서 주변광·반사만 굽는다(라이트맵 없음)
    internal static void BakeEnvironment()
    {
        string path = Root + "/Settings/CourseLighting.lighting";
        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
        if (!ls)
        {
            ls = new LightingSettings();
            AssetDatabase.CreateAsset(ls, path);
        }
        ls.bakedGI = false;
        ls.realtimeGI = false;
        Lightmapping.lightingSettings = ls;
        Lightmapping.Bake();
    }

    // ---------- 후처리 ----------
    internal static Volume SetupPostProcessing()
    {
        string path = Root + "/Settings/CoursePost.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        T Get<T>() where T : VolumeComponent => profile.TryGet<T>(out var c) ? c : Add<T>(profile);

        // 손그림풍: 대비·채도 살짝 올리고 블룸은 은은하게(모바일에서도 1패스 수준)
        var tone = Get<Tonemapping>();
        tone.mode.Override(TonemappingMode.Neutral);
        var bloom = Get<Bloom>();
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.45f);
        bloom.scatter.Override(0.65f);
        bloom.highQualityFiltering.Override(false);
        var color = Get<ColorAdjustments>();
        color.postExposure.Override(0.15f);
        color.contrast.Override(12f);
        color.saturation.Override(4f);   // 형광빛 연두가 되지 않게(색은 재질 쪽에서 잡음)
        var wb = Get<WhiteBalance>();
        wb.temperature.Override(2f);
        var vig = Get<Vignette>();
        vig.intensity.Override(0.18f);
        vig.smoothness.Override(0.45f);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        var vol = new GameObject("PostProcess").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;
        return vol;
    }

    static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var c = profile.Add<T>(true);
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, profile);
        return c;
    }
}
