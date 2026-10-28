using System.Collections.Generic;
using System.Linq;
using Scrun;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ScrunEditorUtil;

// 풍경 꾸미기 2단계:
//  1) 먼 섬(수평선 너머 눈 덮인 산맥) — 바다 위 하늘선이 비지 않게, 안개 속에 산꼭대기만 또렷이
//  2) 볼거리(약 200m마다) — 지역마다 다른 작은 장면: 항구 = 지중해식 집·노점·만국기, 호수 = 보트 창고·갈대·오두막,
//     숲 = 통나무집·모닥불·장작더미, 고원 = 농가·빨간 헛간·풍차·양 떼, 절벽 = 전망대·돌탑
// 모두 코드로 만든 정점 색 메쉬(Sty_Prop 하나)를 300m 덩어리로 합쳐 그리기 호출을 줄인다(풍차 날개만 따로 돌림).
public static class SceneryBuilder
{
    const string Dir = CourseSceneBuilder.GenDir + "/Island/Scenery";
    const float ChunkLen = 150f;

    static System.Random rng;
    static float R() => (float)rng.NextDouble();
    // 정점 색은 선형 값 → 눈으로 고른 색(sRGB)을 변환
    static Color L(float r, float g, float b) => new Color(r, g, b).linear;
    static Color L(Color c) => c.linear;

    // ======================= 먼 섬 =======================
    public static void FarIslands(Transform parent, Vector2 center, float seaLevel)
    {
        EnsureFolderPath(Dir);
        rng = new System.Random(808);
        var root = new GameObject("FarIslands").transform;
        root.SetParent(parent, false);
        var mat = VegetationBuilder.StyMat("Sty_FarIsland", 0f, 1f, false);
        mat.SetFloat("_Wrap", 0.5f);
        mat.SetFloat("_RimStrength", 0f);
        // (방위각°, 거리 km, 반지름 m, 높이 m)
        var list = new (float az, float dist, float radius, float height)[]
        {
            (20f, 6.2f, 1500f, 950f), (75f, 7.4f, 1900f, 1250f), (140f, 6.8f, 1300f, 700f),
            (200f, 7.8f, 2100f, 1100f), (255f, 6.4f, 1200f, 620f), (315f, 7.1f, 1700f, 1050f),
        };
        int i = 0;
        foreach (var (az, dist, radius, height) in list)
        {
            float a = az * Mathf.Deg2Rad;
            var pos = new Vector3(center.x + Mathf.Sin(a) * dist * 1000f, seaLevel - 6f, center.y + Mathf.Cos(a) * dist * 1000f);
            var mesh = SaveMesh(MountainIsland($"FarIsland_{i}", radius, height, i * 13.7f), Dir, $"FarIsland_{i}");
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            i++;
        }
    }

