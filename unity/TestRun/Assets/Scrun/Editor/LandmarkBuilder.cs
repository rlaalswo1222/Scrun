using System.Collections.Generic;
using System.Linq;
using Scrun;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ScrunEditorUtil;

// 섬 지역별 간판 장면: 등대·항구(부두·범선·부표), 출발 광장, 400m 트랙, 호수 데크, 풍력발전기, 아치 바위·바위 기둥,
// 협곡 폭포, 해안 절벽 가드레일. 단순한 형태는 코드로 만들고, 소품은 Poly Haven 모델을 배치한다.
public static class LandmarkBuilder
{
    const string Dir = CourseSceneBuilder.GenDir + "/Island/Landmarks";
    const string Models = PolyHavenImportSetup.Folder + "Models/";

    public class Context
    {
        public RoadNetwork Net;
        public CourseTerrainBuilder Terrain;
        public float SeaLevel;
        public Vector3 TrackCenter;
        public Transform Root;
    }

    static Context ctx;

    public static void Build(Context c)
    {
        ctx = c;
        EnsureFolderPath(Dir);
        StartPlaza();
        LighthouseAndHarbor();
        Track400();
        LakeDeck();
        WindTurbines();
        SeaArch();
        Waterfall();
        CliffGuardRail();
    }

    // ================= 장소 =================

