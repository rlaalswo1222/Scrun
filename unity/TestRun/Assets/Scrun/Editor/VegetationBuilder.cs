using System.Collections.Generic;
using System.Linq;
using Scrun;
using UnityEditor;
using UnityEngine;
using static ScrunEditorUtil;

// 손그림풍 식생·바위 배치 → InstancedScatter 한 개에 담는다.
// 메쉬는 StylizedFlora가 코드로 만든다(나무 LOD 4단계, 덤불 3단계, 잎은 FoliageAtlas 손그림 잎 카드). 지역마다 나무 종류·꽃 색·밀도가 다르다.
//  - 도로변: 풀밭, 덤불·꽃 덤불, 꽃대, 가로수
//  - 넓은 땅: 숲(숲 지역은 빽빽한 침엽수림), 그 밖은 노이즈로 모인 작은 숲 + 드문드문 홀로 선 나무, 바위 무리
public static class VegetationBuilder
{
    const string OutDir = Root + "/Course/Generated/Vegetation";

    class Species
    {
        public readonly List<int> Protos = new();
        public float MinScale = 1f, MaxScale = 1f;
        public float CellSize = 64f;
        public float Sink;
    }

    static readonly List<InstancedScatter.Proto> protos = new();
    static readonly Dictionary<string, Species> species = new();
    static readonly Dictionary<(int proto, int cx, int cz), List<(Vector3 p, float s, float yaw)>> buckets = new();

    // 땅에 구울 그늘(나무 수관·덤불·큰 바위 밑): 위치, 반지름(m), 세기(0~1). 지형 색 지도에 칠해 물체가 땅에 붙어 보이게
    public static readonly List<(Vector2 p, float r, float k)> GroundShade = new();
    static readonly Dictionary<string, (float r, float k)> shadeOf = new()
    {
        ["Broadleaf"] = (4.2f, 0.42f), ["Blossom"] = (4.4f, 0.4f), ["Autumn"] = (4.2f, 0.42f), ["Poplar"] = (2.4f, 0.35f),
        ["Conifer"] = (3.4f, 0.5f), ["Redwood"] = (6.5f, 0.55f), ["Palm"] = (3.2f, 0.28f),
        ["BushGreen"] = (1.4f, 0.3f), ["BushPink"] = (1.3f, 0.28f), ["BushPurple"] = (1.3f, 0.28f), ["BushYellow"] = (1.3f, 0.28f), ["BushWhite"] = (1.3f, 0.28f),
        ["Fern"] = (1.0f, 0.22f), ["RockBig"] = (4.5f, 0.35f), ["Crag"] = (6f, 0.4f),
    };

    // regionAt: 위치 → 지역 id(harbor/lake/forest/highland/cliff, 없으면 null)
    // groundMap: 지형 색 지도(있으면 풀이 그 자리 땅색으로 물듦)
    public static InstancedScatter Build(IList<IPath> paths, CourseTerrainBuilder terrain, System.Func<Vector3, string> regionAt,
                                         string dataPath, Texture groundMap = null)
    {
        protos.Clear(); species.Clear(); buckets.Clear(); GroundShade.Clear();
        EnsureFolderPath(OutDir);
        DefineProtos(terrain, groundMap);
        Scatter(paths, terrain, regionAt);

        var go = new GameObject("Vegetation");
        var scatter = go.AddComponent<InstancedScatter>();
        var cells = new List<InstancedScatter.Cell>();
        var values = new List<float>();
        foreach (var kv in buckets)
        {
            var list = kv.Value;
            var p = protos[kv.Key.proto];
            var b0 = p.lods[0].bounds;
            float radius = b0.extents.magnitude;
            for (int start = 0; start < list.Count; start += InstancedScatter.MaxPerCell)
            {
                int n = Mathf.Min(InstancedScatter.MaxPerCell, list.Count - start);
                var cell = new InstancedScatter.Cell { proto = kv.Key.proto, start = values.Count / InstancedScatter.FloatsPerInstance, count = n };
                var b = new Bounds(list[start].p, Vector3.zero);
                for (int i = 0; i < n; i++)
                {
                    var (pos, sc, yaw) = list[start + i];
                    values.Add(pos.x); values.Add(pos.y); values.Add(pos.z); values.Add(sc); values.Add(yaw);
                    b.Encapsulate(new Bounds(pos + b0.center * sc, Vector3.one * radius * 2f * sc));
                }
                cell.bounds = b;
                cells.Add(cell);
            }
        }
        var bytes = new byte[values.Count * 4];
        System.Buffer.BlockCopy(values.ToArray(), 0, bytes, 0, bytes.Length);
        System.IO.File.WriteAllBytes(dataPath, bytes);
        AssetDatabase.ImportAsset(dataPath, ImportAssetOptions.ForceUpdate);
        scatter.Set(new List<InstancedScatter.Proto>(protos), cells, AssetDatabase.LoadAssetAtPath<TextAsset>(dataPath));

        var counts = new Dictionary<string, int>();
        foreach (var kv in buckets)
        {
            string name = protos[kv.Key.proto].name.Split('_')[0];
            counts[name] = (counts.TryGetValue(name, out int c) ? c : 0) + kv.Value.Count;
        }
        var summary = string.Join(", ", counts.Keys.Select(k => $"{k} {counts[k]:N0}"));
        Debug.Log($"[Scrun] 식생 {scatter.InstanceCount:N0}개, 칸 {cells.Count}, 종류 {protos.Count}, 데이터 {bytes.Length / 1e6f:0.0}MB — {summary}");
        return scatter;
    }