    // 산 섬 하나: 극좌표 격자, 능선 노이즈(봉우리 2~3개), 아래는 숲 초록 → 바위 → 위 눈
    static Mesh MountainIsland(string name, float radius, float height, float seed)
    {
        const int rings = 28, segs = 72;
        var mb = new StylizedFlora.MB();
        var heights = new float[rings + 1, segs];
        var pts = new Vector3[rings + 1, segs];
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s < segs; s++)
            {
                float t = (float)r / rings;
                float a = (float)s / segs * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float edge = radius * (1f + 0.18f * (Mathf.PerlinNoise(dir.x * 1.3f + seed, dir.y * 1.3f) - 0.5f) * 2f);
                var p2 = dir * edge * t;
                // 봉우리: 가운데가 높고 가장자리로 오목하게 떨어짐 + 능선
                float prof = Mathf.Pow(1f - t, 1.6f);
                float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(p2.x * 0.0012f + seed, p2.y * 0.0012f - seed) * 2f - 1f);
                float second = Mathf.Exp(-((p2 - dir * 0f - new Vector2(radius * 0.45f, radius * 0.2f)).sqrMagnitude) / (radius * radius * 0.08f));
                float h = height * (prof * (0.75f + 0.35f * ridge) + 0.35f * second * (1f - t));
                h += 25f * (Mathf.PerlinNoise(p2.x * 0.006f, p2.y * 0.006f) - 0.5f);
                if (r == rings) h = -20f;
                heights[r, s] = h;
                pts[r, s] = new Vector3(p2.x, h, p2.y);
            }
        // 면마다 법선(각진 손그림풍 산) + 높이·경사별 색
        var forest = L(0.22f, 0.34f, 0.20f); var rockC = L(0.50f, 0.47f, 0.48f); var rockD = L(0.38f, 0.36f, 0.38f); var snow = L(0.96f, 0.97f, 1f);
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segs; s++)
            {
                int s1 = (s + 1) % segs;
                Quad(pts[r, s], pts[r, s1], pts[r + 1, s1], pts[r + 1, s]);
            }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            foreach (var (p, q, w) in new[] { (a, c, b), (a, d, c) })
            {
                var n = Vector3.Cross(q - p, w - p).normalized;
                if (n.y < 0) { n = -n; }
                float hy = (p.y + q.y + w.y) / 3f / height;
                float steep = 1f - n.y;
                Color col = Color.Lerp(forest, Color.Lerp(rockD, rockC, Mathf.PerlinNoise(p.x * 0.004f, p.z * 0.004f)), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.4f, hy + steep * 0.3f)));
                float snowLine = 0.62f + 0.12f * Mathf.PerlinNoise(p.x * 0.003f + seed, p.z * 0.003f);
                if (hy > snowLine && steep < 0.75f) col = snow;
                int i0 = mb.Add(p, n, col), i1 = mb.Add(q, n, col), i2 = mb.Add(w, n, col);
                // 위에서 볼 때 반시계 = 앞면(법선 위쪽)
                if (Vector3.Dot(Vector3.Cross(q - p, w - p), Vector3.up) >= 0) mb.Tri(i0, i1, i2); else mb.Tri(i0, i2, i1);
            }
        }
        return mb.ToMesh(name);
    }

    // ======================= 볼거리 =======================
    public static void PointsOfInterest(RoadNetwork net, CourseTerrainBuilder terrain, System.Func<Vector3, string> regionAt, Transform parent)
    {
        EnsureFolderPath(Dir);
        rng = new System.Random(2026);
        var mat = VegetationBuilder.StyMat("Sty_Prop", 0f, 1f, false);
        var root = new GameObject("PointsOfInterest").transform;
        root.SetParent(parent, false);
        var junctions = net.nodes.Select(n => n.position).ToList();
        var placed = new List<(Vector2 c, float r)>();
        var counts = new Dictionary<string, int>();
        int windmills = 0;

        foreach (var e in net.edges)
        {
            var line = net.EdgeLine(e.id);
            var chunks = new Dictionary<int, StylizedFlora.MB>();
            StylizedFlora.MB Chunk(float s)
            {
                int k = Mathf.FloorToInt(s / ChunkLen);
                if (!chunks.TryGetValue(k, out var mb)) chunks[k] = mb = new StylizedFlora.MB();
                return mb;
            }
            Vector3 SidePoint(float s, float side, float off)
            {
                var p = line.GetPoint(s) + line.GetRight(s) * (side * off);
                p.y = terrain.Height(p.x, p.z);
                return p;
            }
            bool NearJunction(Vector3 p) => junctions.Any(j => (j - p).sqrMagnitude < 40f * 40f);
            bool Free(Vector3 p, float radius) => !placed.Any(q => (q.c - new Vector2(p.x, p.z)).sqrMagnitude < (q.r + radius) * (q.r + radius));
            // 건물 자리: 평평하고(경사 0.08 이하), 물·절벽·도로·다른 볼거리와 떨어진 곳
            bool Site(Vector3 p, float radius)
            {
                if (NearJunction(p) || terrain.Excluded(p.x, p.z) || !Free(p, radius)) return false;
                if (terrain.RoadDistance(p.x, p.z) < radius + 6f) return false;
                if (terrain.LakeMask(p.x, p.z) > 0.01f || p.y < -0.3f) return false;
                if (terrain.Island && terrain.CoastDistance(p.x, p.z) < radius + 12f) return false;
                for (int k = 0; k < 6; k++)
                {
                    float a = k / 6f * Mathf.PI * 2f;
                    float hx = terrain.Height(p.x + Mathf.Cos(a) * radius, p.z + Mathf.Sin(a) * radius);
                    if (Mathf.Abs(hx - p.y) > radius * 0.22f + 0.6f) return false;   // 건물 아래 기초가 1.6m 묻혀 있어 약간의 경사는 괜찮음
                }
                return true;
            }
            void Claim(Vector3 p, float radius)
            {
                placed.Add((new Vector2(p.x, p.z), radius));
                terrain.Exclusions.Add((new Vector2(p.x, p.z), radius + 1.5f));
            }
            int WaterSide(float s)
            {
                var a = SidePoint(s, 1f, 30f); var b = SidePoint(s, -1f, 30f);
                float la = terrain.LakeMask(a.x, a.z), lb = terrain.LakeMask(b.x, b.z);
                if (Mathf.Max(la, lb) > 0.05f) return la > lb ? 1 : -1;
                if (terrain.Island)
                {
                    float ca = terrain.CoastDistance(a.x, a.z), cb = terrain.CoastDistance(b.x, b.z);
                    if (Mathf.Min(ca, cb) < 60f) return ca < cb ? 1 : -1;
                }
                return 0;
            }
            void Count(string k) => counts[k] = (counts.TryGetValue(k, out int c) ? c : 0) + 1;

            // ---- 출발 마을: 출발 광장(H)에서 이어지는 길 첫 300m 양옆에 집을 줄지어, 60m마다 만국기 ----
            if (e.from == "H" || e.to == "H")
            {
                int houses = 0;
                for (float t = 45f; t < 330f; t += 16f + R() * 10f)
                {
                    float ss = e.from == "H" ? t : line.Length - t;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (R() < 0.25f) continue;
                        var p = SidePoint(ss, side, 17f + R() * 8f);
                        if (!Site(p, 6.5f)) continue;
                        var yaw = Quaternion.LookRotation(line.GetRight(ss) * -side) * Quaternion.Euler(0f, (R() - 0.5f) * 16f, 0f);
                        House(Chunk(ss), p, yaw);
                        Claim(p, 6.5f);
                        houses++;
                    }
                    if (Mathf.Repeat(t, 60f) < 26f && t < 260f)
                        Bunting(Chunk(ss), SidePoint(ss, -1f, 7.6f), SidePoint(ss, 1f, 7.6f));
                }
                counts["village"] = (counts.TryGetValue("village", out int vc) ? vc : 0) + houses;
            }

            for (float s = 60f + R() * 50f; s < line.Length - 60f; s += 120f + R() * 90f)
            {
                var region = regionAt(line.GetPoint(s));
                int water = WaterSide(s);
                int side = water != 0 ? -water : (R() < 0.5f ? 1 : -1);
                var fwd = line.GetForward(s);
                var toRoad = line.GetRight(s) * -side;
                string kind = Pick(region, water != 0);
                bool done = false;
                // 볼거리마다 크기·거리 후보 몇 군데를 시험
                for (int attempt = 0; attempt < 6 && !done; attempt++)
                {
                    float ss = s + (R() - 0.5f) * 40f;
                    if (ss < 40f || ss > line.Length - 40f) continue;
                    fwd = line.GetForward(ss); toRoad = line.GetRight(ss) * -side;
                    var yaw = Quaternion.LookRotation(toRoad);
                    switch (kind)
                    {
                        case "houses":
                        {
                            var p = SidePoint(ss, side, 20f + R() * 6f);
                            if (!Site(p, 9f)) break;
                            House(Chunk(ss), p, yaw * Quaternion.Euler(0f, (R() - 0.5f) * 20f, 0f));
                            var p2 = p + fwd * (11f + R() * 3f) - toRoad * (2f + R() * 4f); p2.y = terrain.Height(p2.x, p2.z);
                            if (Site(p2, 6f)) { House(Chunk(ss), p2, yaw * Quaternion.Euler(0f, (R() - 0.5f) * 30f, 0f)); Claim(p2, 6f); }
                            Claim(p, 9f); done = true; break;
                        }
                        case "stall":
                        {
                            var p = SidePoint(ss, side, 11.5f);
                            if (!Site(p, 4f)) break;
                            Stall(Chunk(ss), p, yaw); Claim(p, 4f); done = true; break;
                        }
                        case "bunting":
                        {
                            var a = SidePoint(ss, -1f, 7.6f); var b = SidePoint(ss, 1f, 7.6f);
                            var mid = line.GetPoint(ss);
                            if (NearJunction(a) || terrain.Height(mid.x, mid.z) < mid.y - 1f) break;
                            Bunting(Chunk(ss), a, b); done = true; break;
                        }
                        case "boathouse":
                        {
                            if (water == 0) { kind = "cabin"; break; }
                            // 물가 찾기: 도로에서 물 쪽으로 걸어가며 물에 닿는 곳
                            var dir = line.GetRight(ss) * water;
                            var q = line.GetPoint(ss) + dir * 10f;
                            int k = 0;
                            while (k++ < 60 && terrain.Height(q.x, q.z) > -0.25f) q += dir;
                            if (k >= 60 || !Free(q, 8f) || NearJunction(q)) break;
                            q.y = 0f;
                            Boathouse(Chunk(ss), q - dir * 3f, Quaternion.LookRotation(dir));
                            Claim(q, 8f); done = true; break;
                        }
                        case "cabin":
                        {
                            var p = SidePoint(ss, side, 19f + R() * 6f);
                            if (!Site(p, 8f)) break;
                            Cabin(Chunk(ss), p, yaw * Quaternion.Euler(0f, (R() - 0.5f) * 25f, 0f));
                            var fire = p + toRoad * 6.5f + fwd * (R() < 0.5f ? 4f : -4f); fire.y = terrain.Height(fire.x, fire.z);
                            Campfire(Chunk(ss), fire);
                            Claim(p, 8f); done = true; break;
                        }
                        case "camp":
                        {
                            var p = SidePoint(ss, side, 13f + R() * 4f);
                            if (!Site(p, 4.5f)) break;
                            Campfire(Chunk(ss), p);
                            var pile = p + fwd * 5f; pile.y = terrain.Height(pile.x, pile.z);
                            LogPile(Chunk(ss), pile, yaw);
                            Claim(p, 6f); done = true; break;
                        }
                        case "farm":
                        {
                            var p = SidePoint(ss, side, 26f + R() * 10f);
                            if (!Site(p, 12f)) break;
                            Barn(Chunk(ss), p, yaw * Quaternion.Euler(0f, (R() - 0.5f) * 30f, 0f));
                            var silo = p + fwd * 9f - toRoad * 3f; silo.y = terrain.Height(silo.x, silo.z);
                            Silo(Chunk(ss), silo);
                            Claim(p, 13f); done = true; break;
                        }
                        case "windmill":
                        {
                            if (windmills >= 4) { kind = "sheep"; break; }
                            var p = SidePoint(ss, side, 28f + R() * 15f);
                            if (!Site(p, 6f)) break;
                            Windmill(Chunk(ss), root, p, yaw * Quaternion.Euler(0f, 25f, 0f), windmills);
                            windmills++; Claim(p, 7f); done = true; break;
                        }
                        case "sheep":
                        {
                            var p = SidePoint(ss, side, 22f + R() * 25f);
                            if (!Site(p, 3f)) break;   // 양은 비탈에도 있음
                            int n = 4 + (int)(R() * 5f);
                            for (int k = 0; k < n; k++)
                            {
                                var q = p + new Vector3((R() - 0.5f) * 12f, 0f, (R() - 0.5f) * 12f);
                                q.y = terrain.Height(q.x, q.z);
                                Sheep(Chunk(ss), q, Quaternion.Euler(0f, R() * 360f, 0f));
                            }
                            Claim(p, 7f); done = true; break;
                        }
                        case "viewpoint":
                        {
                            int sea = water != 0 ? water : side;
                            var p = SidePoint(ss, sea, 10.5f);
                            if (NearJunction(p) || !Free(p, 5f) || terrain.Excluded(p.x, p.z)) break;
                            Viewpoint(Chunk(ss), p, Quaternion.LookRotation(line.GetRight(ss) * sea));
                            Claim(p, 5f); done = true; break;
                        }
                        case "cairn":
                        {
                            var p = SidePoint(ss, side, 10f + R() * 6f);
                            if (!Site(p, 2f)) break;
                            Cairn(Chunk(ss), p); Claim(p, 2f); done = true; break;
                        }
                        case "signpost":
                        {
                            var p = SidePoint(ss, side, 7.4f);
                            if (NearJunction(p) || !Free(p, 1.5f)) break;
                            Signpost(Chunk(ss), p, yaw); Claim(p, 1.5f); done = true; break;
                        }
                    }
                }
                if (done) Count(kind);
            }

            foreach (var (k, mb) in chunks)
            {
                if (mb.V.Count == 0) continue;
                var mesh = SaveMesh(mb.ToMesh($"POI_{e.id}_{k}"), Dir, $"POI_{e.id}_{k}");
                var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.lightProbeUsage = LightProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                go.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.025f, new Renderer[] { mr }) });   // 멀어 작아지면 안 그림
            }
        }
        Debug.Log("[Scrun] 볼거리: " + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));
    }

    static string Pick(string region, bool nearWater)
    {
        float u = R();
        switch (region)
        {
            case "harbor": return u < 0.45f ? "houses" : u < 0.7f ? "stall" : u < 0.9f ? "bunting" : "signpost";
            case "lake": return nearWater && u < 0.4f ? "boathouse" : u < 0.55f ? "cabin" : u < 0.75f ? "bunting" : u < 0.9f ? "camp" : "signpost";
            case "forest": return u < 0.4f ? "cabin" : u < 0.75f ? "camp" : u < 0.9f ? "signpost" : "cairn";
            case "highland": return u < 0.3f ? "farm" : u < 0.55f ? "windmill" : u < 0.85f ? "sheep" : "bunting";
            case "cliff": return nearWater && u < 0.5f ? "viewpoint" : u < 0.75f ? "cairn" : u < 0.9f ? "sheep" : "signpost";
            default: return u < 0.5f ? "signpost" : "camp";
        }
    }

    // ======================= 모형 =======================
    static void Box(StylizedFlora.MB mb, Vector3 c, Vector3 size, Quaternion rot, Color col) => RoadsideBuilder.Box(mb, c, size, rot, col);

    // 맞배지붕(삼각 기둥): 바닥 가운데 c, 폭 w(용마루와 수직), 길이 d(용마루 방향), 높이 h, 처마 o
    static void GableRoof(StylizedFlora.MB mb, Vector3 c, float w, float d, float h, float o, Quaternion rot, Color roof, Color gable)
    {
        Vector3 P(float x, float y, float z) => c + rot * new Vector3(x, y, z);
        float hw = w / 2f + o, hd = d / 2f + o;
        var ridgeA = P(0, h, -hd); var ridgeB = P(0, h, hd);
        var lA = P(-hw, -o * 0.4f, -hd); var lB = P(-hw, -o * 0.4f, hd);
        var rA = P(hw, -o * 0.4f, -hd); var rB = P(hw, -o * 0.4f, hd);
        QuadFace(mb, lA, lB, ridgeB, ridgeA, roof);
        QuadFace(mb, rB, rA, ridgeA, ridgeB, roof);
        // 지붕 아랫면(처마 밑이 비어 보이지 않게)
        QuadFace(mb, lB, lA, rA, rB, roof * 0.6f);
        // 박공(삼각형 벽)
        var gA0 = P(-w / 2f, 0, -d / 2f); var gA1 = P(w / 2f, 0, -d / 2f); var gA2 = P(0, h - o * 0.3f, -d / 2f);
        var gB0 = P(w / 2f, 0, d / 2f); var gB1 = P(-w / 2f, 0, d / 2f); var gB2 = P(0, h - o * 0.3f, d / 2f);
        TriFace(mb, gA0, gA2, gA1, gable);
        TriFace(mb, gB0, gB2, gB1, gable);
    }

    static void QuadFace(StylizedFlora.MB mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        var n = Vector3.Cross(b - a, c - a).normalized;
        col.a = 1f;
        int i0 = mb.Add(a, n, col), i1 = mb.Add(b, n, col), i2 = mb.Add(c, n, col), i3 = mb.Add(d, n, col);
        mb.Tri(i0, i1, i2); mb.Tri(i0, i2, i3);
    }

    static void TriFace(StylizedFlora.MB mb, Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        var n = Vector3.Cross(b - a, c - a).normalized;
        col.a = 1f;
        int i0 = mb.Add(a, n, col), i1 = mb.Add(b, n, col), i2 = mb.Add(c, n, col);
        mb.Tri(i0, i1, i2);
    }

    // 양면 삼각형(깃발)
    static void TwoSidedTri(StylizedFlora.MB mb, Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        TriFace(mb, a, b, c, col);
        TriFace(mb, a, c, b, col * 0.85f);
    }

    static void Cone(StylizedFlora.MB mb, Vector3 b, float r, float h, int seg, Color col)
    {
        var apex = b + Vector3.up * h;
        for (int i = 0; i < seg; i++)
        {
            float a0 = (float)i / seg * Mathf.PI * 2f, a1 = (float)(i + 1) / seg * Mathf.PI * 2f;
            var p0 = b + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r;
            var p1 = b + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r;
            TriFace(mb, p0, apex, p1, col);
        }
    }

    static void Disc(StylizedFlora.MB mb, Vector3 c, Vector3 n, float r, int seg, Color col)
    {
        var side = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        var up2 = Vector3.Cross(n, side);
        int ci = mb.Add(c, n, col);
        int start = mb.V.Count;
        for (int i = 0; i <= seg; i++)
        {
            float a = (float)i / seg * Mathf.PI * 2f;
            mb.Add(c + (side * Mathf.Cos(a) + up2 * Mathf.Sin(a)) * r, n, col);
        }
        for (int i = 0; i < seg; i++)
        {
            int a = start + i, b = start + i + 1;
            if (Vector3.Dot(Vector3.Cross(mb.V[a] - c, mb.V[b] - c), n) > 0) mb.Tri(ci, a, b); else mb.Tri(ci, b, a);
        }
    }

    static void Log(StylizedFlora.MB mb, Vector3 a, Vector3 b, float r, Color bark, Color cut)
    {
        StylizedFlora.Cylinder(mb, a, b, r, r, 7, bark * 0.85f, bark);
        var axis = (b - a).normalized;
        Disc(mb, a, -axis, r, 7, cut);
        Disc(mb, b, axis, r, 7, cut);
    }

    // ---- 지중해식 집: 흰 회벽(또는 크림·분홍), 테라코타 지붕, 파란 덧창, 굴뚝, 화분 ----
    static void House(StylizedFlora.MB mb, Vector3 p, Quaternion rot)
    {
        var walls = new[] { L(0.95f, 0.93f, 0.88f), L(0.96f, 0.88f, 0.72f), L(0.95f, 0.82f, 0.78f), L(0.86f, 0.90f, 0.94f) };
        var wall = walls[(int)(R() * walls.Length) % walls.Length];
        var roof = L(0.78f, 0.38f, 0.22f) * (0.9f + 0.2f * R());
        var shutter = R() < 0.5f ? L(0.20f, 0.42f, 0.68f) : L(0.26f, 0.52f, 0.34f);
        float w = 6.5f + 2f * R(), d = 5.5f + 1.5f * R(), h = R() < 0.4f ? 6.2f : 3.6f;
        Vector3 P(float x, float y, float z) => p + rot * new Vector3(x, y, z);
        Box(mb, P(0, h / 2f - 0.8f, 0), new Vector3(w, h + 1.6f, d), rot, wall);                         // 벽(아래 0.8m 묻힘)
        Box(mb, P(0, 0.15f, 0), new Vector3(w + 0.3f, 0.5f, d + 0.3f), rot, wall * 0.8f);               // 걸레받이
        GableRoof(mb, P(0, h, 0), d, w, 1.8f, 0.35f, rot * Quaternion.Euler(0f, 90f, 0f), roof, wall);
        // 앞면(+z = 길 쪽): 문 + 창문·덧창
        Box(mb, P(0, 1.05f, d / 2f + 0.03f), new Vector3(1.1f, 2.1f, 0.08f), rot, L(0.45f, 0.30f, 0.20f));
        int floors = h > 5f ? 2 : 1;
        for (int f = 0; f < floors; f++)
            foreach (float x in new[] { -w * 0.3f, w * 0.3f })
            {
                if (f == 0 && Mathf.Abs(x) < 1.2f) continue;
                float y = 1.6f + f * 2.8f;
                Box(mb, P(x, y, d / 2f + 0.03f), new Vector3(0.9f, 1.1f, 0.06f), rot, L(0.18f, 0.22f, 0.28f));
                Box(mb, P(x - 0.7f, y, d / 2f + 0.06f), new Vector3(0.45f, 1.2f, 0.06f), rot, shutter);
                Box(mb, P(x + 0.7f, y, d / 2f + 0.06f), new Vector3(0.45f, 1.2f, 0.06f), rot, shutter);
                // 창가 화분(빨간 꽃)
                Box(mb, P(x, y - 0.7f, d / 2f + 0.2f), new Vector3(1.0f, 0.22f, 0.25f), rot, L(0.62f, 0.34f, 0.22f));
                Box(mb, P(x, y - 0.5f, d / 2f + 0.2f), new Vector3(0.9f, 0.2f, 0.2f), rot, L(0.85f, 0.18f, 0.22f));
            }
        Box(mb, P(w * 0.28f, h + 1.4f, -d * 0.15f), new Vector3(0.6f, 1.6f, 0.6f), rot, wall * 0.9f);    // 굴뚝
    }

    // ---- 노점: 기둥 4 + 줄무늬 차양 + 탁자 위 과일 상자 ----
    static void Stall(StylizedFlora.MB mb, Vector3 p, Quaternion rot)
    {
        var stripeA = R() < 0.5f ? L(0.85f, 0.22f, 0.20f) : L(0.20f, 0.45f, 0.80f);
        var white = L(0.95f, 0.94f, 0.90f);
        Vector3 P(float x, float y, float z) => p + rot * new Vector3(x, y, z);
        foreach (var (x, z) in new[] { (-1.4f, -0.9f), (1.4f, -0.9f), (-1.4f, 0.9f), (1.4f, 0.9f) })
            Box(mb, P(x, 1.2f, z), new Vector3(0.09f, 2.4f, 0.09f), rot, L(0.45f, 0.32f, 0.22f));
        for (int i = 0; i < 6; i++)
            Box(mb, P(-1.5f + 0.5f * i + 0.25f, 2.45f + (i % 2) * 0.01f, 0.1f), new Vector3(0.5f, 0.06f, 2.3f), rot * Quaternion.Euler(-12f, 0f, 0f), i % 2 == 0 ? stripeA : white);
        Box(mb, P(0, 0.85f, 0.4f), new Vector3(2.6f, 0.08f, 0.9f), rot, L(0.55f, 0.40f, 0.26f));
        Box(mb, P(0, 0.42f, 0.4f), new Vector3(2.5f, 0.84f, 0.8f), rot, L(0.45f, 0.32f, 0.22f));
        var fruits = new[] { L(0.95f, 0.55f, 0.10f), L(0.85f, 0.15f, 0.12f), L(0.95f, 0.85f, 0.20f), L(0.45f, 0.70f, 0.20f) };
        for (int i = 0; i < 4; i++)
        {
            Box(mb, P(-0.9f + 0.6f * i, 1.0f, 0.4f), new Vector3(0.5f, 0.22f, 0.6f), rot, L(0.62f, 0.46f, 0.30f));
            Box(mb, P(-0.9f + 0.6f * i, 1.14f, 0.4f), new Vector3(0.44f, 0.1f, 0.54f), rot, fruits[i]);
        }
    }

    // ---- 만국기: 길 양쪽 기둥 + 처진 줄 + 색 삼각 깃발 ----
    static void Bunting(StylizedFlora.MB mb, Vector3 a, Vector3 b)
    {
        var pole = L(0.92f, 0.92f, 0.90f);
        float top = 6.2f;
        StylizedFlora.Cylinder(mb, a, a + Vector3.up * top, 0.09f, 0.07f, 6, pole, pole);
        StylizedFlora.Cylinder(mb, b, b + Vector3.up * top, 0.09f, 0.07f, 6, pole, pole);
        var colors = new[] { L(0.90f, 0.20f, 0.18f), L(0.98f, 0.80f, 0.15f), L(0.18f, 0.45f, 0.85f), L(0.25f, 0.70f, 0.35f), L(0.97f, 0.97f, 0.95f), L(0.95f, 0.50f, 0.15f) };
        var A = a + Vector3.up * (top - 0.2f); var B = b + Vector3.up * (top - 0.2f);
        float span = Vector3.Distance(A, B);
        int n = Mathf.FloorToInt(span / 0.55f);
        var along = (B - A).normalized;
        var face = Vector3.Cross(along, Vector3.up).normalized;
        Vector3 Rope(float t) => Vector3.Lerp(A, B, t) + Vector3.down * (1.2f * 4f * t * (1f - t));
        for (int i = 0; i < n; i++)
        {
            float t0 = (i + 0.1f) / n, t1 = (i + 0.9f) / n;
            var p0 = Rope(t0); var p1 = Rope(t1);
            var tip = (p0 + p1) / 2f + Vector3.down * 0.42f + face * 0.02f;
            TwoSidedTri(mb, p0, p1, tip, colors[i % colors.Length]);
        }
        // 줄(가는 상자 이음)
        for (int i = 0; i < 12; i++)
        {
            var p0 = Rope(i / 12f); var p1 = Rope((i + 1) / 12f);
            Box(mb, (p0 + p1) / 2f, new Vector3(0.025f, 0.025f, Vector3.Distance(p0, p1)), Quaternion.LookRotation(p1 - p0), L(0.3f, 0.3f, 0.3f));
        }
    }

    // ---- 보트 창고: 물 위 나무 부두 + 작은 창고 + 보트 ----
    static void Boathouse(StylizedFlora.MB mb, Vector3 shore, Quaternion rot)
    {
        var wood = L(0.52f, 0.36f, 0.22f); var woodD = L(0.38f, 0.26f, 0.16f);
        Vector3 P(float x, float y, float z) => shore + rot * new Vector3(x, y, z);
        // 부두(물 쪽 = +z)
        Box(mb, P(0, 0.45f, 6f), new Vector3(2.4f, 0.15f, 13f), rot, wood);
        for (int i = 0; i < 6; i++)
            foreach (float x in new[] { -1.1f, 1.1f })
                Box(mb, P(x, -0.5f, 0.5f + i * 2.3f), new Vector3(0.18f, 2.0f, 0.18f), rot, woodD);
        // 창고(부두 옆, 물 위)
        Box(mb, P(3.6f, 1.6f, 7f), new Vector3(4f, 2.6f, 5f), rot, L(0.70f, 0.30f, 0.22f));
        Box(mb, P(3.6f, 0.15f, 7f), new Vector3(4.2f, 0.3f, 5.2f), rot, woodD);
        GableRoof(mb, P(3.6f, 2.9f, 7f), 4f, 5f, 1.4f, 0.3f, rot, L(0.30f, 0.30f, 0.32f), L(0.70f, 0.30f, 0.22f));
        Box(mb, P(3.6f, 1.3f, 9.52f), new Vector3(2.2f, 2f, 0.06f), rot, L(0.92f, 0.90f, 0.85f));
        // 작은 보트(부두 반대쪽)
        var hull = L(0.95f, 0.95f, 0.92f);
        Box(mb, P(-2.3f, 0.1f, 9f), new Vector3(1.3f, 0.45f, 3.6f), rot, hull);
        Box(mb, P(-2.3f, 0.28f, 9f), new Vector3(1.05f, 0.12f, 3.3f), rot, L(0.30f, 0.50f, 0.75f));
        // 갈대(물가)
        var reed = L(0.42f, 0.55f, 0.22f); var reedTop = L(0.55f, 0.40f, 0.22f);
        for (int i = 0; i < 40; i++)
        {
            var q = P((R() - 0.5f) * 14f, -0.4f, -1.5f + R() * 3f);
            float hh = 1.2f + R() * 0.8f;
            Box(mb, q + Vector3.up * hh / 2f, new Vector3(0.04f, hh, 0.04f), Quaternion.Euler((R() - 0.5f) * 14f, R() * 180f, (R() - 0.5f) * 14f), reed);
            if (R() < 0.4f) Box(mb, q + Vector3.up * (hh + 0.1f), new Vector3(0.08f, 0.28f, 0.08f), Quaternion.identity, reedTop);
        }
    }

    // ---- 통나무집: 통나무 벽, 초록 지붕, 돌 굴뚝, 현관 ----
    static void Cabin(StylizedFlora.MB mb, Vector3 p, Quaternion rot)
    {
        var bark = L(0.50f, 0.34f, 0.20f); var cut = L(0.78f, 0.62f, 0.40f);
        float w = 6.5f, d = 5f;
        Vector3 P(float x, float y, float z) => p + rot * new Vector3(x, y, z);
        Box(mb, P(0, 0, 0), new Vector3(w + 0.6f, 1.2f, d + 0.6f), rot, L(0.45f, 0.43f, 0.40f));   // 돌 기초
        for (int row = 0; row < 8; row++)
        {
            float y = 0.75f + row * 0.32f;
            Log(mb, P(-w / 2f - 0.35f, y, d / 2f), P(w / 2f + 0.35f, y, d / 2f), 0.17f, bark, cut);
            Log(mb, P(-w / 2f - 0.35f, y, -d / 2f), P(w / 2f + 0.35f, y, -d / 2f), 0.17f, bark, cut);
            Log(mb, P(-w / 2f, y + 0.16f, -d / 2f - 0.35f), P(-w / 2f, y + 0.16f, d / 2f + 0.35f), 0.17f, bark, cut);
            Log(mb, P(w / 2f, y + 0.16f, -d / 2f - 0.35f), P(w / 2f, y + 0.16f, d / 2f + 0.35f), 0.17f, bark, cut);
        }
        Box(mb, P(0, 1.95f, 0), new Vector3(w - 0.2f, 2.6f, d - 0.2f), rot, L(0.40f, 0.27f, 0.16f));   // 안쪽 채움
        GableRoof(mb, P(0, 3.35f, 0), d, w, 2.2f, 0.5f, rot * Quaternion.Euler(0f, 90f, 0f), L(0.24f, 0.40f, 0.26f), bark);
        Box(mb, P(0, 1.6f, d / 2f + 0.2f), new Vector3(1.0f, 1.9f, 0.08f), rot, L(0.32f, 0.20f, 0.12f));     // 문
        Box(mb, P(1.9f, 2.0f, d / 2f + 0.2f), new Vector3(1.0f, 0.8f, 0.06f), rot, L(1.0f, 0.86f, 0.55f));   // 불 켜진 창
        Box(mb, P(-1.9f, 2.0f, d / 2f + 0.2f), new Vector3(1.0f, 0.8f, 0.06f), rot, L(1.0f, 0.86f, 0.55f));
        Box(mb, P(0, 0.75f, d / 2f + 1.1f), new Vector3(w, 0.15f, 1.6f), rot, L(0.55f, 0.40f, 0.26f));      // 현관 바닥
        Box(mb, P(-w * 0.32f, 4.6f, -d * 0.2f), new Vector3(0.8f, 3.2f, 0.8f), rot, L(0.52f, 0.50f, 0.47f)); // 굴뚝
    }

    // ---- 모닥불: 돌 둘레 + 엇갈린 장작 + 불꽃 + 통나무 의자 ----
    static void Campfire(StylizedFlora.MB mb, Vector3 p)
    {
        var stone = L(0.50f, 0.48f, 0.45f);
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * Mathf.PI * 2f;
            StylizedFlora.FacetedBlob(mb, p + new Vector3(Mathf.Cos(a), 0.08f, Mathf.Sin(a)) * 0.75f, new Vector3(0.22f, 0.16f, 0.2f), 0, 0.3f, i, _ => stone * (0.85f + 0.3f * R()));
        }
        var bark = L(0.42f, 0.28f, 0.16f); var cut = L(0.72f, 0.56f, 0.36f);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * Mathf.PI;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f;
            Log(mb, p - d + Vector3.up * 0.1f, p + d + Vector3.up * 0.25f, 0.06f, bark, cut);
        }
        Cone(mb, p + Vector3.up * 0.15f, 0.35f, 0.9f, 6, L(1.0f, 0.55f, 0.12f));
        Cone(mb, p + Vector3.up * 0.18f, 0.2f, 0.65f, 5, L(1.0f, 0.88f, 0.35f));
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * Mathf.PI * 2f + 0.4f;
            var c = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.1f;
            var t = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.9f;
            Log(mb, c - t + Vector3.up * 0.22f, c + t + Vector3.up * 0.22f, 0.22f, bark, cut);
        }
    }

    // ---- 장작더미 ----
    static void LogPile(StylizedFlora.MB mb, Vector3 p, Quaternion rot)
    {
        var bark = L(0.45f, 0.30f, 0.18f); var cut = L(0.80f, 0.64f, 0.42f);
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 4 - row; i++)
            {
                var c = p + rot * new Vector3(-0.9f + i * 0.6f + row * 0.3f, 0.28f + row * 0.5f, 0f);
                Log(mb, c + rot * Vector3.back * 1.4f, c + rot * Vector3.forward * 1.4f, 0.27f, bark, cut);
            }
    }

    // ---- 농가: 빨간 헛간(흰 테두리, 회색 지붕) ----
    static void Barn(StylizedFlora.MB mb, Vector3 p, Quaternion rot)
    {
        var red = L(0.66f, 0.17f, 0.12f); var white = L(0.95f, 0.94f, 0.90f);
        float w = 9f, d = 12f, h = 5f;
        Vector3 P(float x, float y, float z) => p + rot * new Vector3(x, y, z);
        Box(mb, P(0, h / 2f - 0.8f, 0), new Vector3(w, h + 1.6f, d), rot, red);
        GableRoof(mb, P(0, h, 0), w, d, 3.6f, 0.45f, rot, L(0.36f, 0.36f, 0.38f), red);
        // 앞면(+z): 큰 문 + X 무늬 흰 테두리
        Box(mb, P(0, 1.9f, d / 2f + 0.04f), new Vector3(3.6f, 3.8f, 0.06f), rot, L(0.55f, 0.13f, 0.10f));
        Box(mb, P(0, 3.85f, d / 2f + 0.08f), new Vector3(3.9f, 0.18f, 0.06f), rot, white);
        Box(mb, P(-1.9f, 1.9f, d / 2f + 0.08f), new Vector3(0.18f, 3.9f, 0.06f), rot, white);
        Box(mb, P(1.9f, 1.9f, d / 2f + 0.08f), new Vector3(0.18f, 3.9f, 0.06f), rot, white);
        Box(mb, P(0, 1.9f, d / 2f + 0.1f), new Vector3(0.16f, 5.0f, 0.06f), rot * Quaternion.Euler(0f, 0f, 43f), white);
        Box(mb, P(0, 1.9f, d / 2f + 0.1f), new Vector3(0.16f, 5.0f, 0.06f), rot * Quaternion.Euler(0f, 0f, -43f), white);
        Box(mb, P(0, h + 1.6f, d / 2f + 0.06f), new Vector3(1.2f, 1.2f, 0.06f), rot, white);   // 다락 창
        // 울타리 마당
        for (int i = 0; i < 6; i++)
        {
            var a = P(-w / 2f - 6f + i * 2.4f, 0.55f, d / 2f + 5f);
            Box(mb, a, new Vector3(0.12f, 1.1f, 0.12f), rot, white);
            if (i < 5) Box(mb, a + rot * new Vector3(1.2f, 0.2f, 0f), new Vector3(2.4f, 0.12f, 0.06f), rot, white);
        }
    }

    static void Silo(StylizedFlora.MB mb, Vector3 p)
    {
        var c = L(0.80f, 0.80f, 0.78f);
        StylizedFlora.Cylinder(mb, p + Vector3.down * 0.5f, p + Vector3.up * 11f, 2f, 2f, 12, c * 0.9f, c);
        for (int i = 0; i < 5; i++)
            StylizedFlora.Cylinder(mb, p + Vector3.up * (1.5f + i * 2f), p + Vector3.up * (1.62f + i * 2f), 2.04f, 2.04f, 12, c * 0.7f, c * 0.7f);
        StylizedFlora.Blob(mb, p + Vector3.up * 11f, new Vector3(2f, 1.3f, 2f), 1, 0f, 0f, _ => L(0.42f, 0.44f, 0.48f));
    }

    // ---- 풍차(유럽식): 팔각 탑 + 원뿔 지붕 + 날개 4장(따로 돌아감) ----
    internal static void Windmill(StylizedFlora.MB mb, Transform root, Vector3 p, Quaternion rot, int index)
    {
        var tower = L(0.93f, 0.90f, 0.82f); var cap = L(0.45f, 0.30f, 0.20f);
        StylizedFlora.Cylinder(mb, p + Vector3.down * 0.5f, p + Vector3.up * 10f, 3.2f, 2.2f, 8, tower * 0.92f, tower);
        Cone(mb, p + Vector3.up * 10f, 2.7f, 3f, 8, cap);
        Box(mb, p + rot * new Vector3(0, 1.1f, 3.05f), new Vector3(1.2f, 2.2f, 0.3f), rot, L(0.40f, 0.26f, 0.16f));
        Box(mb, p + rot * new Vector3(0, 6f, 2.65f), new Vector3(0.9f, 1.1f, 0.2f), rot, L(0.25f, 0.35f, 0.5f));

        // 날개: 별도 메쉬 + 회전(Rotator)
        var sails = new StylizedFlora.MB();
        var wood = L(0.40f, 0.28f, 0.18f); var cloth = L(0.96f, 0.94f, 0.88f);
        StylizedFlora.Cylinder(sails, Vector3.back * 0.6f, Vector3.forward * 0.6f, 0.35f, 0.35f, 8, wood, wood);
        for (int b = 0; b < 4; b++)
        {
            var q = Quaternion.Euler(0f, 0f, b * 90f);
            Box(sails, q * new Vector3(0, 4.5f, 0.55f), new Vector3(0.22f, 9f, 0.15f), q, wood);
            Box(sails, q * new Vector3(0.75f, 5.3f, 0.6f), new Vector3(1.3f, 6.6f, 0.05f), q, cloth);
            for (int k = 0; k < 6; k++)
                Box(sails, q * new Vector3(0.75f, 2.3f + k * 1.2f, 0.66f), new Vector3(1.4f, 0.07f, 0.05f), q, wood);
        }
        var mesh = SaveMesh(sails.ToMesh($"WindmillSails_{index}"), Dir, $"WindmillSails_{index}");
        var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(p + Vector3.up * 9.6f + rot * Vector3.forward * 2.9f, rot);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = VegetationBuilder.StyMat("Sty_Prop", 0f, 1f, false);
        go.AddComponent<Rotator>().Set(Vector3.forward, 22f + index * 3f);
    }

    // ---- 양: 복슬복슬한 몸 + 검은 얼굴·다리 ----
    static void Sheep(StylizedFlora.MB mb, Vector3 p, Quaternion rot)
    {
        var wool = L(0.95f, 0.94f, 0.90f); var dark = L(0.18f, 0.16f, 0.15f);
        var body = p + Vector3.up * 0.75f;
        StylizedFlora.Blob(mb, body, new Vector3(0.45f, 0.42f, 0.7f), 1, 0.35f, R() * 10f, d => wool * (0.85f + 0.15f * (d.y * 0.5f + 0.5f)));
        var head = body + rot * new Vector3(0f, 0.25f, 0.75f);
        StylizedFlora.Blob(mb, head, new Vector3(0.2f, 0.24f, 0.28f), 0, 0.1f, 1f, _ => dark);
        StylizedFlora.Blob(mb, head + rot * new Vector3(0, 0.18f, -0.1f), new Vector3(0.25f, 0.14f, 0.2f), 0, 0.3f, 2f, _ => wool);
        foreach (var (x, z) in new[] { (-0.22f, 0.4f), (0.22f, 0.4f), (-0.22f, -0.4f), (0.22f, -0.4f) })
        {
            var f = p + rot * new Vector3(x, 0f, z);
            StylizedFlora.Cylinder(mb, f, f + Vector3.up * 0.5f, 0.06f, 0.06f, 4, dark, dark);
        }
    }

    // ---- 전망대: 나무 데크 + 난간 + 벤치 + 망원경 + 깃대 ----
    static void Viewpoint(StylizedFlora.MB mb, Vector3 p, Quaternion rot)
    {
        var wood = L(0.55f, 0.40f, 0.26f); var dark = L(0.35f, 0.25f, 0.16f);
        Vector3 P(float x, float y, float z) => p + rot * new Vector3(x, y, z);
        Box(mb, P(0, 0.25f, 2f), new Vector3(6f, 0.2f, 4f), rot, wood);
        for (int i = 0; i < 4; i++) Box(mb, P(-2.7f + i * 1.8f, -0.8f, 3.8f), new Vector3(0.2f, 2.2f, 0.2f), rot, dark);
        // 난간(바다 쪽 = +z)
        for (int i = 0; i <= 6; i++) Box(mb, P(-3f + i, 0.85f, 3.9f), new Vector3(0.1f, 1.1f, 0.1f), rot, dark);
        Box(mb, P(0, 1.4f, 3.9f), new Vector3(6.1f, 0.12f, 0.14f), rot, wood);
        Box(mb, P(0, 0.9f, 3.9f), new Vector3(6.1f, 0.08f, 0.08f), rot, wood);
        // 벤치
        Box(mb, P(-1.5f, 0.75f, 1.2f), new Vector3(1.8f, 0.1f, 0.45f), rot, wood);
        Box(mb, P(-1.5f, 1.1f, 1.0f), new Vector3(1.8f, 0.5f, 0.08f), rot, wood);
        Box(mb, P(-2.25f, 0.5f, 1.2f), new Vector3(0.1f, 0.5f, 0.4f), rot, dark);
        Box(mb, P(-0.75f, 0.5f, 1.2f), new Vector3(0.1f, 0.5f, 0.4f), rot, dark);
        // 망원경
        var metal = L(0.25f, 0.45f, 0.55f);
        StylizedFlora.Cylinder(mb, P(1.6f, 0.35f, 3f), P(1.6f, 1.25f, 3f), 0.06f, 0.06f, 6, metal, metal);
        StylizedFlora.Cylinder(mb, P(1.6f, 1.3f, 2.75f), P(1.6f, 1.45f, 3.35f), 0.12f, 0.09f, 8, metal, metal);
        // 깃대 + 깃발
        var pole = P(2.7f, 0.35f, 0.4f);
        StylizedFlora.Cylinder(mb, pole, pole + Vector3.up * 6f, 0.06f, 0.05f, 6, L(0.9f, 0.9f, 0.9f), L(0.9f, 0.9f, 0.9f));
        var f0 = pole + Vector3.up * 5.9f; var f1 = pole + Vector3.up * 5.0f;
        var fl = rot * Vector3.right * 1.4f;
        TwoSidedTri(mb, f0, f1, f0 + fl, L(0.15f, 0.45f, 0.85f));
        TwoSidedTri(mb, f1, f1 + fl, f0 + fl, L(0.15f, 0.45f, 0.85f));
    }

    // ---- 돌탑 ----
    static void Cairn(StylizedFlora.MB mb, Vector3 p)
    {
        float y = 0f;
        for (int i = 0; i < 6; i++)
        {
            float r = Mathf.Lerp(0.55f, 0.18f, i / 5f) * (0.85f + 0.3f * R());
            float h = r * 0.55f;
            StylizedFlora.FacetedBlob(mb, p + new Vector3((R() - 0.5f) * 0.1f, y + h * 0.5f, (R() - 0.5f) * 0.1f), new Vector3(r, h, r * 0.9f), 0, 0.2f, i * 3.3f,
                _ => L(0.62f, 0.60f, 0.56f) * (0.8f + 0.35f * R()));
            y += h * 0.85f;
        }
    }

    // ---- 이정표: 기둥 + 방향 판 2~3개 ----
    static void Signpost(StylizedFlora.MB mb, Vector3 p, Quaternion rot)
    {
        var wood = L(0.50f, 0.36f, 0.22f);
        StylizedFlora.Cylinder(mb, p + Vector3.down * 0.2f, p + Vector3.up * 2.6f, 0.08f, 0.07f, 6, wood, wood);
        var colors = new[] { L(0.95f, 0.75f, 0.20f), L(0.25f, 0.55f, 0.85f), L(0.90f, 0.35f, 0.25f) };
        int n = 2 + (int)(R() * 1.99f);
        for (int i = 0; i < n; i++)
        {
            float yaw = (i % 2 == 0 ? 1 : -1) * (70f + R() * 30f);
            var q = rot * Quaternion.Euler(0f, yaw, 0f);
            var c = p + Vector3.up * (2.3f - i * 0.4f) + q * Vector3.forward * 0.55f;
            Box(mb, c, new Vector3(0.06f, 0.28f, 1.1f), q, colors[i % colors.Length]);
            TriFace(mb, c + q * new Vector3(0.035f, 0.14f, 0.55f), c + q * new Vector3(0.035f, -0.14f, 0.55f), c + q * new Vector3(0.035f, 0f, 0.8f), colors[i % colors.Length]);
            TriFace(mb, c + q * new Vector3(-0.035f, 0.14f, 0.55f), c + q * new Vector3(-0.035f, 0f, 0.8f), c + q * new Vector3(-0.035f, -0.14f, 0.55f), colors[i % colors.Length]);
        }
    }
}
