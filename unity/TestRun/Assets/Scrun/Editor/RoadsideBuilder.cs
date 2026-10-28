using System.Collections.Generic;
using System.Linq;
using Scrun;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ScrunEditorUtil;

// 도로변 꾸미기: 일정한 박자(약 200m마다)로 볼거리를 둔다. 지역마다 다른 것:
//  항구 = 가로등, 호수 = 나무 울타리, 숲 = 이끼 낀 돌담, 고원 = 마른 돌담·건초 더미, 절벽 = 육지 쪽 돌담
//  + 급커브 화살표 표지, 지역마다 구간 아치(배너) 하나
// 모두 정점 색 하나의 재질(Sty_Prop)로 300m 덩어리 메쉬에 합쳐 그리기 호출을 줄인다.
public static class RoadsideBuilder
{
    const string Dir = CourseSceneBuilder.GenDir + "/Island/Roadside";
    const float ChunkLen = 300f, SmallChunkLen = 120f;

    // 정점 색은 선형 값 → 눈으로 고른 색(sRGB)을 .linear로 변환(안 하면 하얗게 바래 보임)
    static readonly Color Stone = new Color(0.62f, 0.60f, 0.56f).linear, StoneDark = new Color(0.46f, 0.45f, 0.42f).linear, Moss = new Color(0.38f, 0.50f, 0.24f).linear;
    static readonly Color Wood = new Color(0.50f, 0.34f, 0.20f).linear, WoodLight = new Color(0.66f, 0.48f, 0.30f).linear;
    static readonly Color Metal = new Color(0.18f, 0.19f, 0.21f).linear, Lamp = new(1.8f, 1.55f, 1.05f);
    static readonly Color Hay = new Color(0.86f, 0.72f, 0.38f).linear;

    static System.Random rng;
    static float R() => (float)rng.NextDouble();