    // 식생 종류(메쉬 LOD·재질). terrain이 null이면 풀의 땅색 물들이기 설정은 그대로 둠(메쉬만 다시 만들 때)
    static void DefineProtos(CourseTerrainBuilder terrain, Texture groundMap)
    {
        var atlas = FoliageAtlas.Build();
        var grassMat = StyMat("Sty_Grass", 1.0f, 2.5f, true);
        grassMat.SetFloat("_FadeShrink", 1f);   // 멀어지면 작아지며 사라짐(디더보다 풀밭 가장자리가 부드러움)
        if (terrain != null)
        {
            grassMat.SetTexture("_GroundMap", groundMap);
            grassMat.SetVector("_GroundRect", groundMap ? terrain.GroundRect : Vector4.zero);
            grassMat.SetFloat("_GroundTint", groundMap ? 0.8f : 0f);
        }
        var bushMat = FoliageMat("Sty_Bush", atlas, 0.5f, 1.5f);
        var treeMat = FoliageMat("Sty_Tree", atlas, 0.3f, 0.4f);
        treeMat.SetTexture("_BarkArray", NatureTextures.BarkArray());   // 줄기·가지는 사진 나무껍질
        treeMat.SetFloat("_UseBark", 1f);
        treeMat.EnableKeyword("_BARK_ARRAY");
        var rockMat = StyMat("Sty_Rock", 0f, 1f, false);
        rockMat.SetTexture("_DetailMap", CourseSceneBuilder.DetailNoise());
        rockMat.SetFloat("_StrataStrength", 0.15f);
        rockMat.SetFloat("_StrataScale", 0.6f);
        NatureTextures.ApplyRock(rockMat, 0.22f, 0.55f);   // 사진 화강암 결 + 윗면 이끼

        // 풀 3종(초록 큰·작은, 마른)
        AddSingle("GrassTall", StylizedFlora.GrassClump(8, 0.3f, 0.55f, new Color(0.14f, 0.30f, 0.06f), new Color(0.40f, 0.64f, 0.14f), 1), grassMat, 24f, 40f, true);
        AddSingle("GrassShort", StylizedFlora.GrassClump(8, 0.16f, 0.32f, new Color(0.16f, 0.33f, 0.07f), new Color(0.46f, 0.70f, 0.17f), 2), grassMat, 24f, 40f, true);
        AddSingle("GrassDry", StylizedFlora.GrassClump(8, 0.25f, 0.5f, new Color(0.30f, 0.34f, 0.10f), new Color(0.72f, 0.66f, 0.28f), 3), grassMat, 24f, 40f, true);

        // 덤불(초록) + 꽃 덤불(분홍·보라·노랑·흰) + 꽃대 + 고사리
        var leaf = new Color(0.25f, 0.46f, 0.16f);
        var bushLods = new[] { 40f, 80f, 170f };   // LOD 0(그림자 드리움)이 그림자 거리(40~50m)를 덮게
        var spikeLods = new[] { 20f, 50f, 170f };   // 멀리는 색 덩어리 하나(꽃밭이 언덕 위에서 보이게)
        AddLod("BushGreen", s => StylizedFlora.Bush(leaf, null, s.lod, 10 + s.v), 2, bushMat, 48f, bushLods, true, 0.8f, 1.6f);
        AddLod("BushPink", s => StylizedFlora.Bush(leaf, new Color(0.95f, 0.45f, 0.70f).linear, s.lod, 20 + s.v), 1, bushMat, 48f, bushLods, true, 0.8f, 1.4f);
        AddLod("BushPurple", s => StylizedFlora.Bush(leaf, new Color(0.62f, 0.38f, 0.92f).linear, s.lod, 30 + s.v), 1, bushMat, 48f, bushLods, true, 0.8f, 1.4f);
        AddLod("BushYellow", s => StylizedFlora.Bush(leaf, new Color(1.0f, 0.78f, 0.18f).linear, s.lod, 40 + s.v), 1, bushMat, 48f, bushLods, true, 0.8f, 1.4f);
        AddLod("BushWhite", s => StylizedFlora.Bush(leaf, new Color(0.97f, 0.96f, 0.92f), s.lod, 50 + s.v), 1, bushMat, 48f, bushLods, true, 0.8f, 1.4f);
        AddLod("SpikePurple", s => StylizedFlora.FlowerSpikes(new Color(0.58f, 0.36f, 0.88f).linear, s.lod, 60 + s.v), 1, bushMat, 48f, spikeLods, false, 0.9f, 1.4f);
        AddLod("SpikePink", s => StylizedFlora.FlowerSpikes(new Color(0.95f, 0.42f, 0.66f).linear, s.lod, 70 + s.v), 1, bushMat, 48f, spikeLods, false, 0.9f, 1.4f);
        AddLod("SpikeYellow", s => StylizedFlora.FlowerSpikes(new Color(1.0f, 0.78f, 0.16f).linear, s.lod, 80 + s.v), 1, bushMat, 48f, spikeLods, false, 0.9f, 1.4f);
        AddLod("Fern", s => StylizedFlora.Fern(s.lod, 85 + s.v), 2, bushMat, 32f, new[] { 22f, 45f, 80f }, false, 0.8f, 1.5f);

        // 나무 7종(LOD 4단계: 잎 카드 많이 → 적게 → 덩어리 몇 개 → 덩어리 하나)
        var treeLods = new[] { 55f, 120f, 240f, 1000f };   // 240m부터는 십자 잎 카드(삼각형 8개) — 높은 곳에서 멀리 내다봐도 가볍게   // LOD 0이 그림자 거리보다 멀리까지
        var tree = new System.Func<StylizedFlora.TreeKind, System.Func<(int lod, int v), Mesh>>(k => s => StylizedFlora.Tree(k, s.lod, s.v));
        AddLod("Broadleaf", tree(StylizedFlora.TreeKind.Broadleaf), 2, treeMat, 96f, treeLods, true, 0.85f, 1.25f);
        AddLod("Blossom", tree(StylizedFlora.TreeKind.Blossom), 2, treeMat, 96f, treeLods, true, 0.8f, 1.15f);
        AddLod("Autumn", tree(StylizedFlora.TreeKind.Autumn), 2, treeMat, 96f, treeLods, true, 0.85f, 1.2f);
        AddLod("Conifer", tree(StylizedFlora.TreeKind.Conifer), 2, treeMat, 96f, treeLods, true, 0.8f, 1.35f);
        AddLod("Poplar", tree(StylizedFlora.TreeKind.Poplar), 2, treeMat, 96f, treeLods, true, 0.85f, 1.2f);
        AddLod("Redwood", tree(StylizedFlora.TreeKind.Redwood), 2, treeMat, 96f, new[] { 70f, 170f, 420f, 1500f }, true, 0.85f, 1.2f);
        AddLod("Palm", tree(StylizedFlora.TreeKind.Palm), 2, treeMat, 96f, treeLods, true, 0.85f, 1.15f);

        // 바위(작은 것·큰 것)
        var stone = new Color(0.30f, 0.27f, 0.23f);   // 정점 색(선형) → 화면에선 중간 밝기의 따뜻한 회색
        AddLod("RockSmall", s => StylizedFlora.Rock(s.lod, 90 + s.v, new Vector3(1.1f, 0.7f, 0.9f), stone), 2, rockMat, 96f, new[] { 35f, 120f, 280f }, false, 0.6f, 1.6f);
        AddLod("RockBig", s => StylizedFlora.Rock(s.lod, 100 + s.v, new Vector3(4f, 3f, 3.4f), stone), 2, rockMat, 96f, new[] { 60f, 300f, 1000f }, true, 0.7f, 1.8f);
        // 바위 봉우리(능선·언덕 꼭대기의 우뚝 선 바위 — 하늘선에 굴곡)
        AddLod("Crag", s => StylizedFlora.Rock(s.lod, 120 + s.v, new Vector3(6f, 7.5f, 5f), stone), 2, rockMat, 128f, new[] { 90f, 450f, 1500f }, true, 0.8f, 1.9f);
        species["Crag"].Sink = 0.12f;
        species["RockSmall"].Sink = 0.15f;
        species["RockBig"].Sink = 0.25f;
    }

