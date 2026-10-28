using System.Collections.Generic;
using System.Linq;
using Scrun;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ScrunEditorUtil;

// 연출 장면(IslandLayout.json "features"): 규칙대로 흩뿌린 볼거리와 달리, 정해진 자리에 기억에 남는 장면을 만든다.
//  gorge     = 높은 바위벽 사이로 지나는 협곡 길(양옆 9.5~13m에 바위벽 + 그 뒤 지형 둑)
//  tunnel    = 곶을 뚫은 바위 터널(둔덕 + 아치 껍데기 + 입구 바위 + 안쪽 등)
//  boardwalk = 호숫가 나무 데크 길(노면을 판자로, 양옆 난간·가로등)
//  windmills = 능선 위 풍차 줄(길에서 50~90m, 높은 쪽)
// 지형에 영향을 주는 것(둑·둔덕·식생 제외)은 지형 메쉬를 만들기 전에 PrepareTerrain에서 넣는다.
public static class SetPieceBuilder
{
    const string Dir = CourseSceneBuilder.GenDir + "/Island/SetPieces";

    public class Feature
    {
        public string type, edge;
        public float s0, s1;      // 엣지 위 구간(m)
        public float height;      // gorge 바위벽 높이
        public int count;         // windmills 개수
    }

    static System.Random rng;
    static float R() => (float)rng.NextDouble();

    public static List<Feature> Parse(Dictionary<string, object> layout, RoadNetwork net, float k)
    {
        var list = new List<Feature>();
        if (!layout.TryGetValue("features", out var fo)) return list;
        foreach (Dictionary<string, object> f in (List<object>)fo)
        {
            var e = new Feature { type = (string)f["type"], edge = (string)f["edge"] };
            var line = net.EdgeLine(e.edge);
            if (f.TryGetValue("at", out var at))
            {
                var a = (List<object>)at;
                var target = new Vector2((float)(double)a[0] * k, (float)(double)a[1] * k);
                float best = float.MaxValue, bestD = 0f;
                for (float d = 0; d <= line.Length; d += 2f)
                {
                    var p = line.GetPoint(d);
                    float sq = (new Vector2(p.x, p.z) - target).sqrMagnitude;
                    if (sq < best) { best = sq; bestD = d; }
                }
                float half = (f.TryGetValue("length", out var lo) ? (float)(double)lo : 60f) / 2f;
                e.s0 = Mathf.Max(40f, bestD - half); e.s1 = Mathf.Min(line.Length - 40f, bestD + half);
            }
            else
            {
                e.s0 = (float)(double)f["from"] * line.Length;
                e.s1 = (float)(double)f["to"] * line.Length;
            }
            e.height = f.TryGetValue("height", out var h) ? (float)(double)h : 18f;
            e.count = f.TryGetValue("count", out var c) ? (int)(double)c : 3;
            list.Add(e);
        }
        return list;
    }

    // 나무 데크 구간(도로 노면을 판자로 바꿀 곳)
    public static IEnumerable<(float s0, float s1)> Boardwalks(List<Feature> features, string edge) =>
        features.Where(f => f.type == "boardwalk" && f.edge == edge).Select(f => (f.s0, f.s1));

