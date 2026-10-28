using System.Collections.Generic;
using System.IO;
using System.Linq;
using Scrun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static ScrunEditorUtil;

// 메뉴 Scrun → Build Island Scene : IslandLayout.json(섬 설계)에서 섬 월드 씬을 처음부터 만든다(덮어씀)
// 1단계: 도로망(구간+교차로) + 코스 + 기존 지형/식생 생성기. 해안 절벽·바다는 2단계.
public static class IslandSceneBuilder
{
    const string LayoutPath = Root + "/Course/IslandLayout.json";
    const string ScenePath = Root + "/Scenes/Island01.unity";
    const string GenDir = CourseSceneBuilder.GenDir + "/Island";
    const string DefaultRoute = "lake_loop";
    const float JunctionRadius = 9f;   // 교차로 원판 반지름(도로 반폭 4m + 연석·갓길 여유)

    [MenuItem("Scrun/Build Island Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureFolderPath(GenDir + "/Terrain");
        EnsureFolder(Root, "Scenes");

        var layout = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(LayoutPath));
        float k = layout.TryGetValue("scale", out var sc) ? (float)(double)sc : 1f;

        var runnerController = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerRigBuilder.CharacterModelPath)
            ? RunnerAnimatorBuilder.Build() : null;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ---- 도로망 ----
        var net = new GameObject("RoadNetwork").AddComponent<RoadNetwork>();
        foreach (var kv in (Dictionary<string, object>)layout["nodes"])
            net.nodes.Add(new RoadNetwork.Node { id = kv.Key, position = V(kv.Value, k) });
        foreach (Dictionary<string, object> e in (List<object>)layout["edges"])
        {
            var edge = new RoadNetwork.Edge { id = (string)e["id"], from = (string)e["from"], to = (string)e["to"], region = (string)e["region"] };
            edge.controlPoints.Add(net.FindNode(edge.from).position);
            foreach (var v in (List<object>)e["via"]) edge.controlPoints.Add(V(v, k));
            edge.controlPoints.Add(net.FindNode(edge.to).position);
            net.edges.Add(edge);
        }
        // 노면 높이표(화면 전용 고저차): 노드 높이 + 경유점 높이를 부드럽게 잇고, 경사 7% 이하, 교차로 앞뒤 30m는 평평하게
        var edgeJson = ((List<object>)layout["edges"]).Cast<Dictionary<string, object>>().ToDictionary(e => (string)e["id"]);
        foreach (var edge in net.edges)
            edge.heights = HeightProfile(edge, edgeJson[edge.id].TryGetValue("elev", out var ev) ? ((List<object>)ev).Select(F).ToList() : null);
        net.ClearCache();
        var features = SetPieceBuilder.Parse(layout, net, k);

        foreach (Dictionary<string, object> r in (List<object>)layout["routes"])
            net.routes.Add(new RoadNetwork.Route { id = (string)r["id"], name = (string)r["name"], path = ((List<object>)r["path"]).Cast<string>().ToList() });

        var regions = ((List<object>)layout["regions"]).Cast<Dictionary<string, object>>().ToDictionary(
            g => (string)g["id"], g => (center: V(g["center"], k), radius: (float)(double)g["radius"] * k));

        foreach (var g in regions)
            net.regions.Add(new RoadNetwork.Region
            {
                id = g.Key, center = g.Value.center, radius = g.Value.radius,
                name = (string)((List<object>)layout["regions"]).Cast<Dictionary<string, object>>().First(r => (string)r["id"] == g.Key)["name"],
            });