    // 메뉴 Scrun → Rebuild Vegetation Meshes (fast): 배치는 그대로 두고 나무·덤불·바위 메쉬와 재질만 다시 만든다(수십 초).
    // 씬의 InstancedScatter가 같은 이름의 메쉬 에셋을 가리키므로 덮어쓰면 바로 바뀜. LOD 수·변형 수를 바꿨으면 Build Island Scene 필요
    [MenuItem("Scrun/Rebuild Vegetation Meshes (fast)")]
    public static void RebuildMeshes()
    {
        protos.Clear(); species.Clear();
        EnsureFolderPath(OutDir);
        DefineProtos(null, null);
        foreach (var path in new[] { $"{Root}/Materials/Sty_RockWall.mat", $"{Root}/Materials/Lm_SeaRockSty.mat" })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m && m.IsKeywordEnabled("_ROCK_TRIPLANAR")) NatureTextures.ApplyRock(m, m.GetFloat("_RockScale"), m.GetFloat("_MossAmount"));
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Scrun] 식생 메쉬 다시 만듦: 종류 {protos.Count}");
    }

    // ================= 배치 규칙 =================
    static void Scatter(IList<IPath> paths, CourseTerrainBuilder terrain, System.Func<Vector3, string> regionAt)
    {
        var rng = new System.Random(20261026);
        float R() => (float)rng.NextDouble();

        bool Ground(Vector3 p, float minRoad, float maxSlope = 0.55f)
        {
            if (terrain.RoadDistance(p.x, p.z) < minRoad) return false;
            if (terrain.LakeMask(p.x, p.z) > 0.12f) return false;
            if (terrain.Island && terrain.CoastDistance(p.x, p.z) < 6f) return false;   // 절벽 끝
            if (p.y < -0.12f) return false;
            if (terrain.Excluded(p.x, p.z)) return false;
            if (1f - terrain.NormalAt(p.x, p.z).y > maxSlope) return false;
            return true;
        }
        Vector3 At(float x, float z) => new(x, terrain.Height(x, z), z);
        float N(Vector3 p, float f, float seed) => Mathf.PerlinNoise(p.x * f + seed, p.z * f - seed);

        string[] Flowers(string region) => region switch
        {
            "harbor" => new[] { "BushPink", "BushWhite", "SpikePink" },
            "lake" => new[] { "BushPurple", "BushYellow", "SpikePurple" },
            "highland" => new[] { "BushYellow", "BushPurple", "SpikeYellow" },
            "forest" => new[] { "BushWhite", "BushGreen", "SpikePurple" },
            "cliff" => new[] { "BushWhite", "BushYellow", "SpikeYellow" },
            _ => new[] { "BushPink", "BushPurple", "SpikePurple" },
        };
        string TreeFor(string region, Vector3 p)
        {
            float t = N(p, 0.004f, 71f), u = R();
            switch (region)
            {
                case "forest": return u < 0.38f ? "Redwood" : u < 0.88f ? "Conifer" : "Broadleaf";
                case "harbor": return u < 0.35f ? "Palm" : u < 0.6f ? "Blossom" : "Broadleaf";
                case "lake": return u < 0.2f ? "Poplar" : u < 0.35f ? "Blossom" : "Broadleaf";
                case "highland": return t > 0.55f ? (u < 0.7f ? "Autumn" : "Broadleaf") : (u < 0.25f ? "Autumn" : "Broadleaf");
                case "cliff": return u < 0.5f ? "Conifer" : "Broadleaf";
                default: return u < 0.25f ? "Blossom" : u < 0.4f ? "Conifer" : "Broadleaf";
            }
        }

        // ---------- 도로변 ----------
        foreach (var path in paths)
        {
            float lap = path.Length;
            Vector3 Side(float s, float side, float off)
            {
                var c = path.GetPoint(s);
                var p = new Vector3(c.x, 0f, c.z) + path.GetRight(s) * (side * off);
                p.y = terrain.Height(p.x, p.z);
                return p;
            }

            // 풀밭: 가장자리부터 끝 경계가 노이즈로 들쭉날쭉한 띠
            for (float s = 0; s < lap; s += 0.75f)
                for (int side = -1; side <= 1; side += 2)
                    for (float off = 6.4f; off < 32f; off += 0.75f)
                    {
                        float o = off + (R() - 0.5f) * 0.7f;
                        var p = Side(s + (R() - 0.5f) * 0.7f, side, o);
                        float edge = 16f + 15f * N(p, 0.05f, 19f);
                        float keep = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 6f, edge, o))) * 0.92f;
                        if (R() > keep || !Ground(p, 6.3f)) continue;
                        string name = N(p, 0.03f, 41f) > 0.7f ? "GrassDry" : (N(p, 0.15f, 7f) > 0.45f ? "GrassTall" : "GrassShort");
                        Place(name, p, 0.85f + 0.35f * R(), R() * 360f);
                    }

            // 덤불·꽃 덤불 무리
            for (float s = 0; s < lap; s += 2f)
                for (int side = -1; side <= 1; side += 2)
                {
                    var p = Side(s, side, 7f + 22f * R() * R());
                    if (N(p, 0.05f, 11f) < 0.36f || !Ground(p, 6.8f)) continue;   // 길가 덤불 띠가 덜 끊기게
                    var set = Flowers(regionAt(p));
                    string pick = N(p, 0.02f, 33f) > 0.5f ? set[(int)(R() * 1.99f)] : "BushGreen";
                    int count = 2 + (int)(R() * 3f);
                    for (int i = 0; i < count; i++)
                    {
                        var q = p + new Vector3((R() - 0.5f) * 3f, 0f, (R() - 0.5f) * 3f);
                        q.y = terrain.Height(q.x, q.z);
                        if (Ground(q, 6.8f)) PlaceRandom(pick, q, R);
                    }
                }

            // 꽃대 무리(도로 가까이)
            for (float s = 0; s < lap; s += 3f)
                for (int side = -1; side <= 1; side += 2)
                {
                    var p = Side(s, side, 6.8f + 10f * R());
                    if (N(p, 0.045f, 23f) < 0.56f || !Ground(p, 6.6f)) continue;
                    string pick = Flowers(regionAt(p))[2];
                    int count = 3 + (int)(R() * 5f);
                    for (int i = 0; i < count; i++)
                    {
                        var q = p + new Vector3((R() - 0.5f) * 4f, 0f, (R() - 0.5f) * 4f);
                        q.y = terrain.Height(q.x, q.z);
                        if (Ground(q, 6.6f)) PlaceRandom(pick, q, R);
                    }
                }

            // 고사리 덤불(숲 지역 도로변 바닥)
            for (float s = 0; s < lap; s += 1.6f)
                for (int side = -1; side <= 1; side += 2)
                {
                    var p = Side(s + (R() - 0.5f) * 1.5f, side, 6.8f + 20f * R());
                    if (regionAt(p) != "forest" || N(p, 0.06f, 29f) < 0.38f || !Ground(p, 6.6f)) continue;
                    PlaceRandom("Fern", p, R);
                }

            // 가로수: 지역별 확률, 노이즈로 끊어져 줄지어 섬. 첫 줄은 길 가까이(9~13m) 촘촘히, 둘째 줄(15~26m)은 듬성듬성
            // → 큰 나무가 길을 감싸 화면 양옆이 비지 않게(Zwift식)
            for (int row = 0; row < 2; row++)
                for (float s = R() * 9f; s < lap; s += row == 0 ? 9f : 12f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float off = row == 0 ? 9f + 4f * R() : 15f + 11f * R();
                        var p = Side(s + (R() - 0.5f) * 4f, side, off);
                        var region = regionAt(p);
                        float chance = region switch { "forest" => 0.9f, "lake" => 0.5f, "harbor" => 0.45f, "highland" => 0.25f, "cliff" => 0.1f, _ => 0.35f };
                        if (row == 1) chance *= 0.6f;
                        if (N(p, 0.012f, 5f) < 0.42f) chance *= 0.3f;
                        if (R() > chance || !Ground(p, 9f, 0.4f)) continue;
                        if (terrain.Island && terrain.CoastDistance(p.x, p.z) < 35f) continue;
                        PlaceRandom(TreeFor(region, p), p, R);
                    }
        }

        // ---------- 넓은 땅: 숲·작은 숲·홀로 선 나무 ----------
        float half = terrain.Size / 2f;
        var c0 = terrain.Center;
        // 언덕까지 숲으로 덮이게(Zwift식): 숲 덩어리(grove)가 넓고 빽빽, 그 사이 초지에도 띄엄띄엄. 먼 나무는 십자 카드라 가벼움
        const float step = 8f;
        int trees = 0, shrubs = 0;
        for (float x = c0.x - half; x < c0.x + half; x += step)
            for (float z = c0.y - half; z < c0.y + half; z += step)
            {
                var p = At(x + (R() - 0.5f) * step * 0.9f, z + (R() - 0.5f) * step * 0.9f);
                if (p.y < -0.1f) continue;
                if (terrain.RoadDistance(p.x, p.z) < 15f) continue;
                if (terrain.Island && terrain.CoastDistance(p.x, p.z) < 30f) continue;
                var region = regionAt(p);
                float grove = N(p, 0.006f, 13f) * 0.7f + N(p, 0.02f, 3f) * 0.3f;
                float density = region switch
                {
                    "forest" => 0.6f + 0.3f * grove,
                    "cliff" => grove > 0.52f ? 0.3f : 0.02f,
                    "highland" => grove > 0.5f ? 0.45f : 0.03f,
                    _ => grove > 0.47f ? 0.5f : 0.035f,
                };
                // 숲 가장자리·초지의 덤불(중경이 비지 않게)
                if (region != "forest" && grove > 0.36f && grove < 0.5f && R() < 0.06f && Ground(p, 15f, 0.45f))
                {
                    var set = Flowers(region);
                    PlaceRandom(R() < 0.6f ? "BushGreen" : set[(int)(R() * 1.99f)], p, R);
                    shrubs++;
                }
                if (R() > density || !Ground(p, 15f, 0.5f)) continue;
                PlaceRandom(TreeFor(region, p), p, R);
                trees++;
                if (region == "forest" && R() < 0.35f)
                {
                    var q = p + new Vector3((R() - 0.5f) * 6f, 0f, (R() - 0.5f) * 6f);
                    q.y = terrain.Height(q.x, q.z);
                    if (Ground(q, 15f)) PlaceRandom(R() < 0.6f ? "Fern" : "BushGreen", q, R);
                }
            }

        // ---------- 꽃밭: 고원 언덕의 보라·노랑 꽃 무리, 호수 초원의 보라 꽃(길에서 보이는 15~160m) ----------
        int fields = 0;
        for (float x = c0.x - half; x < c0.x + half; x += 5f)
            for (float z = c0.y - half; z < c0.y + half; z += 5f)
            {
                var p = At(x + (R() - 0.5f) * 4.5f, z + (R() - 0.5f) * 4.5f);
                float rd = terrain.RoadDistance(p.x, p.z);
                if (rd < 15f || rd > 160f || p.y < 0f) continue;
                var region = regionAt(p);
                if (region != "highland" && region != "lake") continue;
                float f = N(p, 0.009f, 91f);
                if (f < (region == "highland" ? 0.6f : 0.68f) || !Ground(p, 15f, 0.3f)) continue;
                string pick = region == "lake" ? "SpikePurple" : (N(p, 0.004f, 13f) > 0.5f ? "SpikePurple" : "SpikeYellow");
                int n = 1 + (int)(R() * 2.5f);
                for (int i = 0; i < n; i++)
                {
                    var q = p + new Vector3((R() - 0.5f) * 3f, 0f, (R() - 0.5f) * 3f);
                    q.y = terrain.Height(q.x, q.z);
                    if (Ground(q, 15f, 0.3f)) { PlaceRandom(pick, q, R); fields++; }
                }
            }
        Debug.Log($"[Scrun] 꽃밭 {fields:N0}포기");

        // ---------- 바위 무리: 능선·경사 ----------
        for (float x = c0.x - half; x < c0.x + half; x += 28f)
            for (float z = c0.y - half; z < c0.y + half; z += 28f)
            {
                var p = At(x + (R() - 0.5f) * 25f, z + (R() - 0.5f) * 25f);
                if (p.y < -0.1f || terrain.RoadDistance(p.x, p.z) < 12f) continue;
                if (terrain.LakeMask(p.x, p.z) > 0.05f || terrain.Excluded(p.x, p.z)) continue;
                float slope = 1f - terrain.NormalAt(p.x, p.z).y;
                float ridge = N(p, 0.009f, 57f);
                var region = regionAt(p);
                float chance = (ridge > 0.62f ? 0.5f : 0.03f) + slope * 0.8f + (region == "cliff" ? 0.15f : 0f);
                if (R() > chance) continue;
                bool big = R() < 0.35f + slope;
                int n = 1 + (int)(R() * 3f);
                for (int i = 0; i < n; i++)
                {
                    var q = p + new Vector3((R() - 0.5f) * 10f, 0f, (R() - 0.5f) * 10f);
                    q.y = terrain.Height(q.x, q.z);
                    if (q.y < 0f || terrain.RoadDistance(q.x, q.z) < (big ? 14f : 9f)) continue;
                    PlaceRandom(big && i == 0 ? "RockBig" : "RockSmall", q, R);
                }
            }

        // 바위 봉우리: 주변(40m)보다 높은 능선·언덕 꼭대기, 길에서 50~700m
        int crags = 0;
        for (float x = c0.x - half; x < c0.x + half; x += 36f)
            for (float z = c0.y - half; z < c0.y + half; z += 36f)
            {
                var p = At(x + (R() - 0.5f) * 30f, z + (R() - 0.5f) * 30f);
                float rd = terrain.RoadDistance(p.x, p.z);
                if (rd < 50f || rd > 700f || p.y < terrain.RoadHeight(p.x, p.z) + 6f || terrain.Excluded(p.x, p.z)) continue;
                if (terrain.Island && terrain.CoastDistance(p.x, p.z) < 25f) continue;
                float around = 0f;
                for (int k = 0; k < 6; k++)
                {
                    float a = k / 6f * Mathf.PI * 2f;
                    around += terrain.Height(p.x + Mathf.Cos(a) * 40f, p.z + Mathf.Sin(a) * 40f);
                }
                float rise = p.y - around / 6f;
                var region = regionAt(p);
                float chance = Mathf.InverseLerp(1.5f, 8f, rise) * (region == "cliff" ? 0.8f : region == "highland" ? 0.6f : region == "forest" ? 0.45f : 0.3f);
                if (R() > chance) continue;
                int n = 1 + (int)(R() * 3f);
                for (int i = 0; i < n; i++)
                {
                    var q = p + new Vector3((R() - 0.5f) * 14f, 0f, (R() - 0.5f) * 14f);
                    q.y = terrain.Height(q.x, q.z);
                    PlaceRandom(i == 0 ? "Crag" : "RockBig", q, R);
                    crags++;
                }
            }
        Debug.Log($"[Scrun] 바위 봉우리 {crags:N0}");

        // 호숫가 바위
        if (terrain.LakeCircles.Count > 0)
        {
            var lake = terrain.LakeBounds();
            int tries = Mathf.RoundToInt(1500f * lake.width * lake.height / (500f * 500f));
            for (int i = 0; i < tries; i++)
            {
                var p = At(Mathf.Lerp(lake.xMin, lake.xMax, R()), Mathf.Lerp(lake.yMin, lake.yMax, R()));
                float m = terrain.LakeMask(p.x, p.z);
                if (m < 0.05f || m > 0.5f || terrain.RoadDistance(p.x, p.z) < 9f) continue;
                PlaceRandom("RockSmall", p, R);
            }
        }
        Debug.Log($"[Scrun] 넓은 땅 나무 {trees:N0}그루, 초지 덤불 {shrubs:N0}");
    }

    // ================= 배치 도구 =================
    static void PlaceRandom(string name, Vector3 p, System.Func<float> R)
    {
        var sp = species[name];
        Place(name, p, Mathf.Lerp(sp.MinScale, sp.MaxScale, R()), R() * 360f, (int)(R() * 1000f));
    }

    static void Place(string name, Vector3 p, float scale, float yaw, int variantSeed = 0)
    {
        var sp = species[name];
        int proto = sp.Protos[variantSeed % sp.Protos.Count];
        p.y -= protos[proto].lods[0].bounds.size.y * scale * sp.Sink;
        float cell = sp.CellSize;
        var key = (proto, Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.z / cell));
        if (shadeOf.TryGetValue(name, out var sh)) GroundShade.Add((new Vector2(p.x, p.z), sh.r * scale, sh.k));
        if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<(Vector3, float, float)>();
        list.Add((p, scale, yaw));
    }

    static void AddSingle(string name, Mesh mesh, Material mat, float cell, float maxDist, bool thinnable)
    {
        var saved = SaveMesh(mesh, OutDir, $"Sty_{name}");
        protos.Add(new InstancedScatter.Proto
        {
            name = name, lods = new[] { saved }, lodDistances = new[] { maxDist }, material = mat, thinnable = thinnable,
        });
        var sp = new Species { CellSize = cell };
        sp.Protos.Add(protos.Count - 1);
        species[name] = sp;
    }

    static void AddLod(string name, System.Func<(int lod, int v), Mesh> make, int variants, Material mat, float cell,
                       float[] lodDist, bool shadows, float minS, float maxS)
    {
        var sp = new Species { CellSize = cell, MinScale = minS, MaxScale = maxS };
        for (int v = 0; v < variants; v++)
        {
            var lods = new Mesh[lodDist.Length];
            for (int l = 0; l < lods.Length; l++) lods[l] = SaveMesh(make((l, v)), OutDir, $"Sty_{name}_{v}_L{l}");
            protos.Add(new InstancedScatter.Proto
            {
                name = $"{name}_{v}", lods = lods, lodDistances = lodDist, material = mat, castShadows = shadows,
            });
            sp.Protos.Add(protos.Count - 1);
        }
        species[name] = sp;
    }

    // 잎 재질: 잎 아틀라스 + 알파→커버리지 + 양면(뒷면 법선 안 뒤집음) + 역광 + 잎 떨림
    public static Material FoliageMat(string name, Texture2D atlas, float wind, float windScale)
    {
        var mat = StyMat(name, wind, windScale, true);
        mat.SetTexture("_BaseMap", atlas);
        mat.SetFloat("_AlphaTest", 1f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cutoff", 0.45f);
        mat.SetFloat("_AlphaToMask", 1f);
        mat.SetFloat("_BackfaceFlip", 0f);
        mat.SetFloat("_Wrap", 0.55f);
        mat.SetFloat("_Translucency", 0.35f);
        mat.SetFloat("_Flutter", 0.6f);
        mat.SetFloat("_RimStrength", 0.2f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Scrun/Stylized 재질(정점 색 사용, 인스턴싱)
    public static Material StyMat(string name, float wind, float windScale, bool twoSided, Color? color = null)
    {
        EnsureFolder(Root, "Materials");
        string path = $"{Root}/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Scrun/Stylized");
        if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        mat.SetColor("_BaseColor", color ?? Color.white);
        mat.SetFloat("_Wind", wind);
        mat.SetFloat("_WindScale", windScale);
        mat.SetFloat("_Cull", twoSided ? 0f : 2f);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