    public static void Build(RoadNetwork net, CourseTerrainBuilder terrain, System.Func<Vector3, string> regionAt, Transform parent)
    {
        EnsureFolderPath(Dir);
        rng = new System.Random(77);
        var mat = VegetationBuilder.StyMat("Sty_Prop", 0f, 1f, false);
        mat.SetColor("_ShadeColor", new Color(0.74f, 0.75f, 0.84f));   // 흰 벽 그늘이 보랏빛으로 물들지 않게(기본보다 덜 푸르게)
        // 바위벽: 정점 색 + 높이 방향 지층 띠
        var rockMat = VegetationBuilder.StyMat("Sty_RockWall", 0f, 1f, false);
        rockMat.SetTexture("_DetailMap", CourseSceneBuilder.DetailNoise());
        rockMat.SetFloat("_StrataStrength", 0.3f);
        rockMat.SetFloat("_StrataScale", 0.45f);
        rockMat.SetFloat("_Wrap", 0.35f);
        NatureTextures.ApplyRock(rockMat, 0.12f, 0.45f);   // 사진 바위 결(큰 벽이라 크게)
        var root = new GameObject("Roadside").transform;
        root.SetParent(parent, false);
        var junctions = net.nodes.Select(n => n.position).ToList();
        var archDone = new HashSet<string>();
        int pieces = 0;

        foreach (var e in net.edges)
        {
            var line = net.EdgeLine(e.id);
            var chunks = new Dictionary<int, StylizedFlora.MB>();
            var rockChunks = new Dictionary<int, StylizedFlora.MB>();
            StylizedFlora.MB Chunk(float s)
            {
                int k = Mathf.FloorToInt(s / SmallChunkLen);
                if (!chunks.TryGetValue(k, out var mb)) chunks[k] = mb = new StylizedFlora.MB();
                return mb;
            }
            StylizedFlora.MB RockChunk(float s)
            {
                int k = Mathf.FloorToInt(s / ChunkLen);
                if (!rockChunks.TryGetValue(k, out var mb)) rockChunks[k] = mb = new StylizedFlora.MB();
                return mb;
            }
            bool Clear(float s)
            {
                var p = line.GetPoint(s);
                if (junctions.Any(j => (j - p).sqrMagnitude < 32f * 32f)) return false;
                if (terrain.Height(p.x, p.z) < p.y - 1f) return false;   // 다리 위
                if (terrain.Excluded(p.x, p.z)) return false;
                return true;
            }
            Vector3 SidePoint(float s, float side, float off)
            {
                var p = line.GetPoint(s) + line.GetRight(s) * (side * off);
                p.y = terrain.Height(p.x, p.z);
                return p;
            }
            int SeaSide(float s)
            {
                if (!terrain.Island) return 0;
                var a = SidePoint(s, 1f, 40f); var b = SidePoint(s, -1f, 40f);
                float ca = terrain.CoastDistance(a.x, a.z), cb = terrain.CoastDistance(b.x, b.z);
                if (Mathf.Min(ca, cb) > 120f) return 0;
                return ca < cb ? 1 : -1;
            }
            int LakeSide(float s)
            {
                var a = SidePoint(s, 1f, 25f); var b = SidePoint(s, -1f, 25f);
                float la = terrain.LakeMask(a.x, a.z), lb = terrain.LakeMask(b.x, b.z);
                if (Mathf.Max(la, lb) < 0.05f) return 0;
                return la > lb ? 1 : -1;
            }

            // ---- 박자: 구간마다 지역에 맞는 띠(담·울타리·가로등) ----
            for (float s = 30f + R() * 60f; s < line.Length - 30f;)
            {
                float len = 70f + R() * 90f;
                var region = regionAt(line.GetPoint(s));
                int sea = SeaSide(s), lake = LakeSide(s);
                switch (region)
                {
                    case "harbor":
                        for (float t = s; t < s + len && t < line.Length - 30f; t += 32f)
                            if (Clear(t)) for (int side = -1; side <= 1; side += 2) { LampPost(Chunk(t), SidePoint(t, side, 7.2f), line.GetRight(t) * -side); pieces++; }
                        break;
                    case "lake":
                    {
                        int side = lake != 0 ? lake : (R() < 0.5f ? 1 : -1);
                        pieces += Fence(line, s, Mathf.Min(s + len, line.Length - 30f), side, 7.0f, Chunk, Clear, SidePoint);
                        break;
                    }
                    case "forest":
                    {
                        int side = R() < 0.5f ? 1 : -1;
                        pieces += Wall(line, s, Mathf.Min(s + len, line.Length - 30f), side, 7.0f, true, Chunk, Clear, SidePoint);
                        break;
                    }
                    case "highland":
                    {
                        int side = R() < 0.5f ? 1 : -1;
                        if (R() < 0.6f) pieces += Wall(line, s, Mathf.Min(s + len * 1.5f, line.Length - 30f), side, 7.0f, false, Chunk, Clear, SidePoint);
                        else pieces += Fence(line, s, Mathf.Min(s + len, line.Length - 30f), side, 7.0f, Chunk, Clear, SidePoint);
                        // 건초 더미 무리(조금 떨어진 풀밭)
                        for (int i = 0; i < 3 + (int)(R() * 4); i++)
                        {
                            float t = s + R() * len;
                            var p = SidePoint(t, -side, 18f + R() * 25f);
                            if (terrain.RoadDistance(p.x, p.z) < 12f || 1f - terrain.NormalAt(p.x, p.z).y > 0.15f) continue;
                            HayBale(Chunk(t), p, R() * 360f);
                            pieces++;
                        }
                        break;
                    }
                    case "cliff":
                    {
                        int side = sea != 0 ? -sea : (R() < 0.5f ? 1 : -1);   // 바다 반대쪽(가드레일은 바다 쪽에 이미 있음)
                        pieces += Wall(line, s, Mathf.Min(s + len, line.Length - 30f), side, 7.0f, false, Chunk, Clear, SidePoint);
                        break;
                    }
                }
                s += len + 50f + R() * 70f;
            }

            // ---- 급커브 화살표 표지(커브 바깥쪽) ----
            for (float s = 40f; s < line.Length - 40f; s += 10f)
            {
                var f0 = line.GetForward(s - 25f); var f1 = line.GetForward(s + 25f);
                float turn = Vector3.SignedAngle(f0, f1, Vector3.up);
                if (Mathf.Abs(turn) < 38f || !Clear(s)) continue;
                int outer = turn > 0 ? -1 : 1;   // 오른쪽으로 꺾이면 왼쪽이 바깥
                for (int i = -1; i <= 1; i++)
                {
                    float t = s + i * 9f;
                    var p = SidePoint(t, outer, 7.4f);
                    Chevron(Chunk(t), p, line.GetRight(t) * -outer, turn > 0);
                    pieces++;
                }
                s += 60f;
            }

            // ---- 바위벽: 길 옆 둑·언덕이 높은 곳(45m 밖이 6m 넘게 높음)은 14~20m에 바위를 줄지어 세워 깎아낸 비탈처럼 ----
            for (int side = -1; side <= 1; side += 2)
                for (float s = 20f + R() * 10f; s < line.Length - 20f; s += 5.5f + R() * 3f)
                {
                    if (!Clear(s)) continue;
                    var far = SidePoint(s, side, 45f);
                    float rise = far.y - line.GetPoint(s).y;   // 노면 기준(길이 높은 고원에서도 옆 언덕이 솟은 곳만)
                    if (rise < 6f || terrain.LakeMask(far.x, far.z) > 0.02f) continue;
                    if (terrain.Island && terrain.CoastDistance(far.x, far.z) < 40f) continue;
                    float off = 14.5f + R() * 4.5f;
                    var p = SidePoint(s, side, off);
                    float tall = Mathf.Clamp(rise * (0.45f + 0.25f * R()), 3f, 12f);
                    var pal = CourseTerrainBuilder.PaletteFor(regionAt(p));
                    RockOutcrop(RockChunk(s), p, line.GetForward(s), tall, pal.Dark, R);
                    pieces++;
                }

            // ---- 지역마다 구간 아치 하나: 가장 곧은 곳 ----
            var reg = e.region;
            if (!archDone.Contains(reg) && reg != "harbor")
            {
                float best = -1f, bestS = -1f;
                for (float s = 80f; s < line.Length - 80f; s += 20f)
                {
                    if (!Clear(s)) continue;
                    float straight = Vector3.Dot(line.GetForward(s - 40f), line.GetForward(s + 40f));
                    if (straight > best) { best = straight; bestS = s; }
                }
                if (bestS > 0f)
                {
                    var c = line.GetPoint(bestS);
                    var col = reg switch { "lake" => new Color(0.15f, 0.62f, 0.32f), "highland" => new Color(0.92f, 0.45f, 0.12f), "cliff" => new Color(0.15f, 0.45f, 0.85f), _ => new Color(0.62f, 0.25f, 0.70f) };
                    Arch(Chunk(bestS), c, line.GetForward(bestS), col);
                    terrain.Exclusions.Add((new Vector2(c.x, c.z), 1f));
                    archDone.Add(reg);
                    pieces++;
                }
            }

            foreach (var (prefix, dict, m) in new[] { ("Roadside", chunks, mat), ("RockWall", rockChunks, rockMat) })
                foreach (var (k, mb) in dict)
                {
                    if (mb.V.Count == 0) continue;
                    var mesh = SaveMesh(mb.ToMesh($"{prefix}_{e.id}_{k}"), Dir, $"{prefix}_{e.id}_{k}");
                    var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(root, false);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = m;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    // 담·울타리·가로등 덩어리는 화면에서 작아지면 그리지 않음(바위벽은 멀리서도 보이게 둠)
                    if (prefix == "Roadside") go.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.035f, new Renderer[] { mr }) });
                }
        }
        Debug.Log($"[Scrun] 도로변 꾸미기 {pieces}개");
    }

    // ---------------- 띠 ----------------
    delegate StylizedFlora.MB ChunkFn(float s);
    delegate bool ClearFn(float s);
    delegate Vector3 SideFn(float s, float side, float off);

    static int Wall(Polyline line, float s0, float s1, int side, float off, bool mossy, ChunkFn chunk, ClearFn clear, SideFn at)
    {
        int n = 0;
        for (float s = s0; s < s1; s += 0.95f)
        {
            if (!clear(s)) continue;
            var p = at(s, side, off);
            var fwd = line.GetForward(s);
            var rot = Quaternion.LookRotation(fwd);
            var mb = chunk(s);
            for (int row = 0; row < 2; row++)
            {
                float len = 0.75f + R() * 0.35f, h = 0.36f + R() * 0.08f;
                var c = Color.Lerp(StoneDark, Stone, R());
                if (mossy && row == 1 && R() < 0.7f) c = Color.Lerp(c, Moss, 0.6f);
                var center = p + Vector3.up * (0.19f + row * 0.4f) + fwd * (row % 2 == 0 ? 0f : 0.4f);
                Box(mb, center, new Vector3(0.55f - row * 0.06f, h, len), rot * Quaternion.Euler((R() - 0.5f) * 6f, (R() - 0.5f) * 10f, (R() - 0.5f) * 6f), c);
            }
            n++;
        }
        return n > 0 ? 1 : 0;
    }

    static int Fence(Polyline line, float s0, float s1, int side, float off, ChunkFn chunk, ClearFn clear, SideFn at)
    {
        int n = 0;
        Vector3? prev = null;
        for (float s = s0; s < s1; s += 2.5f)
        {
            if (!clear(s)) { prev = null; continue; }
            var p = at(s, side, off);
            var mb = chunk(s);
            Box(mb, p + Vector3.up * 0.55f, new Vector3(0.13f, 1.1f, 0.13f), Quaternion.Euler(0f, R() * 10f, 0f), Wood);
            if (prev.HasValue)
            {
                var a = prev.Value; var mid = (a + p) / 2f; var dir = p - a;
                var rot = Quaternion.LookRotation(dir.normalized);
                float len = dir.magnitude;
                Box(mb, mid + Vector3.up * 0.85f, new Vector3(0.06f, 0.12f, len), rot, WoodLight);
                Box(mb, mid + Vector3.up * 0.45f, new Vector3(0.06f, 0.12f, len), rot, WoodLight);
            }
            prev = p;
            n++;
        }
        return n > 0 ? 1 : 0;
    }

    // ---------------- 하나짜리 ----------------
    internal static void LampPost(StylizedFlora.MB mb, Vector3 p, Vector3 towardRoad)
    {
        towardRoad.y = 0f; towardRoad.Normalize();
        StylizedFlora.Cylinder(mb, p, p + Vector3.up * 4.6f, 0.09f, 0.06f, 6, Metal, Metal);
        Box(mb, p + Vector3.up * 0.25f, new Vector3(0.3f, 0.5f, 0.3f), Quaternion.identity, Metal);
        var arm = p + Vector3.up * 4.5f + towardRoad * 0.5f;
        Box(mb, arm, new Vector3(0.06f, 0.06f, 1.0f), Quaternion.LookRotation(towardRoad), Metal);
        var head = p + Vector3.up * 4.35f + towardRoad * 1.0f;
        Box(mb, head + Vector3.up * 0.1f, new Vector3(0.45f, 0.12f, 0.45f), Quaternion.LookRotation(towardRoad), Metal);
        Box(mb, head - Vector3.up * 0.06f, new Vector3(0.32f, 0.22f, 0.32f), Quaternion.LookRotation(towardRoad), Lamp);
    }

    static void Chevron(StylizedFlora.MB mb, Vector3 p, Vector3 face, bool pointRight)
    {
        face.y = 0f; face.Normalize();
        var rot = Quaternion.LookRotation(face);
        Box(mb, p + Vector3.up * 0.6f, new Vector3(0.08f, 1.2f, 0.08f), rot, new Color(0.85f, 0.85f, 0.82f));
        var board = p + Vector3.up * 1.45f;
        Box(mb, board, new Vector3(0.75f, 0.6f, 0.04f), rot, new Color(0.95f, 0.78f, 0.12f));
        // 화살표 ">" 두 획
        float dir = pointRight ? 1f : -1f;
        var right = rot * Vector3.right;
        Box(mb, board + right * dir * 0.04f + Vector3.up * 0.11f + face * 0.025f, new Vector3(0.36f, 0.09f, 0.02f), rot * Quaternion.Euler(0f, 0f, -dir * 40f), new Color(0.1f, 0.1f, 0.1f));
        Box(mb, board + right * dir * 0.04f - Vector3.up * 0.11f + face * 0.025f, new Vector3(0.36f, 0.09f, 0.02f), rot * Quaternion.Euler(0f, 0f, dir * 40f), new Color(0.1f, 0.1f, 0.1f));
    }

    static void HayBale(StylizedFlora.MB mb, Vector3 p, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var axis = rot * Vector3.right;
        StylizedFlora.Cylinder(mb, p + Vector3.up * 0.7f - axis * 0.6f, p + Vector3.up * 0.7f + axis * 0.6f, 0.72f, 0.72f, 12, Hay * 0.85f, Hay);
        // 마구리(동그란 면)
        for (int side = -1; side <= 1; side += 2)
        {
            var c = p + Vector3.up * 0.7f + axis * 0.6f * side;
            int center = mb.Add(c, axis * side, Hay * 0.95f);
            int start = mb.V.Count;
            for (int i = 0; i <= 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                var d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, axis) * Vector3.up;
                mb.Add(c + d * 0.72f, axis * side, Hay * 0.8f);
            }
            for (int i = 0; i < 12; i++)
            {
                int a = start + i, b = start + i + 1;
                var n = Vector3.Cross(mb.V[a] - mb.V[center], mb.V[b] - mb.V[center]);
                if (Vector3.Dot(n, axis * side) > 0) mb.Tri(center, a, b); else mb.Tri(center, b, a);
            }
        }
    }

    // 바위 노두: 길 방향으로 긴 각진 바위(아래는 땅에 묻힘), 가로 층 무늬, 윗면은 이끼·풀색
    // 정점 색은 선형 값으로 해석되므로(화면에선 더 밝게 보임) 어둡게 잡는다
    static readonly Color RockLight = new(0.30f, 0.26f, 0.22f), RockDark = new(0.13f, 0.115f, 0.10f), RockWarm = new(0.30f, 0.19f, 0.12f);

    internal static void RockOutcrop(StylizedFlora.MB mb, Vector3 p, Vector3 fwd, float tall, Color moss, System.Func<float> rnd)
    {
        // 큰 바위 하나 + 옆에 기대 선 작은 바위 1~2개(한 덩어리로 보이지 않게)
        OneRock(mb, p, fwd, tall, moss, rnd, tall > 9f ? 2 : 1);
        int extra = rnd() < 0.6f ? 1 : 0;
        for (int i = 0; i < extra; i++)
        {
            var side = Vector3.Cross(Vector3.up, fwd).normalized;
            var q = p + fwd * ((rnd() - 0.5f) * tall * 1.2f) + side * ((rnd() - 0.5f) * tall * 0.5f);
            OneRock(mb, q + Vector3.down * 0.3f, fwd, tall * (0.35f + 0.3f * rnd()), moss, rnd, 1);
        }
    }

    internal static void OneRock(StylizedFlora.MB mb, Vector3 p, Vector3 fwd, float tall, Color moss, System.Func<float> rnd, int subdiv)
    {
        fwd.y = 0f; fwd.Normalize();
        var rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, (rnd() - 0.5f) * 40f, 0f);
        float len = tall * (0.9f + 0.6f * rnd()), depth = tall * (0.55f + 0.3f * rnd());
        var center = p + Vector3.up * (tall * 0.38f - 0.8f);
        float seed = rnd() * 100f, bandOff = rnd() * 10f;
        int start = mb.V.Count;
        StylizedFlora.FacetedBlob(mb, Vector3.zero, new Vector3(depth, tall * 0.62f, len) * 0.5f * 1.6f, subdiv, 0.4f, seed, local =>
        {
            float band = Mathf.PerlinNoise(local.y * 0.55f + bandOff, 0.3f);
            var c = Color.Lerp(RockDark, RockLight, 0.35f + 0.55f * band);
            c = Color.Lerp(c, RockWarm, Mathf.PerlinNoise(local.x * 0.2f + seed, local.z * 0.2f) * 0.35f);
            if (local.y > tall * 0.22f && local.normalized.y > 0.55f) c = Color.Lerp(c, moss.linear, 0.75f);
            c.a = 1f;
            return c;
        });
        // 회전·이동(FacetedBlob은 원점 기준으로 만들었음)
        for (int i = start; i < mb.V.Count; i++)
        {
            mb.V[i] = center + rot * mb.V[i];
            mb.N[i] = rot * mb.N[i];
        }
    }

    // 구간 아치: 기둥 2 + 위 들보 + 배너(체크무늬)
    static void Arch(StylizedFlora.MB mb, Vector3 c, Vector3 fwd, Color color)
    {
        fwd.y = 0f; fwd.Normalize();
        var rot = Quaternion.LookRotation(fwd);
        var right = rot * Vector3.right;
        float half = CourseRoadBuilder.ShoulderOuter + 0.6f;
        var white = new Color(0.95f, 0.95f, 0.93f);
        foreach (int s in new[] { -1, 1 })
        {
            Box(mb, c + right * half * s + Vector3.up * 3.1f, new Vector3(0.45f, 6.2f, 0.45f), rot, color * 0.75f);
            Box(mb, c + right * half * s + Vector3.up * 0.3f, new Vector3(0.9f, 0.6f, 0.9f), rot, color * 0.6f);
        }
        Box(mb, c + Vector3.up * 6.0f, new Vector3(half * 2f + 0.45f, 0.35f, 0.35f), rot, color * 0.75f);
        // 배너: 칸마다 색 번갈아(체크)
        int cols = 16;
        float w = (half * 2f - 0.6f) / cols;
        for (int i = 0; i < cols; i++)
            for (int row = 0; row < 2; row++)
            {
                var pc = c + right * (-half + 0.3f + w * (i + 0.5f)) + Vector3.up * (5.45f - row * 0.5f);
                Box(mb, pc, new Vector3(w, 0.5f, 0.06f), rot, (i + row) % 2 == 0 ? color : white);
            }
    }

    // 상자(면마다 법선, 정점 색)
    public static void Box(StylizedFlora.MB mb, Vector3 c, Vector3 size, Quaternion rot, Color col)
    {
        var h = size / 2f;
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var d in dirs)
        {
            var u = d == Vector3.up || d == Vector3.down ? Vector3.right : Vector3.up;
            var w = Vector3.Cross(d, u);
            var fc = Vector3.Scale(d, h);
            var du = Vector3.Scale(u, h); var dw = Vector3.Scale(w, h);
            var n = rot * d;
            var shade = d == Vector3.down ? col * 0.7f : col;
            shade.a = 1f;
            int a = mb.Add(c + rot * (fc - du - dw), n, shade);
            int b = mb.Add(c + rot * (fc + du - dw), n, shade);
            int cc = mb.Add(c + rot * (fc + du + dw), n, shade);
            int dd = mb.Add(c + rot * (fc - du + dw), n, shade);
            if (Vector3.Dot(Vector3.Cross(mb.V[b] - mb.V[a], mb.V[cc] - mb.V[a]), n) > 0) { mb.Tri(a, b, cc); mb.Tri(a, cc, dd); }
            else { mb.Tri(a, cc, b); mb.Tri(a, dd, cc); }
        }
    }
}