        string RegionAt(Vector3 p)
        {
            string best = null; float bestK = float.MaxValue;
            foreach (var g in regions)
            {
                float kk = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(g.Value.center.x, g.Value.center.z)) / g.Value.radius;
                if (kk < bestK) { bestK = kk; best = g.Key; }
            }
            return best;
        }

        // ---- 코스 ----
        var route = new GameObject("Route").AddComponent<RouteTrack>();
        route.Init(net, DefaultRoute);

        // ---- 도로 구간 선(메쉬는 지형 색이 정해진 뒤에 만듦: 갓길을 지형 색으로 칠하므로) ----
        var roadRoot = new GameObject("Roads").transform;
        var edgeLines = new List<IPath>();
        foreach (var e in net.edges) edgeLines.Add(net.EdgeLine(e.id));

        // ---- 지형 ----
        var b = new Bounds(edgeLines[0].GetPoint(0f), Vector3.zero);
        foreach (var l in edgeLines) for (float d = 0; d <= l.Length; d += 20f) b.Encapsulate(l.GetPoint(d));
        float roadExtent = Mathf.Max(b.extents.x, b.extents.z);
        var terrain = new CourseTerrainBuilder
        {
            Center = new Vector2(b.center.x, b.center.z),
            Size = Mathf.Ceil((roadExtent * 2f + 1600f) / 100f) * 100f,
            Cell = 12f,
            ChunkCells = 40,
            MountainStart = 1e6f, MountainFull = 2e6f,   // 섬: 산맥 대신 바다
            FlatRadius = 16f, HillFull = 120f,           // 도로 가까이부터 언덕이 솟아 길을 감싸게
        };
        // 해안·협곡 (가로 길이는 scale 적용, 높이는 그대로)
        var coast = (Dictionary<string, object>)layout["coast"];
        terrain.Island = true;
        terrain.IslandCenter = terrain.Center;
        terrain.CoastRadius = F(coast["radius"]) * k;
        terrain.RoadLandMargin = F(coast["roadMargin"]) * k;
        terrain.CliffWidth = F(coast["cliffWidth"]) * k;
        terrain.SeaFloor = F(coast["seaFloor"]);
        float seaLevel = F(coast["seaLevel"]);
        foreach (Dictionary<string, object> c in (List<object>)layout["canyons"])
            terrain.AddCanyon(((List<object>)c["points"]).Select(v => V(v, k)).ToList(), F(c["width"]) * k, F(c["depth"]));
        var lakeEdge = net.EdgeLine("lake_west");
        terrain.AddLakeAlong(lakeEdge, 150f, lakeEdge.Length - 150f, 16f, 60f, 230f, side: +1f);   // 북쪽으로 달릴 때 오른쪽(섬 안쪽)
        terrain.BuildDistanceField(edgeLines);
        // 도로 옆 둑: 지역별 높이(숲 협곡 길은 높게, 호숫가·항구는 낮게) × 구간 노이즈(300~500m씩 둑이 있다 없다)
        var bankHeight = new Dictionary<string, float> { ["harbor"] = 4f, ["lake"] = 5f, ["forest"] = 16f, ["highland"] = 9f, ["cliff"] = 12f };
        terrain.BankAmount = (x, z) =>
        {
            float wsum = 0f, amp = 0f;
            foreach (var g in regions)
            {
                float kk = Vector2.Distance(new Vector2(x, z), new Vector2(g.Value.center.x, g.Value.center.z)) / g.Value.radius;
                float w = 1f / (Mathf.Pow(kk, 6f) + 0.02f);
                wsum += w; amp += w * bankHeight[g.Key];
            }
            amp /= wsum;
            float n = Mathf.PerlinNoise(x * 0.0042f + 17f, z * 0.0042f - 5f) * 0.7f + Mathf.PerlinNoise(x * 0.013f, z * 0.013f) * 0.3f;
            return amp * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.62f, n));
        };
        // 연출 장면이 지형에 주는 것(협곡 길 둑, 터널 둔덕, 식생 제외)
        SetPieceBuilder.PrepareTerrain(features, net, terrain);
        // 해안선: coastMargin이 있는 길(해안 절벽 길)은 바다 쪽 육지를 그만큼만 남김
        var edgeData = ((List<object>)layout["edges"]).Cast<Dictionary<string, object>>().ToList();
        var cliffMargins = edgeData.Where(e => e.ContainsKey("coastMargin")).Select(e => F(e["coastMargin"]) * k).ToList();
        if (cliffMargins.Count > 0)
        {
            terrain.CliffRoadMargin = cliffMargins.Min();
            var inland = edgeData.Where(e => !e.ContainsKey("coastMargin")).Select(e => (IPath)net.EdgeLine((string)e["id"])).ToList();
            // 해안 절벽 길은 섬 안쪽으로 옮긴 선을 육지 보장에 넣음 → 바다 쪽만 좁고 안쪽은 섬 본체와 이어짐
            foreach (var e in edgeData.Where(e => e.ContainsKey("coastMargin")))
            {
                var line = net.EdgeLine((string)e["id"]);
                float shift = terrain.RoadLandMargin - F(e["coastMargin"]) * k;
                var pts = new List<Vector3>();
                for (float d = 0; d <= line.Length; d += 5f)
                {
                    var p = line.GetPoint(d);
                    var right = line.GetRight(d);
                    var toCenter = new Vector3(terrain.Center.x - p.x, 0f, terrain.Center.y - p.z);
                    pts.Add(p + right * (Vector3.Dot(right, toCenter) > 0f ? shift : -shift));
                }
                inland.Add(new Polyline(pts, false));
            }
            terrain.BuildLandField(inland);
        }

        // 평지 구역: 400m 트랙, 출발 광장
        var trackCenter = V(((Dictionary<string, object>)layout["track"])["center"], k);
        terrain.FlatZones.Add((new Vector2(trackCenter.x, trackCenter.z), 105f, 60f));
        var hNode = net.FindNode("H").position;
        terrain.FlatZones.Add((new Vector2(hNode.x, hNode.z), 30f, 30f));
        // 섬 안쪽 봉우리 3개(하늘선)
        terrain.AutoPeaks(3);
        Debug.Log("[Scrun] 봉우리: " + string.Join(", ", terrain.Peaks.Select(pk => $"({pk.c.x:0},{pk.c.y:0}) r{pk.radius:0} h{pk.height:0}")));

        // 지역별 땅 색
        foreach (var g in regions)
            terrain.Regions.Add((new Vector2(g.Value.center.x, g.Value.center.z), g.Value.radius, CourseTerrainBuilder.PaletteFor(g.Key)));

        var terrainMat = CourseSceneBuilder.TerrainMaterial(terrain, "Island", 2048);

        // ---- 도로 메쉬 · 교차로 ----
        // 연석은 출발 광장(H) 가까이만(마을 길). 그 밖은 아스팔트 → 흙 → 풀로 자연스럽게
        var roadMats = CourseSceneBuilder.RoadMaterials();
        var deckMats = CourseSceneBuilder.BoardwalkMaterials(roadMats);
        System.Func<Vector3, Color> ground = p => terrain.GroundColorLinear(p.x, p.z);
        const float CurbReach = 220f;
        foreach (var e in net.edges)
        {
            var line = net.EdgeLine(e.id);
            float trim = JunctionRadius - 2f;   // 원판 아래로 2m 겹치게
            float ra = trim, rb = line.Length - trim;
            var pieces = new List<(float from, float to, bool curb)>();
            bool curbStart = e.from == "H", curbEnd = e.to == "H";
            float ca = curbStart ? Mathf.Min(rb, CurbReach) : ra, cb = curbEnd ? Mathf.Max(ra, line.Length - CurbReach) : rb;
            if (curbStart) pieces.Add((ra, ca, true));
            pieces.Add((ca, cb, false));
            if (curbEnd) pieces.Add((cb, rb, true));
            // 나무 데크 구간은 따로 잘라 판자 노면으로
            var decks = SetPieceBuilder.Boardwalks(features, e.id).ToList();
            var split = new List<(float from, float to, bool curb, bool deck)>();
            foreach (var (from, to, curb) in pieces)
            {
                float cur = from;
                foreach (var (d0, d1) in decks.OrderBy(d => d.s0))
                {
                    if (d1 <= cur || d0 >= to) continue;
                    if (d0 > cur) split.Add((cur, d0, curb, false));
                    split.Add((Mathf.Max(cur, d0), Mathf.Min(to, d1), curb, true));
                    cur = Mathf.Min(to, d1);
                }
                if (to > cur) split.Add((cur, to, curb, false));
            }
            int piece = 0;
            foreach (var (from, to, curb, deck) in split)
            {
                if (to - from < 1f) continue;
                foreach (var (mesh, origin) in CourseRoadBuilder.Build(line, $"Road_{e.id}_{piece++}", from, to, curb, ground))
                    AddStatic(roadRoot, SaveMesh(mesh, GenDir, mesh.name), origin, deck ? deckMats : roadMats);
            }
        }
        var plain = PlainAsphalt();
        var junctionMesh = SaveMesh(CourseRoadBuilder.BuildJunction("Junction", JunctionRadius), GenDir, "Junction");
        foreach (var n in net.nodes)
        {
            var go = AddStatic(roadRoot, junctionMesh, n.position, new[] { plain });
            go.name = $"Junction_{n.id}";
        }
        var terrainRoot = new GameObject("Terrain").transform;
        CourseSceneBuilder.AddTerrainChunks(terrainRoot, terrain.BuildChunks(seaLevel - 3f), terrainMat, GenDir + "/Terrain");

        // 바다: 큰 수면 하나(협곡 바닥에도 물이 참). 물 셰이더는 구운 지형 높이로 수심·물가 거품을 그림
        var heights = WaterBuilder.BakeHeights("Island", terrain);
        var seaMat = WaterBuilder.Material("Water_Sea", heights, seaLevel, WaterBuilder.SeaShallow, WaterBuilder.SeaDeep, 14f, 0.03f, reflect: 0.3f);
        WaterBuilder.Plane("Sea", new Vector3(terrain.Center.x, seaLevel, terrain.Center.y), 40000f, 40000f, seaMat);

        // 다리: 도로 중앙 아래 지형이 꺼진 구간(협곡)
        BuildBridges(net, terrain, roadRoot);

        // 지역 간판 장면(등대·항구·트랙·호수 데크·풍력발전기·아치 바위·폭포·가드레일)
        LandmarkBuilder.Build(new LandmarkBuilder.Context
        {
            Net = net, Terrain = terrain, SeaLevel = seaLevel, TrackCenter = trackCenter,
            Root = new GameObject("Landmarks").transform,
        });

        var lakeRect = terrain.LakeBounds();
        var lakeMat = WaterBuilder.Material("Water_Lake", heights, -0.2f, WaterBuilder.LakeShallow, WaterBuilder.LakeDeep, 3.5f, 0.06f, maxDepth: 8f);
        WaterBuilder.Plane("Lake", new Vector3(lakeRect.center.x, -0.2f, lakeRect.center.y), lakeRect.width + 40f, lakeRect.height + 40f, lakeMat);
        var pool = GameObject.Find("Pool");
        if (pool)
            pool.GetComponent<MeshRenderer>().sharedMaterial = WaterBuilder.Material("Water_Pool", heights, pool.transform.position.y,
                WaterBuilder.LakeShallow, WaterBuilder.LakeDeep, 3f, 0.08f, maxDepth: 10f);

        // 도로변 꾸미기(담·울타리·가로등·커브 표지·구간 아치·건초)
        RoadsideBuilder.Build(net, terrain, RegionAt, roadRoot);
        // 지역 볼거리(집·노점·만국기·보트 창고·통나무집·모닥불·헛간·풍차·양·전망대·돌탑·이정표) + 수평선의 먼 산 섬
        SceneryBuilder.PointsOfInterest(net, terrain, RegionAt, roadRoot);
        SceneryBuilder.FarIslands(roadRoot.parent ? roadRoot.parent : null, terrain.Center, seaLevel);
        // 연출 장면(협곡 길·바위 터널·호숫가 데크·능선 풍차)
        SetPieceBuilder.Build(features, net, terrain, RegionAt, roadRoot);

        // ---- 출발 아치 · 하늘 · 후처리 ----
        CourseSceneBuilder.BuildStart(route);
        var sunLight = CourseSceneBuilder.SetupLighting();
        SkyBuilder.Setup(sunLight);   // 기본 = 맑은 오후(구름), T 키로 노을
        var post = CourseSceneBuilder.SetupPostProcessing();

        // ---- 식생 ----
        VegetationBuilder.Build(edgeLines, terrain, RegionAt, GenDir + "/Vegetation_Island01.bytes", terrainMat.GetTexture("_BaseMap"));
        // 땅 그늘 굽기(나무·덤불·바위 + 건물·풍차 등 볼거리 자리)
        var shades = new List<(Vector2 p, float r, float k)>(VegetationBuilder.GroundShade);
        foreach (var (c, r) in terrain.Exclusions) if (r >= 4f && r <= 14f) shades.Add((c, r * 0.9f, 0.3f));
        CourseSceneBuilder.BakeGroundShade(terrainMat, terrain, shades);
        ClampTexture(terrainMat.GetTexture("_BaseMap"));

        // ---- 러너 · 카메라 ----
        var runner = RunnerRigBuilder.Create(route, runnerController, CourseRoadBuilder.RoadY);
        var cam = Camera.main;

        // 개발용 이동 도구(1~5 지역, Tab 코스, [ ] 건너뛰기, F 자유비행, M 섬 전체)
        var dev = new GameObject("DevTravel").AddComponent<DevTravel>();
        SetRef(dev, "network", net);
        SetRef(dev, "route", route);
        SetRef(dev, "runner", runner);
        SetRef(dev, "follow", cam.GetComponent<FollowCamera>());

        // 함께 달리는 봇·고스트·이름표, 하늘의 새
        Vector3 OnGround(Vector3 p) => new(p.x, Mathf.Max(0f, terrain.Height(p.x, p.z)), p.z);
        var birdSpots = new List<(Vector3, float)>
        {
            (new Vector3(lakeRect.center.x, 0f, lakeRect.center.y), 90f),
            (OnGround(regions["cliff"].center), 140f),
            (OnGround(regions["harbor"].center), 80f),
            (OnGround(regions["highland"].center), 120f),
        };
        CompanionBuilder.Build(route, runner, runnerController, birdSpots);

        // 기기 성능별 품질(폰: 30fps·렌더 배율·식생 거리·풀 밀도)
        var quality = new GameObject("Quality").AddComponent<ScrunQuality>();
        SetRef(quality, "postVolume", post);
        cam.farClipPlane = 12000f;   // 수평선의 먼 산 섬(6~8km)까지
        cam.nearClipPlane = 0.2f;
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.None;   // MSAA(URP 에셋 4x)가 계단을 처리
        camData.stopNaN = true;   // 아주 밝은 빛(등대) 근처에서 계산 오류가 블룸으로 퍼지는 것 방지

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        CourseSceneBuilder.BakeEnvironment();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);

        float total = edgeLines.Sum(l => l.Length);
        var routeInfo = string.Join(", ", net.routes.Select(r => $"{r.name} {net.BuildRoute(r.id).Length / 1000f:0.00}km"));
        Debug.Log($"[Scrun] {ScenePath} 생성 완료. 도로망 {total / 1000f:0.0}km, 지형 {terrain.Size / 1000f:0.0}km. 코스: {routeInfo}");
    }

    static float F(object o) => (float)(double)o;

    static void ClampTexture(Texture t)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
        if (!ti) return;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
    }

    // 도로 중앙 아래가 1.5m 넘게 꺼진 구간마다 다리 바닥판 + 교각
    static void BuildBridges(RoadNetwork net, CourseTerrainBuilder terrain, Transform parent)
    {
        // 돌 고가교: 다듬은 돌 색 + 높이 방향 돌 줄눈(지층 띠를 촘촘히)
        var concrete = VegetationBuilder.StyMat("Lm_BridgeStone", 0f, 1f, false, new Color(0.66f, 0.61f, 0.54f));
        concrete.SetTexture("_BaseMap", null);
        concrete.SetTexture("_DetailMap", CourseSceneBuilder.DetailNoise());
        concrete.SetFloat("_DetailScale", 0.35f);
        concrete.SetFloat("_DetailStrength", 0.35f);
        concrete.SetFloat("_StrataStrength", 0.45f);
        concrete.SetFloat("_StrataScale", 1.6f);
        concrete.SetFloat("_Wrap", 0.4f);
        var root = new GameObject("Bridges").transform;
        root.SetParent(parent, false);
        foreach (var e in net.edges)
        {
            var line = net.EdgeLine(e.id);
            var ranges = new List<(float a, float b)>();
            float start = -1f;
            for (float d = 0; d <= line.Length; d += 1f)
            {
                var p = line.GetPoint(d);
                bool low = terrain.Height(p.x, p.z) < p.y - 1.5f;   // 노면보다 1.5m 넘게 꺼짐 = 협곡
                if (low && start < 0f) start = d;
                if (!low && start >= 0f) { ranges.Add((start, d)); start = -1f; }
            }
            if (start >= 0f) ranges.Add((start, line.Length));

            int n = 0;
            foreach (var (a0, b0) in ranges)
            {
                float a = Mathf.Max(0f, a0 - 10f), b = Mathf.Min(line.Length, b0 + 10f);
                foreach (var (mesh, origin) in CourseRoadBuilder.BuildDeck(line, $"Bridge_{e.id}_{n}", a, b))
                    AddStatic(root, SaveMesh(mesh, GenDir, mesh.name), origin, new[] { concrete });
                // 고가교: 24m마다 교각(아래로 넓어짐) + 교각 사이 반원 아치 벽
                var mb = new StylizedFlora.MB();
                const float span = 24f, pierD = 3.6f, W = 8.6f;
                var piers = new List<float>();
                for (float d = a + 12f; d < b - 8f; d += span)
                {
                    var p = line.GetPoint(d);
                    if (terrain.Height(p.x, p.z) > p.y - 8f) continue;
                    piers.Add(d);
                    float ground = terrain.Height(p.x, p.z);
                    float top = p.y - 1.4f, bottom = ground - 3f;
                    var rot = Quaternion.LookRotation(line.GetForward(d));
                    RoadsideBuilder.Box(mb, new Vector3(p.x, (top + bottom) / 2f, p.z), new Vector3(W, top - bottom, pierD), rot, Color.white);
                    RoadsideBuilder.Box(mb, new Vector3(p.x, bottom + 4f, p.z), new Vector3(W + 2f, 8f, pierD + 1.6f), rot, new Color(0.85f, 0.85f, 0.85f));
                }
                for (int i = 0; i + 1 < piers.Count; i++)
                {
                    float d0 = piers[i] + pierD / 2f, d1 = piers[i + 1] - pierD / 2f;
                    if (d1 - d0 > span) continue;
                    float r = (d1 - d0) / 2f, mid = (d0 + d1) / 2f;
                    const int steps = 14;
                    int baseIdx = mb.V.Count;
                    for (int k = 0; k <= steps; k++)
                    {
                        float d = Mathf.Lerp(d0, d1, (float)k / steps);
                        var p = line.GetPoint(d); var right = line.GetRight(d);
                        float top = p.y - 1.4f;
                        float x = d - mid;
                        float arch = top - 2.2f - r + Mathf.Sqrt(Mathf.Max(0f, r * r - x * x));   // 아치 안쪽 선(꼭대기는 바닥판 2.2m 아래)
                        foreach (int side in new[] { -1, 1 })
                        {
                            var wallN = right * side;
                            mb.Add(p + right * (side * W / 2f) + Vector3.up * (top - p.y), wallN, Color.white);
                            mb.Add(new Vector3(p.x, arch, p.z) + right * (side * W / 2f), wallN, Color.white);
                        }
                        // 아치 아랫면
                        var down = new Vector3(0f, -1f, 0f);
                        mb.Add(new Vector3(p.x, arch, p.z) - right * (W / 2f), down, new Color(0.8f, 0.8f, 0.8f));
                        mb.Add(new Vector3(p.x, arch, p.z) + right * (W / 2f), down, new Color(0.8f, 0.8f, 0.8f));
                    }
                    const int per = 6;
                    for (int k = 0; k < steps; k++)
                    {
                        int c = baseIdx + k * per, nx = c + per;
                        // 왼쪽 벽(c+0 위, c+1 아래), 오른쪽 벽(c+2, c+3), 아랫면(c+4 왼, c+5 오)
                        void Q(int p0, int p1, int p2, int p3, Vector3 want)
                        {
                            var nrm = Vector3.Cross(mb.V[p1] - mb.V[p0], mb.V[p2] - mb.V[p0]);
                            if (Vector3.Dot(nrm, want) >= 0) { mb.Tri(p0, p1, p2); mb.Tri(p0, p2, p3); }
                            else { mb.Tri(p0, p2, p1); mb.Tri(p0, p3, p2); }
                        }
                        Q(c, nx, nx + 1, c + 1, mb.N[c]);
                        Q(c + 2, nx + 2, nx + 3, c + 3, mb.N[c + 2]);
                        Q(c + 4, nx + 4, nx + 5, c + 5, Vector3.down);
                    }
                }
                if (mb.V.Count > 0)
                    AddStatic(root, SaveMesh(mb.ToMesh($"Viaduct_{e.id}_{n}"), GenDir, $"Viaduct_{e.id}_{n}"), Vector3.zero, new[] { concrete });
                // 다리 양옆 협곡 가장자리는 나무를 비워 협곡이 내려다보이게
                for (float d = a - 40f; d <= b + 40f; d += 8f)
                {
                    var p = line.GetPoint(Mathf.Clamp(d, 0f, line.Length));
                    foreach (int side in new[] { -1, 1 })
                    {
                        var q = p + line.GetRight(Mathf.Clamp(d, 0f, line.Length)) * (side * 30f);
                        terrain.Exclusions.Add((new Vector2(q.x, q.z), 24f));
                    }
                }
                Debug.Log($"[Scrun] 다리 {e.id}: {a:0}~{b:0}m ({b - a:0}m)");
                n++;
            }
        }
    }

    // [x, z] 또는 [x, z, 높이]: 가로는 scale 적용, 높이는 그대로
    static Vector3 V(object o, float k)
    {
        var a = (List<object>)o;
        return new Vector3((float)(double)a[0] * k, a.Count > 2 ? (float)(double)a[2] : 0f, (float)(double)a[1] * k);
    }

    const float MaxGrade = 0.07f, Landing = 30f;

    // 엣지 높이표: 평면 곡선 위 기준점(시작 노드, 경유점들, 끝 노드)의 높이를 코사인으로 잇고 → 경사 제한 → 다듬기
    static List<float> HeightProfile(RoadNetwork.Edge e, List<float> viaHeights)
    {
        var flat = Polyline.FromControlPoints(e.controlPoints.Select(c => new Vector3(c.x, 0f, c.z)).ToList(), false);
        float len = flat.Length;
        float h0 = e.controlPoints[0].y, h1 = e.controlPoints[^1].y;
        var keys = new List<(float d, float h)> { (0f, h0) };
        if (viaHeights != null)
            for (int i = 0; i < viaHeights.Count && i + 1 < e.controlPoints.Count - 1; i++)
            {
                var via = e.controlPoints[i + 1];
                float best = float.MaxValue, bestD = 0f;
                for (float d = 0; d <= len; d += 2f)
                {
                    var p = flat.GetPoint(d);
                    float sq = (p.x - via.x) * (p.x - via.x) + (p.z - via.z) * (p.z - via.z);
                    if (sq < best) { best = sq; bestD = d; }
                }
                keys.Add((Mathf.Clamp(bestD, Landing * 2f, len - Landing * 2f), viaHeights[i]));
            }
        keys.Add((len, h1));
        keys.Sort((a, b) => a.d.CompareTo(b.d));

        int n = Mathf.FloorToInt(len / RoadNetwork.HeightStep) + 2;
        var h = new float[n];
        for (int i = 0; i < n; i++)
        {
            float d = Mathf.Min(len, i * RoadNetwork.HeightStep);
            int k = 0;
            while (k < keys.Count - 2 && keys[k + 1].d < d) k++;
            var (da, ha) = keys[k]; var (db, hb) = keys[k + 1];
            float t = db > da ? Mathf.Clamp01((d - da) / (db - da)) : 0f;
            h[i] = Mathf.Lerp(ha, hb, 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI));
        }
        float maxStep = MaxGrade * RoadNetwork.HeightStep;
        void Clamp()
        {
            for (int i = 1; i < n; i++) h[i] = Mathf.Clamp(h[i], h[i - 1] - maxStep, h[i - 1] + maxStep);
            for (int i = n - 2; i >= 0; i--) h[i] = Mathf.Clamp(h[i], h[i + 1] - maxStep, h[i + 1] + maxStep);
        }
        for (int pass = 0; pass < 4; pass++)
        {
            // 교차로 앞뒤 평평하게(원판과 높이가 맞게)
            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Min(len, i * RoadNetwork.HeightStep);
                float wa = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Landing, Landing * 2f, d));
                float wb = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Landing, Landing * 2f, len - d));
                h[i] = Mathf.Lerp(Mathf.Lerp(h[i], h0, wa), h1, wb);
            }
            Clamp();
            var sm = (float[])h.Clone();
            for (int i = 2; i < n - 2; i++) sm[i] = (h[i - 2] + h[i - 1] * 2f + h[i] * 3f + h[i + 1] * 2f + h[i + 2]) / 9f;
            h = sm;
        }
        h[0] = h0; h[n - 1] = h1;
        return h.ToList();
    }

    static GameObject AddStatic(Transform parent, Mesh mesh, Vector3 pos, Material[] mats)
    {
        var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterials = mats;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    // 교차로용 차선 없는 아스팔트: 도로 텍스처와 같은 밝기(0.27)로 맞춘 실사, 1장 = 3m
    static Material PlainAsphalt()
    {
        var mat = VegetationBuilder.StyMat("Course_AsphaltPlain", 0f, 1f, false, new Color(0.33f, 0.35f, 0.39f));
        mat.SetTexture("_BaseMap", null);
        mat.SetTexture("_DetailMap", CourseSceneBuilder.DetailNoise());
        mat.SetFloat("_DetailScale", 0.15f);
        mat.SetFloat("_DetailStrength", 0.25f);
        mat.SetFloat("_Wrap", 0.3f);
        return mat;
    }
}