    static void StartPlaza()
    {
        var root = Group("StartPlaza");
        var h = ctx.Net.FindNode("H").position;
        // 회색 보도블록(60cm, 줄눈) — 텍스처 1장 = 2.4m
        var pavers = MakeTexture("Lm_Pavers", 512, 512, (u, v) =>
        {
            float x = u * 4f, y = v * 4f;
            int row = Mathf.FloorToInt(y);
            if (row % 2 == 1) x += 0.5f;   // 반장 엇갈림
            float fx = x - Mathf.Floor(x), fy = y - Mathf.Floor(y);
            float tile = TileNoise(Mathf.Repeat(Mathf.Floor(x) * 0.37f, 1f), Mathf.Repeat(row * 0.53f, 1f), 4, 9f);
            float g = 0.56f + 0.1f * tile + 0.05f * TileNoise(u, v, 32, 3f);
            if (fx < 0.03f || fy < 0.03f) g *= 0.55f;
            return new Color(g, g * 0.99f, g * 0.97f, 1f);
        });
        var mat = MakeMat("Lm_Plaza", Color.white, pavers, Vector2.one, 0.2f);
        var disc = SaveMesh(Disc("Plaza", 24f, 64, 0.012f, 2.4f), Dir, "Plaza");
        MeshObject("Plaza", disc, mat, root, h, Quaternion.identity);

        // 가로등·벤치를 원 둘레에, 도로 위는 건너뜀. 가로등은 코드로 만든 가벼운 모델(원본 3만 삼각형 → 수백)
        var lamps = new StylizedFlora.MB();
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2f;
            var p = h + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 22f;
            if (ctx.Terrain.RoadDistance(p.x, p.z) < 7.5f) continue;
            RoadsideBuilder.LampPost(lamps, p + Vector3.up * 0.012f, h - p);
            if (i % 3 == 1)
            {
                var b = h + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 17f;
                if (ctx.Terrain.RoadDistance(b.x, b.z) > 7.5f)
                    Prop("painted_wooden_bench", root, b, Mathf.Atan2(h.x - b.x, h.z - b.z) * Mathf.Rad2Deg + 180f);
            }
        }
        MeshObject("Lamps", SaveMesh(lamps.ToMesh("PlazaLamps"), Dir, "PlazaLamps"), VegetationBuilder.StyMat("Sty_Prop", 0f, 1f, false), root, Vector3.zero, Quaternion.identity);
    }

    static void LighthouseAndHarbor()
    {
        var root = Group("Harbor");
        var h = ctx.Net.FindNode("H").position;
        var center = new Vector3(ctx.Terrain.Center.x, 0f, ctx.Terrain.Center.y);
        var outward = (h - center); outward.y = 0f; outward.Normalize();
        var side = Vector3.Cross(Vector3.up, outward);

        // 등대: 광장에서 바다 쪽, 절벽 끝 25m 안쪽 (도로에서 비켜 옆으로 60m)
        var p = h + side * 60f;
        for (int i = 0; i < 400 && ctx.Terrain.CoastDistance(p.x, p.z) > 22f; i++) p += outward * 2f;
        p.y = ctx.Terrain.Height(p.x, p.z);
        Lighthouse(root, p);
        ctx.Terrain.Exclusions.Add((new Vector2(p.x, p.z), 8f));

        // 절벽 아래 부두: 등대 옆에서 바다로 나가며 해저가 해수면보다 낮아지는 곳부터
        var q = p - side * 30f;
        for (int i = 0; i < 400 && ctx.Terrain.Height(q.x, q.z) > ctx.SeaLevel - 3f; i++) q += outward * 2f;
        var deckRot = Quaternion.LookRotation(outward);
        Pier(root, "HarborPier", q - outward * 6f, outward, 26f, ctx.SeaLevel + 1.6f, ctx.SeaLevel - 6f);
        foreach (var (asset, off) in new[] { ("wooden_crate_01", new Vector3(0.6f, 0f, 4f)), ("barrel_03", new Vector3(-0.8f, 0f, 6f)),
                                             ("barrel_03", new Vector3(-0.5f, 0f, 7f)), ("lifebuoy", new Vector3(1.2f, 0.4f, 10f)) })
        {
            var prop = Prop(asset, root, q + deckRot * off, deckRot.eulerAngles.y + Random.Range(0f, 360f));
            SetBottomAt(prop, ctx.SeaLevel + 1.62f);
        }

        // 범선과 부표
        var ship = Prop("dutch_ship_medium", root, q + outward * 110f + side * 30f, deckRot.eulerAngles.y + 70f);
        SetBottomAt(ship, ctx.SeaLevel - 2.5f);
        CullWhenSmall(ship, 0.02f);   // 멀어서 작게 보이면 그리지 않음(7만 삼각형)
        for (int i = 0; i < 2; i++)
        {
            var b = Prop("ocean_buoy", root, q + outward * (45f + 30f * i) - side * (20f + 15f * (i % 2)), i * 47f);
            SetBottomAt(b, ctx.SeaLevel - 0.8f);
            CullWhenSmall(b, 0.01f);
        }
    }

    static void Lighthouse(Transform parent, Vector3 basePos)
    {
        var root = new GameObject("Lighthouse").transform;
        root.SetParent(parent, false);
        root.position = basePos;

        // 몸통: 흰·빨강 띠(텍스처), 아래가 넓은 원뿔대
        var stripes = MakeTexture("Lm_LighthouseStripes", 8, 64, (u, v) =>
            (Mathf.FloorToInt(v * 4f) % 2 == 0) ? new Color(0.93f, 0.92f, 0.88f) : new Color(0.72f, 0.12f, 0.1f));
        var bodyMat = MakeMat("Lm_LighthouseBody", Color.white, stripes, Vector2.one, 0.35f);
        var body = Lathe("LighthouseBody", new[] { (3.4f, -1f), (3.3f, 0f), (2.4f, 22f) }, 40, 24f, false);
        MeshObject("Body", SaveMesh(body, Dir, "LighthouseBody"), bodyMat, root, basePos, Quaternion.identity);

        var dark = MakeMat("Lm_DarkMetal", new Color(0.12f, 0.12f, 0.13f), smoothness: 0.5f);
        var red = MakeMat("Lm_RedCap", new Color(0.62f, 0.1f, 0.08f), smoothness: 0.5f);
        var gallery = Lathe("LighthouseGallery", new[] { (0f, 22f), (3.6f, 22f), (3.6f, 22.4f), (0f, 22.4f) }, 40, 1f, true);
        MeshObject("Gallery", SaveMesh(gallery, Dir, "LighthouseGallery"), dark, root, basePos, Quaternion.identity);
        var rail = Lathe("LighthouseRail", new[] { (3.55f, 22.4f), (3.55f, 23.5f), (3.45f, 23.5f), (3.45f, 22.4f) }, 40, 1f, false);
        MeshObject("Rail", SaveMesh(rail, Dir, "LighthouseRail"), dark, root, basePos, Quaternion.identity);
        // 등실: 빛나는 유리 + 빨간 지붕
        var lamp = MakeMat("Lm_LampGlass", new Color(1f, 0.9f, 0.6f), smoothness: 0.9f, emission: new Color(1f, 0.82f, 0.45f) * 2.5f);
        var glass = Lathe("LighthouseLamp", new[] { (1.7f, 22.4f), (1.7f, 25.4f) }, 32, 3f, false);
        MeshObject("Lamp", SaveMesh(glass, Dir, "LighthouseLamp"), lamp, root, basePos, Quaternion.identity);
        var cap = Lathe("LighthouseCap", new[] { (2.1f, 25.4f), (1.6f, 26.4f), (0.3f, 27.6f), (0f, 28.2f) }, 32, 2f, false);
        MeshObject("Cap", SaveMesh(cap, Dir, "LighthouseCap"), red, root, basePos, Quaternion.identity);

        // 회전하는 스포트라이트는 등실 유리 가까이에서 과도한 반사를 만들어 블룸이 화면을 검게 깨뜨려서 뺌(빛나는 등실만)
    }

    static void Track400()
    {
        var root = Group("Track400");
        var c = ctx.TrackCenter;
        c.y = 0f;
        const float straight = 84.39f, r = 36.5f, width = 9.76f;   // 국제 규격 400m(1레인 측정선 r+0.3)

        // 트랙 노면: 빨간 우레탄 + 흰 레인 8줄
        var tartan = MakeTexture("Lm_Tartan", 1024, 32, (u, v) =>
        {
            float g = 0.85f + 0.15f * TileNoise(u, v, 64, 3f);
            var col = new Color(0.62f, 0.21f, 0.15f) * g;
            float lane = u * 8f;
            if (Mathf.Abs(lane - Mathf.Round(lane)) * (width / 8f) < 0.025f) col = new Color(0.93f, 0.93f, 0.92f);
            col.a = 1f;
            return col;
        });
        var trackMat = MakeMat("Lm_Track", Color.white, tartan, Vector2.one, 0.2f);
        var mesh = new Mesh { name = "Track400" };
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        int n = 400;
        float perimeter = 2f * straight + 2f * Mathf.PI * r;
        for (int i = 0; i <= n; i++)
        {
            float s = (float)i / n * perimeter;
            var (pIn, nrm) = Stadium(s, straight, r);
            v.Add(pIn + Vector3.up * 0.03f);
            v.Add(pIn + nrm * width + Vector3.up * 0.03f);
            uv.Add(new Vector2(0f, s / 2f)); uv.Add(new Vector2(1f, s / 2f));
            if (i < n)
            {
                int k = i * 2;
                // 안쪽(0)→바깥(1), 진행 방향이 반시계라 위에서 볼 때 시계방향이 되게
                t.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });
            }
        }
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        FixWindingUp(mesh);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        MeshObject("Track", SaveMesh(mesh, Dir, "Track400"), trackMat, root, c, Quaternion.identity);

        // 안쪽 잔디밭
        var grass = MakeMat("Lm_Infield", new Color(0.34f, 0.52f, 0.19f));   // 손그림풍 잔디(줄무늬는 정점 없이 단색 + 잔결)
        grass.SetTexture("_DetailMap", CourseSceneBuilder.DetailNoise());
        grass.SetFloat("_DetailScale", 0.25f);
        grass.SetFloat("_DetailStrength", 0.45f);
        var inf = new Mesh { name = "Infield" };
        var iv = new List<Vector3> { Vector3.up * 0.02f }; var iuv = new List<Vector2> { Vector2.zero }; var it = new List<int>();
        for (int i = 0; i <= n; i++)
        {
            var (pIn, _) = Stadium((float)i / n * perimeter, straight, r);
            iv.Add(pIn + Vector3.up * 0.02f); iuv.Add(new Vector2(pIn.x, pIn.z) / 2f);
            if (i > 0) it.AddRange(new[] { 0, i, i + 1 });
        }
        inf.SetVertices(iv); inf.SetUVs(0, iuv); inf.SetTriangles(it, 0);
        FixWindingUp(inf);
        inf.RecalculateNormals(); inf.RecalculateBounds();
        MeshObject("Infield", SaveMesh(inf, Dir, "Infield"), grass, root, c, Quaternion.identity);

        // 결승선
        var white = MakeMat("Lm_White", new Color(0.93f, 0.93f, 0.92f), smoothness: 0.3f);
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(line.GetComponent<Collider>());
        line.name = "FinishLine";
        line.transform.SetParent(root, false);
        line.transform.position = c + new Vector3(straight / 2f, 0.035f, -r - width / 2f);
        line.transform.localScale = new Vector3(0.05f, 0.005f, width);
        line.GetComponent<MeshRenderer>().sharedMaterial = white;
    }

    // 스타디움(직선 2 + 반원 2) 안쪽 경계 위 s(m) 지점과 바깥 방향. 반시계 진행, 아래 직선부터
    static (Vector3 p, Vector3 n) Stadium(float s, float straight, float r)
    {
        float half = straight / 2f, arc = Mathf.PI * r;
        if (s < straight) return (new Vector3(-half + s, 0f, -r), Vector3.back);
        s -= straight;
        if (s < arc) { float a = -Mathf.PI / 2f + s / r; var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); return (new Vector3(half, 0f, 0f) + n * r, n); }
        s -= arc;
        if (s < straight) return (new Vector3(half - s, 0f, r), Vector3.forward);
        s -= straight;
        { float a = Mathf.PI / 2f + s / r; var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); return (new Vector3(-half, 0f, 0f) + n * r, n); }
    }

    static void LakeDeck()
    {
        var root = Group("LakeDeck");
        var line = ctx.Net.EdgeLine("lake_west");
        float d = line.Length * 0.5f;
        var p = line.GetPoint(d);
        var right = line.GetRight(d);   // 호수는 오른쪽(섬 안쪽)
        // 물가를 찾아 데크 시작점으로
        var q = p + right * 8f;
        for (int i = 0; i < 100 && ctx.Terrain.Height(q.x, q.z) > -0.3f; i++) q += right * 1f;
        q -= right * 3f;
        Pier(root, "LakePier", q - right * 2f, right, 16f, 0.45f, -4f);
        var bench = p + right * 9.5f + line.GetForward(d) * 4f;
        Prop("painted_wooden_bench", root, bench, Quaternion.LookRotation(right).eulerAngles.y);
        var table = p + right * 10f - line.GetForward(d) * 6f;
        Prop("wooden_picnic_table", root, table, Quaternion.LookRotation(line.GetForward(d)).eulerAngles.y);
        ctx.Terrain.Exclusions.Add((new Vector2(bench.x, bench.z), 2f));
        ctx.Terrain.Exclusions.Add((new Vector2(table.x, table.z), 3f));
    }

    static void WindTurbines()
    {
        var root = Group("WindTurbines");
        var region = ctx.Net.regions.First(r => r.id == "highland");
        var white = MakeMat("Lm_Turbine", new Color(0.9f, 0.91f, 0.92f), smoothness: 0.55f);
        var tower = SaveMesh(Lathe("TurbineTower", new[] { (2.3f, -2f), (2.2f, 0f), (1.3f, 70f), (0f, 70f) }, 24, 70f, false), Dir, "TurbineTower");
        var nacelle = SaveMesh(Box("TurbineNacelle", new Vector3(3.4f, 3.6f, 11f)), Dir, "TurbineNacelle");
        var hub = SaveMesh(Lathe("TurbineHub", new[] { (0f, -1.2f), (1.6f, -0.6f), (1.7f, 0.6f), (0f, 2.6f) }, 20, 3f, false), Dir, "TurbineHub");
        var blade = SaveMesh(Blade("TurbineBlade", 38f, 3.2f, 0.9f), Dir, "TurbineBlade");

        // 지역 안 격자 후보 중 도로·해안에서 떨어진 높은 곳 7개
        var cands = new List<Vector3>();
        for (float x = -region.radius; x <= region.radius; x += 40f)
            for (float z = -region.radius; z <= region.radius; z += 40f)
            {
                var p = region.center + new Vector3(x, 0f, z);
                if ((p - region.center).magnitude > region.radius) continue;
                if (ctx.Terrain.RoadDistance(p.x, p.z) < 70f || ctx.Terrain.CoastDistance(p.x, p.z) < 80f) continue;
                p.y = ctx.Terrain.Height(p.x, p.z);
                if (p.y < 0f) continue;
                cands.Add(p);
            }
        var chosen = new List<Vector3>();
        foreach (var p in cands.OrderByDescending(p => p.y))
        {
            if (chosen.Any(c => (c - p).magnitude < 170f)) continue;
            chosen.Add(p);
            if (chosen.Count == 7) break;
        }
        var facing = Quaternion.Euler(0f, 225f, 0f);   // 남서풍을 향함
        int i = 0;
        foreach (var p in chosen)
        {
            var t = new GameObject($"Turbine_{i++}").transform;
            t.SetParent(root, false);
            t.SetPositionAndRotation(p, facing);
            MeshObject("Tower", tower, white, t, p, facing);
            MeshObject("Nacelle", nacelle, white, t, p + Vector3.up * 71.5f + facing * Vector3.back * 2f, facing);
            var rotor = new GameObject("Rotor").transform;
            rotor.SetParent(t, false);
            rotor.SetPositionAndRotation(p + Vector3.up * 71.5f + facing * Vector3.forward * 4.2f, facing * Quaternion.Euler(0f, 0f, Random.Range(0f, 120f)));
            MeshObject("Hub", hub, white, rotor, rotor.position, rotor.rotation * Quaternion.Euler(90f, 0f, 0f));
            for (int b = 0; b < 3; b++)
                MeshObject($"Blade{b}", blade, white, rotor, rotor.position, rotor.rotation * Quaternion.Euler(0f, 0f, b * 120f));
            rotor.gameObject.AddComponent<Scrun.Rotator>().Set(Vector3.forward, 22f + Random.Range(-3f, 3f));
            ctx.Terrain.Exclusions.Add((new Vector2(p.x, p.z), 6f));
        }
        Debug.Log($"[Scrun] 풍력발전기 {chosen.Count}기");
    }

    static void SeaArch()
    {
        var root = Group("SeaRocks");
        var line = ctx.Net.EdgeLine("cliff_road");
        float d = line.Length * 0.55f;
        var p = line.GetPoint(d);
        var right = line.GetRight(d);
        // 바다 쪽 = 해안 거리가 줄어드는 쪽
        var sea = ctx.Terrain.CoastDistance((p + right * 50f).x, (p + right * 50f).z) < ctx.Terrain.CoastDistance(p.x, p.z) ? right : -right;
        var basePos = p + sea * 210f; basePos.y = 0f;
        // 손그림풍 바위(실사 텍스처 대신 단색 + 높이 방향 지층 띠) → 섬의 다른 바위·절벽과 같은 화풍
        var rock = VegetationBuilder.StyMat("Lm_SeaRockSty", 0f, 1f, false, new Color(0.52f, 0.40f, 0.31f));
        rock.SetTexture("_BaseMap", null);
        rock.SetTexture("_DetailMap", CourseSceneBuilder.DetailNoise());
        rock.SetFloat("_StrataStrength", 0.55f);
        rock.SetFloat("_StrataScale", 0.3f);
        rock.SetFloat("_Wrap", 0.35f);
        NatureTextures.ApplyRock(rock, 0.07f, 0.3f);   // 사진 바위 결(거대한 바위라 크게)
        var arch = SaveMesh(ArchMesh("SeaArch", 42f, 11f), Dir, "SeaArch");
        // 아치 면이 해안과 나란하게(길에서 비스듬히 구멍이 보이게)
        MeshObject("Arch", arch, rock, root, basePos, Quaternion.LookRotation(sea) * Quaternion.Euler(0f, 20f, 0f));
        var fwd = line.GetForward(d);
        foreach (var (off, h, rad, seed) in new[] { (fwd * 90f + sea * 40f, 48f, 13f, 1f), (-fwd * 70f + sea * 10f, 36f, 10f, 2f), (fwd * 30f + sea * 120f, 58f, 15f, 3f) })
        {
            var stack = SaveMesh(Stack($"SeaStack{seed}", h, rad, seed), Dir, $"SeaStack{seed}");
            MeshObject("Stack", stack, rock, root, basePos + off, Quaternion.Euler(0f, seed * 40f, 0f));
        }
    }

    static void Waterfall()
    {
        var root = Group("Waterfall");
        var canyon = ctx.Terrain.Canyons[0];
        Vector2 a = canyon.pts[0], b = canyon.pts[1];
        var dir2 = (b - a).normalized;
        var dir = new Vector3(dir2.x, 0f, dir2.y);
        // 협곡 막다른 끝 벽에서 협곡 방향으로 떨어짐
        var top = new Vector3(a.x, 0f, a.y) - dir * (canyon.width * 0.42f);
        for (int i = 0; i < 60 && ctx.Terrain.Height(top.x, top.z) < -0.5f; i++) top -= dir * 1f;
        top.y = ctx.Terrain.Height(top.x, top.z) + 0.3f;

        // 세로 물줄기 무늬 + 양옆 가장자리는 투명하게(가로 1장 = 폭포 전체 폭)
        var streaks = MakeTexture("Lm_WaterfallStreaks", 256, 256, (u, v) =>
        {
            float s = TileNoise(u * 0.999f, v * 0.2f, 48, 7f) * 0.65f + TileNoise(u * 0.999f, v, 12, 2f) * 0.35f;
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.18f, Mathf.Min(u, 1f - u)));
            float a2 = Mathf.Clamp01(0.25f + s * 0.75f) * edge;
            return new Color(0.82f + 0.18f * s, 0.9f + 0.1f * s, 0.97f, a2);
        });
        var water = Transparent("Lm_Waterfall", streaks, new Color(0.92f, 0.97f, 1f, 0.9f), new Vector2(1f, 1.5f));
        float fall = top.y - ctx.SeaLevel;
        var sheet = SaveMesh(CascadeSheet("WaterfallSheet", top, dir, 16f), Dir, "WaterfallSheet");
        var go = MeshObject("Falls", sheet, water, root, Vector3.zero, Quaternion.identity);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<Scrun.UVScroller>().Set(new Vector2(0f, -1.1f));

        // 물보라
        var mist = new GameObject("Mist");
        mist.transform.SetParent(root, false);
        // 물줄기 끝 = 메쉬의 가장 낮은 점들 → 물보라와 웅덩이
        var sv = sheet.vertices;
        var bottom = sv[sv.Length - 1];
        foreach (var p in sv) if (p.y < bottom.y) bottom = p;
        mist.transform.position = bottom + Vector3.up * 1f;
        var pool = SaveMesh(Disc("WaterfallPool", 16f, 40, 0f, 4f), Dir, "WaterfallPool");
        var poolGo = MeshObject("Pool", pool, MakeMat("Course_Water", new Color(0.09f, 0.19f, 0.23f), smoothness: 0.9f), root,
                                new Vector3(bottom.x, bottom.y - 0.2f, bottom.z), Quaternion.identity);
        poolGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var ps = mist.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 4f; main.startSpeed = 1.5f; main.startSize = new ParticleSystem.MinMaxCurve(5f, 10f);
        main.startColor = new Color(1f, 1f, 1f, 0.12f); main.maxParticles = 200; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 30f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(16f, 1f, 4f);
        var pr = mist.GetComponent<ParticleSystemRenderer>();
        var soft = MakeTexture("Lm_SoftDot", 64, 64, (u, v) => { float r = Vector2.Distance(new Vector2(u, v), Vector2.one * 0.5f) * 2f; return new Color(1f, 1f, 1f, Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r)); });
        var pm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        pm.SetTexture("_BaseMap", soft);
        pm.SetFloat("_Surface", 1f); pm.SetFloat("_Blend", 0f);
        pm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        pm.SetOverrideTag("RenderType", "Transparent");
        pm.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); pm.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); pm.SetFloat("_ZWrite", 0f);
        pm.renderQueue = (int)RenderQueue.Transparent;
        string pmPath = Root + "/Materials/Lm_Mist.mat";
        AssetDatabase.DeleteAsset(pmPath);
        AssetDatabase.CreateAsset(pm, pmPath);
        pr.sharedMaterial = pm;
        ctx.Terrain.Exclusions.Add((new Vector2(top.x, top.z), 12f));
    }

    static void CliffGuardRail()
    {
        var root = Group("GuardRail");
        var line = ctx.Net.EdgeLine("cliff_road");
        var steel = MakeMat("Lm_Steel", new Color(0.62f, 0.64f, 0.66f), smoothness: 0.55f);
        float off = CourseRoadBuilder.ShoulderOuter - 0.4f;
        for (float d0 = 10f; d0 < line.Length - 10f; d0 += 200f)
        {
            float d1 = Mathf.Min(line.Length - 10f, d0 + 200f);
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            var origin = line.GetPoint((d0 + d1) / 2f);
            for (float d = d0; d < d1; d += 1f)
            {
                var p = line.GetPoint(d); var r = line.GetRight(d);
                var sea = ctx.Terrain.CoastDistance((p + r * 40f).x, (p + r * 40f).z) < ctx.Terrain.CoastDistance((p - r * 40f).x, (p - r * 40f).z) ? r : -r;
                var p2 = line.GetPoint(d + 1f); var r2 = line.GetRight(d + 1f); var sea2 = Vector3.Dot(r2, sea) > 0 ? r2 : -r2;
                // 레일 판(높이 0.55~0.85m) 앞뒤 면
                var a0 = p + sea * off - origin; var a1 = p2 + sea2 * off - origin;
                AddQuad(verts, norms, uvs, tris, a0 + Vector3.up * 0.55f, a1 + Vector3.up * 0.55f, a1 + Vector3.up * 0.85f, a0 + Vector3.up * 0.85f);
                AddQuad(verts, norms, uvs, tris, a1 + Vector3.up * 0.55f, a0 + Vector3.up * 0.55f, a0 + Vector3.up * 0.85f, a1 + Vector3.up * 0.85f);
                if (Mathf.Repeat(d, 4f) < 1f)
                    AddBoxTo(verts, norms, uvs, tris, a0 + sea * 0.12f + Vector3.up * 0.45f, new Vector3(0.12f, 0.9f, 0.12f), Quaternion.LookRotation(line.GetForward(d)));
            }
            var mesh = new Mesh { name = $"GuardRail_{d0:0}" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            MeshObject(mesh.name, SaveMesh(mesh, Dir, mesh.name), steel, root, origin, Quaternion.identity);
        }
    }

    // ================= 메쉬 도구 =================

    // 회전체: (반지름, 높이) 단면을 y축으로 돌림. cap=true면 윗·아랫면 법선 위/아래
    static Mesh Lathe(string name, (float r, float y)[] profile, int seg, float vScale, bool cap)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float len = 0f;
        var cum = new float[profile.Length];
        for (int i = 1; i < profile.Length; i++) cum[i] = len += Vector2.Distance(new Vector2(profile[i].r, profile[i].y), new Vector2(profile[i - 1].r, profile[i - 1].y));
        for (int i = 0; i < profile.Length; i++)
            for (int j = 0; j <= seg; j++)
            {
                float a = (float)j / seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * profile[i].r, profile[i].y, Mathf.Sin(a) * profile[i].r));
                uv.Add(new Vector2((float)j / seg, cum[i] / vScale));
            }
        for (int i = 0; i < profile.Length - 1; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = i * (seg + 1) + j, b = a + seg + 1;
                t.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
            }
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    static Mesh Disc(string name, float radius, int seg, float y, float uvMeters)
    {
        var v = new List<Vector3> { new(0f, y, 0f) }; var uv = new List<Vector2> { Vector2.zero }; var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = (float)i / seg * Mathf.PI * 2f;
            var p = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
            v.Add(p); uv.Add(new Vector2(p.x, p.z) / uvMeters);
            if (i > 0) t.AddRange(new[] { 0, i + 1, i });
        }
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    static Mesh Box(string name, Vector3 size)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        AddBoxTo(v, n, uv, t, Vector3.zero, size, Quaternion.identity);
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
        return m;
    }

    static void AddBoxTo(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 c, Vector3 size, Quaternion rot)
    {
        var h = size / 2f;
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var d in dirs)
        {
            var u = d == Vector3.up || d == Vector3.down ? Vector3.right : Vector3.up;
            var w = Vector3.Cross(d, u);
            var fc = Vector3.Scale(d, h);
            var du = Vector3.Scale(u, h); var dw = Vector3.Scale(w, h);
            // (u × w) = d 이므로 a→(+u)→(+u+w)→(+w) 순서면 바깥(d)을 봄
            AddQuad(v, n, uv, t, c + rot * (fc - du - dw), c + rot * (fc + du - dw), c + rot * (fc + du + dw), c + rot * (fc - du + dw));
        }
    }

    // 사각형 a→b→c→d. 법선 = (b-a)×(d-a) 방향이 보이는 면
    static void AddQuad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int i = v.Count;
        var nrm = Vector3.Cross(b - a, d - a).normalized;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        for (int k = 0; k < 4; k++) n.Add(nrm);
        uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
        // 삼각형 (a,b,c)의 앞면 = (b-a)×(c-a) 쪽 → 위 법선과 같은 쪽
        t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
    }

    // 위에서 볼 때 앞면이 위를 향하도록 삼각형 순서 맞춤
    static void FixWindingUp(Mesh m)
    {
        var v = m.vertices; var t = m.triangles;
        for (int i = 0; i < t.Length; i += 3)
            if (Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y < 0f) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
        m.triangles = t;
    }

    // 날개: 뿌리 넓고 끝 좁은 얇은 판(+Y 방향)
    static Mesh Blade(string name, float length, float rootChord, float tipChord)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float th0 = 0.6f, th1 = 0.15f;
        Vector3 R(float x, float z) => new(x, 1.5f, z);
        Vector3 T(float x, float z) => new(x, length, z);
        var r0 = R(-rootChord * 0.3f, -th0); var r1 = R(rootChord * 0.7f, -th0); var r2 = R(rootChord * 0.7f, th0); var r3 = R(-rootChord * 0.3f, th0);
        var t0 = T(-tipChord * 0.3f, -th1); var t1 = T(tipChord * 0.7f, -th1); var t2 = T(tipChord * 0.7f, th1); var t3 = T(-tipChord * 0.3f, th1);
        AddQuad(v, n, uv, t, r0, t0, t1, r1);   // 앞
        AddQuad(v, n, uv, t, r2, t2, t3, r3);   // 뒤
        AddQuad(v, n, uv, t, r1, t1, t2, r2);   // 바깥 모서리
        AddQuad(v, n, uv, t, r3, t3, t0, r0);   // 안쪽 모서리
        AddQuad(v, n, uv, t, t0, t3, t2, t1);   // 끝
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
        // 면 방향이 섞였을 수 있어 바깥을 향하게 보정
        FixOutward(m);
        return m;
    }

    // 중심에서 바깥을 향하도록 삼각형 순서·법선 맞춤(볼록한 모양용)
    static void FixOutward(Mesh m)
    {
        var v = m.vertices; var t = m.triangles; var c = m.bounds.center;
        for (int i = 0; i < t.Length; i += 3)
        {
            var nrm = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            var mid = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
            if (Vector3.Dot(nrm, mid - c) < 0f) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
        }
        m.triangles = t;
        m.RecalculateNormals();
    }

    // 아치 바위: 반원 경로를 따라 울퉁불퉁한 원 단면을 밀어냄(다리는 해저 아래까지)
    static Mesh ArchMesh(string name, float span, float thickness)
    {
        var path = new List<Vector3>();
        for (float y = -60f; y < -10f; y += 5f) path.Add(new Vector3(-span, y, 0f));
        for (int i = 0; i <= 36; i++) { float a = Mathf.PI - i / 36f * Mathf.PI; path.Add(new Vector3(Mathf.Cos(a) * span, -10f + Mathf.Sin(a) * span * 0.75f, 0f)); }
        for (float y = -15f; y >= -60f; y -= 5f) path.Add(new Vector3(span, y, 0f));
        return Sweep(name, path, i => thickness * (1.25f - 0.35f * Mathf.Sin(Mathf.Clamp01((float)i / path.Count) * Mathf.PI)), 18, 3.5f);
    }

    static Mesh Stack(string name, float height, float radius, float seed)
    {
        var path = new List<Vector3>();
        for (float y = -60f; y <= height; y += 3f) path.Add(new Vector3(0f, y, 0f));
        return Sweep(name, path, i => radius * Mathf.Lerp(1.15f, 0.55f, Mathf.Clamp01((float)i / path.Count)), 20, 4f + seed);
    }

    static Mesh Sweep(string name, List<Vector3> path, System.Func<int, float> radius, int ring, float noiseSeed)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float along = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            var tan = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
            var side = Mathf.Abs(Vector3.Dot(tan, Vector3.forward)) > 0.9f ? Vector3.right : Vector3.forward;
            var nrm = Vector3.Cross(tan, side).normalized; side = Vector3.Cross(nrm, tan).normalized;
            if (i > 0) along += Vector3.Distance(path[i], path[i - 1]);
            float rad = radius(i);
            for (int j = 0; j <= ring; j++)
            {
                float a = (float)j / ring * Mathf.PI * 2f;
                var dir = nrm * Mathf.Cos(a) + side * Mathf.Sin(a) * 1.2f;
                var basePt = path[i] + dir * rad;
                float nz = Mathf.PerlinNoise(basePt.x * 0.07f + noiseSeed, basePt.y * 0.07f + basePt.z * 0.05f) * 0.6f
                         + Mathf.PerlinNoise(basePt.x * 0.25f - noiseSeed, basePt.y * 0.25f) * 0.4f;
                float wob = Mathf.PerlinNoise(path[i].y * 0.04f + noiseSeed * 3f, noiseSeed) - 0.5f;   // 줄기 전체가 살짝 휘어짐
                v.Add(path[i] + side * wob * rad * 0.8f + dir * rad * (0.55f + 0.9f * nz));
                uv.Add(new Vector2((float)j / ring * rad * 6.3f / 6f, along / 6f));
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int j = 0; j < ring; j++)
            {
                int a = i * (ring + 1) + j, b = a + ring + 1;
                t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals();
        // 단면 중심에서 바깥을 보도록(경로 방향에 따라 뒤집혔으면 교정)
        var nv = m.normals; var center = path[path.Count / 2];
        int outward = 0;
        for (int k = 0; k < v.Count; k += 7) outward += Vector3.Dot(nv[k], v[k] - path[k / (ring + 1)]) > 0 ? 1 : -1;
        if (outward < 0)
        {
            var tt = m.triangles;
            for (int k = 0; k < tt.Length; k += 3) (tt[k + 1], tt[k + 2]) = (tt[k + 2], tt[k + 1]);
            m.triangles = tt;
            m.RecalculateNormals();
        }
        m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    // 폭포: 협곡 끝 경사면을 따라 지형 위 0.6m로 쏟아져 내리는 물줄기(월드 좌표 메쉬)
    static Mesh CascadeSheet(string name, Vector3 top, Vector3 dir, float width)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        var side = Vector3.Cross(Vector3.up, dir).normalized;
        int cols = 8, rows = 0;
        float along = 0f;
        var prevMid = top;
        for (float s = 0f; s < 150f; s += 1.5f, rows++)
        {
            var mid = top + dir * s;
            float hMid = Mathf.Max(ctx.Terrain.Height(mid.x, mid.z), ctx.SeaLevel);
            if (rows > 0) along += Vector3.Distance(new Vector3(mid.x, hMid, mid.z), prevMid);
            prevMid = new Vector3(mid.x, hMid, mid.z);
            float w = width * (1f + 0.4f * Mathf.Clamp01(s / 60f));
            for (int j = 0; j <= cols; j++)
            {
                var p = mid + side * (((float)j / cols - 0.5f) * w);
                float h = Mathf.Max(ctx.Terrain.Height(p.x, p.z), ctx.SeaLevel) + 1.4f;
                v.Add(new Vector3(p.x, h, p.z));
                uv.Add(new Vector2((float)j / cols, along / 8f));
            }
            if (hMid <= ctx.SeaLevel + 0.1f) { rows++; break; }
            // 경사가 완만해지면(1.5m 앞에서 0.15m 미만 낮아짐, 20m 이후) 멈춤 → 웅덩이
            if (s > 20f)
            {
                var ahead = mid + dir * 1.5f;
                if (hMid - Mathf.Max(ctx.Terrain.Height(ahead.x, ahead.z), ctx.SeaLevel) < 0.15f) { rows++; break; }
            }
        }
        for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < cols; j++)
            {
                int a = i * (cols + 1) + j, b = a + cols + 1;
                t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // 폭포 물줄기: 위 가장자리에서 앞으로 조금 나가며 떨어지는 휘어진 판
    static Mesh FallSheet(string name, float width, float height, float bulge)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        int rows = 16, cols = 8;
        for (int i = 0; i <= rows; i++)
        {
            float f = (float)i / rows;
            float z = bulge * Mathf.Sin(f * Mathf.PI * 0.5f) * (1f - 0.3f * f);
            for (int j = 0; j <= cols; j++)
            {
                float x = ((float)j / cols - 0.5f) * width * (1f + 0.25f * f);
                v.Add(new Vector3(x, -f * height, z));
                uv.Add(new Vector2((float)j / cols, f * height / 8f));
            }
        }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                int a = i * (cols + 1) + j, b = a + cols + 1;
                t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // ================= 재질·소품 =================

    static string PHTex(string asset) => CourseSceneBuilder.PHTex + $"{asset}/{asset}_diff_1k.jpg";

    static Material Textured(string name, string diffPath, float tiling, float smooth, Color tint, string normalPath = null)
    {
        var mat = MakeMat(name, tint, AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath), Vector2.one * tiling, smooth);
        normalPath ??= diffPath.Replace("_diff_", "_nor_gl_").Replace(".jpg", ".png");
        var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (nrm) SetNormalMap(mat, nrm);
        return mat;
    }

    static Material Transparent(string name, Texture2D tex, Color color, Vector2 tiling)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetTexture("_BaseMap", tex);
        mat.SetTextureScale("_BaseMap", tiling);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static readonly Dictionary<string, Material> propMats = new();

    // Poly Haven 소품 배치: 재질 이름(부위)마다 URP 재질을 만들어 바꿔 끼움
    static GameObject Prop(string asset, Transform parent, Vector3 pos, float yaw, float scale = 1f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}{asset}/{asset}.fbx");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(parent, false);
        // 원본의 가져오기 회전(Z가 위인 FBX 보정 등)을 유지한 채 수직축으로만 돌림
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation);
        go.transform.localScale *= scale;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = PropMaterial(asset, mats[i] ? mats[i].name : asset);
            mr.sharedMaterials = mats;
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static Material PropMaterial(string asset, string matName)
    {
        string key = $"{asset}/{matName}";
        if (propMats.TryGetValue(key, out var cached) && cached) return cached;
        string dir = $"{Models}{asset}/";
        // 재질 이름에서 부위 추출: "modular_wooden_pier_planks" → "planks"
        string part = matName.StartsWith(asset + "_") ? matName.Substring(asset.Length + 1) : null;
        if (part == "table" && asset == "wooden_picnic_table") part = "top";   // 재질 이름과 텍스처 이름이 다른 경우
        string P(string map, string ext) => part != null && System.IO.File.Exists($"{dir}{asset}_{part}_{map}_1k.{ext}")
            ? $"{dir}{asset}_{part}_{map}_1k.{ext}" : $"{dir}{asset}_{map}_1k.{ext}";

        Material mat;
        string safe = $"Prop_{asset}_{part ?? "main"}";
        if (part == "glass") mat = MakeMat(safe, new Color(0.25f, 0.27f, 0.28f), smoothness: 0.95f);
        else if (part == "bulb") mat = MakeMat(safe, new Color(1f, 0.85f, 0.6f), smoothness: 0.5f, emission: new Color(1f, 0.75f, 0.45f) * 3f);
        else
        {
            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(P("diff", "jpg"));
            mat = MakeMat(safe, Color.white, diff, Vector2.one, 0.25f);
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(P("nor_gl", "png"));
            if (nrm) SetNormalMap(mat, nrm);
            var alphaPath = P("alpha", "png");
            if (System.IO.File.Exists(alphaPath) && diff)
            {
                // 돛처럼 잘라내기가 필요한 부위: 색+알파 합성
                var d = RawImage.Load(P("diff", "jpg")); var a = RawImage.Load(alphaPath);
                var px = new Color[d.Px.Length];
                for (int i = 0; i < px.Length; i++) { var c = d.Px[i]; c.a = a.Px[i].r; px[i] = c; }
                mat.SetTexture("_BaseMap", SaveTexture($"{safe}_diffA", d.W, d.H, px));
                SetCutoutDoubleSided(mat, 0.4f);
            }
        }
        EditorUtility.SetDirty(mat);
        propMats[key] = mat;
        return mat;
    }

    // 소품의 가까운 끝(방향 dir 기준 가장 뒤쪽)이 start에 오도록 수평 이동(부두를 물가에서 바다 쪽으로 뻗게)
    static void AlignStart(GameObject go, Vector3 start, Vector3 dir)
    {
        dir.y = 0f; dir.Normalize();
        float min = float.MaxValue;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            foreach (var v in mf.sharedMesh.vertices)
                min = Mathf.Min(min, Vector3.Dot(mf.transform.TransformPoint(v) - start, dir));
        go.transform.position -= dir * min;
    }

    // 나무 부두(코드 생성): 널판 + 양옆 들보 + 기둥. start에서 dir 방향으로 length m, 윗면 높이 deckY
    static void Pier(Transform parent, string name, Vector3 start, Vector3 dir, float length, float deckY, float bottomY)
    {
        dir.y = 0f; dir.Normalize();
        var rot = Quaternion.LookRotation(dir);
        var right = rot * Vector3.right;
        var mb = new StylizedFlora.MB();
        var rng = new System.Random(name.GetHashCode());
        float R() => (float)rng.NextDouble();
        var plank = new Color(0.62f, 0.46f, 0.30f);
        for (float s = 0f; s < length; s += 0.32f)
        {
            var c = new Vector3(start.x, deckY - 0.04f, start.z) + dir * s;
            RoadsideBuilder.Box(mb, c, new Vector3(2.6f, 0.08f, 0.28f), rot * Quaternion.Euler(0f, (R() - 0.5f) * 2f, 0f), plank * (0.85f + 0.25f * R()));
        }
        foreach (int side in new[] { -1, 1 })
        {
            var beam = new Vector3(start.x, deckY - 0.22f, start.z) + dir * (length / 2f) + right * 1.2f * side;
            RoadsideBuilder.Box(mb, beam, new Vector3(0.18f, 0.25f, length), rot, plank * 0.7f);
            for (float s = 0.5f; s < length; s += 3f)
            {
                var top = new Vector3(start.x, deckY + 0.5f, start.z) + dir * s + right * 1.25f * side;
                var post = new Vector3(top.x, (top.y + bottomY) / 2f, top.z);
                RoadsideBuilder.Box(mb, post, new Vector3(0.22f, top.y - bottomY, 0.22f), rot, plank * 0.6f);
            }
        }
        var go = MeshObject(name, SaveMesh(mb.ToMesh(name), Dir, name), VegetationBuilder.StyMat("Sty_Prop", 0f, 1f, false), parent, Vector3.zero, Quaternion.identity);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    // 화면 높이 비율이 작아지면 그리지 않음(LODGroup 1단계)
    static void CullWhenSmall(GameObject go, float screenHeight)
    {
        var group = go.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(screenHeight, go.GetComponentsInChildren<Renderer>()) });
        group.RecalculateBounds();
    }

    static void SetBottomAt(GameObject go, float y)
    {
        var b = WorldBounds(go);
        go.transform.position += Vector3.up * (y - b.min.y);
    }

    static void SetTopAt(GameObject go, float y)
    {
        // 데크 윗면 기준: 가장 높은 점이 아니라 판자 높이를 쓰기 위해 바운드 위쪽 15%를 데크로 봄
        var b = WorldBounds(go);
        go.transform.position += Vector3.up * (y - b.max.y);
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Transform Group(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(ctx.Root, false);
        return t;
    }

    static GameObject MeshObject(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, rot);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }
}