    // ---------------- 지형 준비(메쉬 만들기 전) ----------------
    public static void PrepareTerrain(List<Feature> features, RoadNetwork net, CourseTerrainBuilder terrain)
    {
        foreach (var f in features)
        {
            var line = net.EdgeLine(f.edge);
            switch (f.type)
            {
                case "gorge":
                {
                    // 협곡 길: 둑을 높게(바위벽 뒤로 지형이 솟아 하늘이 좁아 보이게) + 바위벽 자리엔 식생 없음
                    var pts = new List<Vector2>();
                    for (float s = f.s0; s <= f.s1; s += 10f) { var p = line.GetPoint(s); pts.Add(new Vector2(p.x, p.z)); }
                    var baseBank = terrain.BankAmount;
                    float hgt = f.height;
                    terrain.BankAmount = (x, z) =>
                    {
                        float b = baseBank != null ? baseBank(x, z) : 0f;
                        var q = new Vector2(x, z);
                        float best = float.MaxValue;
                        foreach (var p in pts) best = Mathf.Min(best, (p - q).sqrMagnitude);
                        float g = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(70f, 160f, Mathf.Sqrt(best)));
                        return Mathf.Max(b, hgt * 1.6f * g);
                    };
                    for (float s = f.s0; s <= f.s1; s += 6f)
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var p = line.GetPoint(s) + line.GetRight(s) * (side * 11.5f);
                            terrain.Exclusions.Add((new Vector2(p.x, p.z), 4.5f));
                        }
                    break;
                }
                case "tunnel":
                {
                    var mid = line.GetPoint((f.s0 + f.s1) / 2f);
                    float len = f.s1 - f.s0;
                    terrain.Mounds.Add((new Vector2(mid.x, mid.z), len * 0.5f + 55f, 26f));
                    for (float s = f.s0 - 10f; s <= f.s1 + 10f; s += 5f)
                    {
                        var p = line.GetPoint(s);
                        terrain.Exclusions.Add((new Vector2(p.x, p.z), 12f));
                    }
                    break;
                }
                case "boardwalk":
                    for (float s = f.s0; s <= f.s1; s += 4f)
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var p = line.GetPoint(s) + line.GetRight(s) * (side * 5.2f);
                            terrain.Exclusions.Add((new Vector2(p.x, p.z), 1.6f));
                        }
                    break;
            }
        }
    }

    // ---------------- 장면 만들기 ----------------
    public static void Build(List<Feature> features, RoadNetwork net, CourseTerrainBuilder terrain, System.Func<Vector3, string> regionAt, Transform parent)
    {
        EnsureFolderPath(Dir);
        rng = new System.Random(1027);
        var root = new GameObject("SetPieces").transform;
        root.SetParent(parent, false);
        var prop = VegetationBuilder.StyMat("Sty_Prop", 0f, 1f, false);
        var rock = VegetationBuilder.StyMat("Sty_RockWall", 0f, 1f, false);
        int n = 0, windmill = 10;
        foreach (var f in features)
        {
            var line = net.EdgeLine(f.edge);
            var mbProp = new StylizedFlora.MB();
            var mbRock = new StylizedFlora.MB();
            switch (f.type)
            {
                case "gorge": Gorge(mbRock, line, f, terrain, regionAt); break;
                case "tunnel": Tunnel(mbRock, mbProp, line, f, terrain, regionAt); break;
                case "boardwalk": Boardwalk(mbProp, line, f); break;
                case "windmills":
                    for (int i = 0; i < f.count; i++)
                    {
                        float s = Mathf.Lerp(f.s0, f.s1, (i + 0.5f) / f.count) + (R() - 0.5f) * 40f;
                        // 높은 쪽(언덕 위)으로
                        var a = line.GetPoint(s) + line.GetRight(s) * 70f; var b = line.GetPoint(s) - line.GetRight(s) * 70f;
                        int side = terrain.Height(a.x, a.z) >= terrain.Height(b.x, b.z) ? 1 : -1;
                        for (int tries = 0; tries < 8; tries++)
                        {
                            float off = 50f + R() * 40f;
                            var p = line.GetPoint(s) + line.GetRight(s) * (side * off);
                            p.y = terrain.Height(p.x, p.z);
                            if (1f - terrain.NormalAt(p.x, p.z).y > 0.18f || terrain.Excluded(p.x, p.z)) { s += 15f; continue; }
                            var rot = Quaternion.LookRotation(-line.GetRight(s) * side) * Quaternion.Euler(0f, 30f, 0f);
                            SceneryBuilder.Windmill(mbProp, root, p, rot, windmill++);
                            terrain.Exclusions.Add((new Vector2(p.x, p.z), 7f));
                            break;
                        }
                    }
                    break;
            }
            foreach (var (mb, mat, tag) in new[] { (mbProp, prop, "Prop"), (mbRock, rock, "Rock") })
            {
                if (mb.V.Count == 0) continue;
                string name = $"SetPiece_{f.type}_{f.edge}_{tag}";
                var mesh = SaveMesh(mb.ToMesh(name), Dir, name);
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.lightProbeUsage = LightProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
            n++;
        }
        Debug.Log($"[Scrun] 연출 장면 {n}개: " + string.Join(", ", features.Select(f => $"{f.type}({f.edge} {f.s0:0}~{f.s1:0}m)")));
    }

    // 협곡 길: 양옆 11~14m에 얇고 곧게 선 바위 판을 겹쳐 세워 벽처럼(위는 하늘이 보이게 길 쪽으로 기울지 않음).
    // 양 끝은 낮게 시작해 가운데로 갈수록 높아짐. 바위 판 = 세로로 긴 각진 덩어리(두께 3~5m, 높이 tall, 길이 5~9m)
    static readonly Color SlabLight = new(0.30f, 0.26f, 0.22f), SlabDark = new(0.15f, 0.13f, 0.115f);

    static void Gorge(StylizedFlora.MB mb, Polyline line, Feature f, CourseTerrainBuilder terrain, System.Func<Vector3, string> regionAt)
    {
        for (int side = -1; side <= 1; side += 2)
            for (float s = f.s0; s <= f.s1; s += 3.5f + R() * 2.5f)
            {
                float t = Mathf.InverseLerp(f.s0, f.s1, s);
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) * 4f);
                float tall = Mathf.Max(4f, f.height * ramp * (0.7f + 0.45f * R()));
                var road = line.GetPoint(s);
                var outward = line.GetRight(s) * side;
                float depth = 3f + R() * 2f, len = 5f + R() * 4f;
                // 길 쪽 면이 11~14m에 오게(가운데는 그보다 depth/2 바깥)
                var p = road + outward * (11f + R() * 3f + depth * 0.5f);
                p.y = road.y;
                var pal = CourseTerrainBuilder.PaletteFor(regionAt(p));
                var moss = pal.Dark.linear;
                var rot = Quaternion.LookRotation(line.GetForward(s)) * Quaternion.Euler((R() - 0.5f) * 6f, (R() - 0.5f) * 14f, side * (4f + R() * 6f));
                float seed = R() * 100f, bandOff = R() * 10f;
                int start = mb.V.Count;
                StylizedFlora.FacetedBlob(mb, Vector3.zero, new Vector3(depth * 0.5f, tall * 0.5f, len * 0.5f), 1, 0.22f, seed, local =>
                {
                    float band = Mathf.PerlinNoise((local.y + tall * 0.5f) * 0.45f + bandOff, 0.3f);
                    var c = Color.Lerp(SlabDark, SlabLight, 0.3f + 0.6f * band);
                    if (local.y > tall * 0.38f && local.normalized.y > 0.6f) c = Color.Lerp(c, moss, 0.75f);
                    c.a = 1f;
                    return c;
                });
                var center = p + Vector3.up * (tall * 0.5f - 1.2f);
                for (int i = start; i < mb.V.Count; i++) { mb.V[i] = center + rot * mb.V[i]; mb.N[i] = rot * mb.N[i]; }
                // 발치에 굴러떨어진 작은 바위
                if (R() < 0.35f)
                    RoadsideBuilder.OneRock(mb, road + outward * (9f + R() * 1.5f), line.GetForward(s), 1.2f + R() * 1.5f, pal.Dark, R, 1);
            }
    }

    // 바위 터널: 반타원 아치 껍데기(안쪽 면 + 바깥 면 + 입구 테두리), 입구 둘레 바위, 안쪽 벽 등
    static void Tunnel(StylizedFlora.MB rockMb, StylizedFlora.MB propMb, Polyline line, Feature f, CourseTerrainBuilder terrain, System.Func<Vector3, string> regionAt)
    {
        const int arcSeg = 14;
        const float inW = 6.9f, inH = 7.6f, thick = 2.6f;
        var rockA = new Color(0.30f, 0.26f, 0.22f); var rockB = new Color(0.20f, 0.17f, 0.15f);
        int rows = Mathf.CeilToInt((f.s1 - f.s0) / 1.5f) + 1;
        Vector3 Ring(float s, float a, bool outer)
        {
            var c = line.GetPoint(s); var r = line.GetRight(s);
            float w = outer ? inW + thick : inW, h = outer ? inH + thick : inH;
            float bump = outer ? 0.8f * (Mathf.PerlinNoise(s * 0.15f, a * 3f) - 0.5f) : 0.25f * (Mathf.PerlinNoise(s * 0.4f, a * 5f) - 0.5f);
            return c + r * (Mathf.Cos(a) * (w + bump)) + Vector3.up * (Mathf.Sin(a) * (h + bump) - 0.3f);
        }
        foreach (bool outer in new[] { false, true })
        {
            int start = rockMb.V.Count;
            for (int i = 0; i < rows; i++)
            {
                float s = Mathf.Lerp(f.s0, f.s1, (float)i / (rows - 1));
                var c = line.GetPoint(s);
                for (int j = 0; j <= arcSeg; j++)
                {
                    float a = Mathf.PI * j / arcSeg;
                    var p = Ring(s, a, outer);
                    var nrm = (p - (c + Vector3.up * 1.5f)).normalized * (outer ? 1f : -1f);
                    var col = Color.Lerp(rockB, rockA, Mathf.PerlinNoise(s * 0.2f, j * 0.7f));
                    rockMb.Add(p, nrm, col);
                }
            }
            for (int i = 0; i < rows - 1; i++)
                for (int j = 0; j < arcSeg; j++)
                {
                    int a0 = start + i * (arcSeg + 1) + j, a1 = a0 + 1, b0 = a0 + arcSeg + 1, b1 = b0 + 1;
                    // 바깥 면은 바깥, 안쪽 면은 길 쪽이 앞면
                    var pa = rockMb.V[a0]; var pb = rockMb.V[b0]; var pc = rockMb.V[a1];
                    bool flip = Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), rockMb.N[a0]) < 0;
                    if (!flip) { rockMb.Tri(a0, b0, a1); rockMb.Tri(a1, b0, b1); }
                    else { rockMb.Tri(a0, a1, b0); rockMb.Tri(a1, b1, b0); }
                }
        }
        // 입구 테두리(안쪽 ↔ 바깥 사이)
        foreach (float s in new[] { f.s0, f.s1 })
        {
            var fwd = line.GetForward(s) * (s == f.s0 ? -1f : 1f);
            for (int j = 0; j < arcSeg; j++)
            {
                float a0 = Mathf.PI * j / arcSeg, a1 = Mathf.PI * (j + 1) / arcSeg;
                var p0 = Ring(s, a0, false); var p1 = Ring(s, a1, false); var q0 = Ring(s, a0, true); var q1 = Ring(s, a1, true);
                var col = rockA;
                int i0 = rockMb.Add(p0, fwd, col), i1 = rockMb.Add(p1, fwd, col), i2 = rockMb.Add(q1, fwd, col), i3 = rockMb.Add(q0, fwd, col);
                if (Vector3.Dot(Vector3.Cross(p1 - p0, q1 - p0), fwd) > 0) { rockMb.Tri(i0, i1, i2); rockMb.Tri(i0, i2, i3); }
                else { rockMb.Tri(i0, i2, i1); rockMb.Tri(i0, i3, i2); }
            }
            // 입구 둘레 큰 바위(아치를 산에 박힌 것처럼)
            var pal = CourseTerrainBuilder.PaletteFor(regionAt(line.GetPoint(s)));
            for (int k = 0; k < 7; k++)
            {
                float a = Mathf.PI * (k + 0.5f) / 7f;
                var p = Ring(s, a, true) + fwd * (R() * 2f) + (Ring(s, a, true) - line.GetPoint(s)).normalized * 2f;
                RoadsideBuilder.OneRock(rockMb, p - Vector3.up * 2f, line.GetForward(s), 6f + R() * 5f, pal.Dark, R, 1);
            }
        }
        // 터널 옆·위를 바위로 채워 언덕에 박힌 터널처럼(둔덕은 길에서 20m 밖부터 솟으므로 그 사이를 메움)
        var palMid = CourseTerrainBuilder.PaletteFor(regionAt(line.GetPoint((f.s0 + f.s1) / 2f)));
        for (float s = f.s0 - 6f; s <= f.s1 + 6f; s += 5f + R() * 3f)
            for (int side = -1; side <= 1; side += 2)
            {
                var road = line.GetPoint(s);
                var p = road + line.GetRight(s) * (side * (11f + R() * 8f));
                p.y = road.y - 0.5f;
                RoadsideBuilder.OneRock(rockMb, p, line.GetForward(s), 9f + R() * 7f, palMid.Dark, R, 1);
            }
        for (float s = f.s0 + 4f; s <= f.s1 - 4f; s += 7f + R() * 4f)
        {
            var road = line.GetPoint(s);
            var p = road + line.GetRight(s) * ((R() - 0.5f) * 8f) + Vector3.up * (inH + thick - 1.5f);
            RoadsideBuilder.OneRock(rockMb, p, line.GetForward(s), 4f + R() * 4f, palMid.Dark, R, 1);
        }

        // 안쪽 벽 등(따뜻한 빛, 정점 색을 1 넘게 → 블룸)
        var lamp = new Color(2.2f, 1.7f, 1.0f);
        for (float s = f.s0 + 6f; s < f.s1 - 3f; s += 10f)
            for (int side = -1; side <= 1; side += 2)
            {
                var p = line.GetPoint(s) + line.GetRight(s) * (side * (inW - 0.6f)) + Vector3.up * 4.2f;
                RoadsideBuilder.Box(propMb, p, new Vector3(0.25f, 0.5f, 0.6f), Quaternion.LookRotation(line.GetForward(s)), lamp);
            }
    }

    // 호숫가 데크: 양옆 난간(기둥 2m 간격 + 위·가운데 가로대) + 20m마다 가로등
    static void Boardwalk(StylizedFlora.MB mb, Polyline line, Feature f)
    {
        var wood = new Color(0.50f, 0.34f, 0.20f).linear; var woodL = new Color(0.66f, 0.48f, 0.30f).linear;
        const float off = CourseRoadBuilder.RoadWidth / 2f + 0.25f;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3? prev = null;
            for (float s = f.s0; s <= f.s1; s += 2f)
            {
                var p = line.GetPoint(s) + line.GetRight(s) * (side * off);
                RoadsideBuilder.Box(mb, p + Vector3.up * 0.55f, new Vector3(0.14f, 1.1f, 0.14f), Quaternion.LookRotation(line.GetForward(s)), wood);
                if (prev.HasValue)
                {
                    var a = prev.Value; var dir = p - a; var mid = (a + p) / 2f;
                    var rot = Quaternion.LookRotation(dir.normalized);
                    RoadsideBuilder.Box(mb, mid + Vector3.up * 1.08f, new Vector3(0.16f, 0.08f, dir.magnitude), rot, woodL);
                    RoadsideBuilder.Box(mb, mid + Vector3.up * 0.6f, new Vector3(0.06f, 0.1f, dir.magnitude), rot, woodL);
                }
                prev = p;
                if (Mathf.Repeat(s - f.s0, 20f) < 2f)
                    RoadsideBuilder.LampPost(mb, line.GetPoint(s) + line.GetRight(s) * (side * (off + 0.35f)), -line.GetRight(s) * side);
            }
        }
    }
}
