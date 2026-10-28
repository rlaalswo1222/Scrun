using System.Collections.Generic;
using UnityEngine;

// 손그림풍 식생·바위 메쉬 생성(정점 색 + 잎 텍스처 아틀라스, Scrun/Stylized 셰이더용).
// 나무·덤불 = 줄기·가지 골격 + 잎 뭉치. 잎 뭉치는 안쪽 덩어리(틈 메움) + 바깥 잎 카드(FoliageAtlas의 손그림 잎)로
// 테두리가 들쭉날쭉한 "진짜 나무" 실루엣을 만든다. 잎 카드 법선은 나무 전체를 감싸는 둥근 방향 → 부드러운 입체감.
// LOD는 모두 같은 골격·같은 잎 뭉치 위치를 쓰고 카드 수만 줄인다(멀리서 바뀌어도 모양이 그대로 → 꿈틀거리지 않음).
//  나무: LOD 0(카드 많이) · 1(카드 적고 크게) · 2(덩어리 여러 개) · 3(십자 잎 카드 — 아주 가벼움)
//  덤불·꽃·고사리: LOD 0 · 1 · 2
// 줄기·가지는 사진 나무껍질(NatureTextures.BarkArray, 반복 UV) — UV1.y = 껍질 종류 + 1, 잎·덩어리는 0
// 원점 = 바닥 중심, +Y 위. UV1.x = 잎 떨림 가중치(카드 1, 줄기 0)
public static class StylizedFlora
{
    public enum TreeKind { Broadleaf, Blossom, Autumn, Conifer, Poplar, Redwood, Palm }
    public const int TreeLods = 4;

    // ---------------- 메쉬 조립 도구 ----------------
    public class MB
    {
        public readonly List<Vector3> V = new(), N = new();
        public readonly List<Color> C = new();
        public readonly List<Vector2> UV = new(), UV1 = new();
        public readonly List<int> T = new();

        public int Add(Vector3 p, Vector3 n, Color c) => Add(p, n, c, FoliageAtlas.SolidUV, 0f);

        public int Add(Vector3 p, Vector3 n, Color c, Vector2 uv, float flutter)
        {
            V.Add(p); N.Add(n); C.Add(c); UV.Add(uv); UV1.Add(new Vector2(flutter, 0f));
            return V.Count - 1;
        }

        // 껍질 정점: uv = 반복 껍질 UV(둘레·높이 방향 텍스처 장 수)
        public int AddBark(Vector3 p, Vector3 n, Color c, Vector2 uv, NatureTextures.Bark kind)
        {
            V.Add(p); N.Add(n); C.Add(c); UV.Add(uv); UV1.Add(new Vector2(0f, (int)kind + 1));
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetColors(C); m.SetUVs(0, UV); m.SetUVs(1, UV1); m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // ---------------- 정이십면체 구 ----------------
    static readonly Dictionary<int, (Vector3[] v, int[] t)> icoCache = new();

    static (Vector3[] v, int[] t) Ico(int subdiv)
    {
        if (icoCache.TryGetValue(subdiv, out var c)) return c;
        float p = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new List<Vector3>
        {
            new(-1, p, 0), new(1, p, 0), new(-1, -p, 0), new(1, -p, 0),
            new(0, -1, p), new(0, 1, p), new(0, -1, -p), new(0, 1, -p),
            new(p, 0, -1), new(p, 0, 1), new(-p, 0, -1), new(-p, 0, 1),
        };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
        var t = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        for (int s = 0; s < subdiv; s++)
        {
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int idx)) return idx;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                return mid[key] = v.Count - 1;
            }
            var nt = new List<int>();
            for (int i = 0; i < t.Count; i += 3)
            {
                int a = t[i], b = t[i + 1], cc = t[i + 2];
                int ab = Mid(a, b), bc = Mid(b, cc), ca = Mid(cc, a);
                nt.AddRange(new[] { a, ab, ca, b, bc, ab, cc, ca, bc, ab, bc, ca });
            }
            t = nt;
        }
        return icoCache[subdiv] = (v.ToArray(), t.ToArray());
    }

    // 삼각형 (a,b,c)의 앞면 = Cross(b-a, c-a) 쪽. center에서 바깥을 보도록 맞춤
    static void FixOutward(MB mb, int startTri, Vector3 center)
    {
        for (int i = startTri; i < mb.T.Count; i += 3)
        {
            Vector3 a = mb.V[mb.T[i]], b = mb.V[mb.T[i + 1]], c = mb.V[mb.T[i + 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center) < 0)
                (mb.T[i + 1], mb.T[i + 2]) = (mb.T[i + 2], mb.T[i + 1]);
        }
    }

    static float Noise3(Vector3 p) =>
        (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.4f, p.z) + Mathf.PerlinNoise(p.z - 17.2f, p.x)) / 3f;

    // 울퉁불퉁한 덩어리. soft: 노멀을 덩어리 중심 방향과 섞어 부드럽게(나뭇잎 뭉치 느낌)
    public static void Blob(MB mb, Vector3 center, Vector3 scale, int subdiv, float bumpy, float seed,
                            System.Func<Vector3, Color> color, Vector3? softCenter = null, float soft = 0.6f)
    {
        var (uv, ut) = Ico(subdiv);
        int start = mb.V.Count, startTri = mb.T.Count;
        for (int i = 0; i < uv.Length; i++)
        {
            var d = uv[i];
            float r = 1f + bumpy * (Noise3(d * 2.3f + Vector3.one * seed) - 0.5f) * 2f;
            var p = center + Vector3.Scale(d * r, scale);
            var n = d;
            if (softCenter.HasValue) n = Vector3.Lerp(d, (p - softCenter.Value).normalized, soft).normalized;
            mb.Add(p, n, color(d));
        }
        for (int i = 0; i < ut.Length; i += 3) mb.Tri(start + ut[i], start + ut[i + 1], start + ut[i + 2]);
        FixOutward(mb, startTri, center);
    }

    // 각진(면마다 법선) 덩어리 = 바위
    public static void FacetedBlob(MB mb, Vector3 center, Vector3 scale, int subdiv, float bumpy, float seed,
                                   System.Func<Vector3, Color> color)
    {
        var (uv, ut) = Ico(subdiv);
        var pos = new Vector3[uv.Length];
        for (int i = 0; i < uv.Length; i++)
        {
            var d = uv[i];
            float r = 1f + bumpy * (Noise3(d * 1.7f + Vector3.one * seed) - 0.5f) * 2f;
            var p = Vector3.Scale(d * r, scale);
            if (p.y < -scale.y * 0.35f) p.y = -scale.y * 0.35f + (p.y + scale.y * 0.35f) * 0.15f;   // 바닥은 납작
            pos[i] = center + p;
        }
        for (int i = 0; i < ut.Length; i += 3)
        {
            Vector3 a = pos[ut[i]], b = pos[ut[i + 1]], c = pos[ut[i + 2]];
            var n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, (a + b + c) / 3f - center) < 0) { (b, c) = (c, b); n = -n; }
            var col = color((a + b + c) / 3f - center);
            int ia = mb.Add(a, n, col), ib = mb.Add(b, n, col), ic = mb.Add(c, n, col);
            mb.Tri(ia, ib, ic);
        }
    }

    // 둘레를 껍질 텍스처 몇 장으로 감쌀지(이음매 없게 정수)
    static float BarkAround(float r, NatureTextures.Bark kind) =>
        Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * r / NatureTextures.BarkMeters[(int)kind]));

    // 줄기(뿔대). 아래 r0 → 위 r1. bark가 있으면 껍질 텍스처(v0 = 아래 끝의 높이 방향 시작, m)
    public static void Cylinder(MB mb, Vector3 from, Vector3 to, float r0, float r1, int seg, Color c0, Color c1,
                                NatureTextures.Bark? bark = null, float v0 = 0f)
    {
        var axis = (to - from).normalized;
        var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        var up2 = Vector3.Cross(side, axis);
        int start = mb.V.Count;
        float around = bark.HasValue ? BarkAround((r0 + r1) * 0.5f, bark.Value) : 0f;
        float m = bark.HasValue ? NatureTextures.BarkMeters[(int)bark.Value] : 1f;
        float len = (to - from).magnitude;
        for (int i = 0; i <= seg; i++)
        {
            float a = (float)i / seg * Mathf.PI * 2f;
            var d = side * Mathf.Cos(a) + up2 * Mathf.Sin(a);
            if (bark.HasValue)
            {
                float u = (float)i / seg * around;
                mb.AddBark(from + d * r0, d, c0, new Vector2(u, v0 / m), bark.Value);
                mb.AddBark(to + d * r1, d, c1, new Vector2(u, (v0 + len) / m), bark.Value);
                continue;
            }
            mb.Add(from + d * r0, d, c0);
            mb.Add(to + d * r1, d, c1);
        }
        for (int i = 0; i < seg; i++)
        {
            int k = start + i * 2;
            mb.Tri(k, k + 1, k + 2);
            mb.Tri(k + 1, k + 3, k + 2);
        }
        FixOutwardRange(mb, start, (from + to) / 2f, axis);
    }

    // 여러 마디 줄기(휘거나 줄무늬가 있는 줄기). 마디마다 반지름·색
    static void Tube(MB mb, IList<Vector3> pts, IList<float> radii, int seg, System.Func<int, float, Color> color,
                     NatureTextures.Bark? bark = null)
    {
        int start = mb.V.Count;
        int rows = pts.Count;
        Vector3 prevSide = Vector3.zero;
        float around = 0f, m = 1f, along = 0f;
        if (bark.HasValue)
        {
            float mid = 0f; foreach (var rr in radii) mid += rr; mid /= radii.Count;
            around = BarkAround(mid, bark.Value);
            m = NatureTextures.BarkMeters[(int)bark.Value];
        }
        for (int r = 0; r < rows; r++)
        {
            if (r > 0) along += (pts[r] - pts[r - 1]).magnitude;
            var axis = (r < rows - 1 ? pts[r + 1] - pts[r] : pts[r] - pts[r - 1]).normalized;
            var side = r == 0 ? Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized
                              : Vector3.ProjectOnPlane(prevSide, axis).normalized;
            prevSide = side;
            var up2 = Vector3.Cross(side, axis);
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                var d = side * Mathf.Cos(a) + up2 * Mathf.Sin(a);
                if (bark.HasValue) mb.AddBark(pts[r] + d * radii[r], d, color(r, (float)i / seg), new Vector2((float)i / seg * around, along / m), bark.Value);
                else mb.Add(pts[r] + d * radii[r], d, color(r, (float)i / seg));
            }
        }
        int startTri = mb.T.Count;
        for (int r = 0; r < rows - 1; r++)
            for (int i = 0; i < seg; i++)
            {
                int a = start + r * (seg + 1) + i, b = a + 1, c = a + seg + 1, d = c + 1;
                mb.Tri(a, c, b); mb.Tri(b, c, d);
            }
        // 바깥이 앞면
        for (int t = startTri; t < mb.T.Count; t += 3)
        {
            Vector3 a = mb.V[mb.T[t]], b = mb.V[mb.T[t + 1]], c = mb.V[mb.T[t + 2]];
            var n = mb.N[mb.T[t]] + mb.N[mb.T[t + 1]] + mb.N[mb.T[t + 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) (mb.T[t + 1], mb.T[t + 2]) = (mb.T[t + 2], mb.T[t + 1]);
        }
    }

    static void FixOutwardRange(MB mb, int startVertex, Vector3 center, Vector3 axis)
    {
        for (int i = 0; i < mb.T.Count; i += 3)
        {
            if (mb.T[i] < startVertex) continue;
            Vector3 a = mb.V[mb.T[i]], b = mb.V[mb.T[i + 1]], c = mb.V[mb.T[i + 2]];
            var n = Vector3.Cross(b - a, c - a);
            var mid = (a + b + c) / 3f;
            var outDir = mid - center; outDir -= Vector3.Dot(outDir, axis) * axis;
            if (Vector3.Dot(n, outDir) < 0) (mb.T[i + 1], mb.T[i + 2]) = (mb.T[i + 2], mb.T[i + 1]);
        }
    }

    // 원뿔(침엽수 한 층): 테두리가 들쭉날쭉하고 아래로 처짐
    static void ConeTier(MB mb, Vector3 baseCenter, float radius, float height, int seg, float jag, float seed, Color inner, Color outer)
    {
        int startTri = mb.T.Count;
        int apex = mb.Add(baseCenter + Vector3.up * height, Vector3.up, outer);
        int ring = mb.V.Count;
        for (int i = 0; i <= seg; i++)
        {
            float a = (float)i / seg * Mathf.PI * 2f + seed;
            float r = radius * (1f + ((i % 2 == 0) ? jag : -jag) * (0.6f + 0.4f * Mathf.PerlinNoise(i * 0.7f, seed)));
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var p = baseCenter + d * r + Vector3.down * (0.25f * radius * (0.5f + 0.5f * Mathf.PerlinNoise(seed, i * 0.9f)));
            var n = (d * 0.8f + Vector3.up * 0.6f).normalized;
            mb.Add(p, n, Color.Lerp(outer, inner, 0.15f));
        }
        for (int i = 0; i < seg; i++) mb.Tri(apex, ring + i + 1, ring + i);
        int bottom = mb.Add(baseCenter + Vector3.up * height * 0.15f, Vector3.down, inner);
        for (int i = 0; i < seg; i++) mb.Tri(bottom, ring + i, ring + i + 1);
        FixOutward(mb, startTri, baseCenter + Vector3.up * height * 0.3f);
    }

    static Color Shade(Color c, float k) => new(c.r * k, c.g * k, c.b * k, 1f);

    static Vector2 RectUV(Rect r, float u, float v)
    {
        // 칸 가장자리 2% 안쪽(밉맵에서 이웃 칸이 번지지 않게)
        const float pad = 0.02f;
        return new Vector2(r.xMin + r.width * Mathf.Lerp(pad, 1f - pad, u), r.yMin + r.height * Mathf.Lerp(pad, 1f - pad, v));
    }

    // 잎 카드(가운데 기준 사각형). 법선은 normalCenter에서 바깥(둥근 덩어리처럼 빛남)
    static void Card(MB mb, Vector3 center, Vector3 right, Vector3 up, Rect uvRect, Color col, Vector3 normalCenter, float flutter, float bottomShade = 0.78f)
    {
        var corners = new[] { center - right - up, center + right - up, center + right + up, center - right + up };
        var uvs = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        var cardN = Vector3.Cross(right, up).normalized;
        int s = mb.V.Count;
        for (int i = 0; i < 4; i++)
        {
            var radial = (corners[i] - normalCenter).normalized;
            var n = Vector3.Lerp(Vector3.Dot(cardN, radial) >= 0 ? cardN : -cardN, radial, 0.8f).normalized;
            var c = i < 2 ? Shade(col, bottomShade) : col;
            mb.Add(corners[i], n, c, RectUV(uvRect, uvs[i].x, uvs[i].y), flutter * (i < 2 ? 0.6f : 1f));
        }
        mb.Tri(s, s + 2, s + 1); mb.Tri(s, s + 3, s + 2);
    }

    // 가지 카드(밑동에서 끝으로): u = 길이 방향. bend = 끝으로 갈수록 아래로 처지는 정도(m), segs = 휘는 마디
    static void Frond(MB mb, Vector3 basePos, Vector3 dir, Vector3 across, float len, float width, float bend, int segs,
                      Rect uvRect, Color baseCol, Color tipCol, Vector3 normalCenter, float flutter)
    {
        dir.Normalize(); across = across.normalized * (width * 0.5f);
        var cardN = Vector3.Cross(dir, across).normalized;
        int s = mb.V.Count;
        for (int k = 0; k <= segs; k++)
        {
            float t = (float)k / segs;
            var p = basePos + dir * (len * t) + Vector3.down * (bend * t * t);
            var c = Color.Lerp(baseCol, tipCol, t);
            for (int side = -1; side <= 1; side += 2)
            {
                var q = p + across * side;
                var radial = (q - normalCenter).normalized;
                var n = Vector3.Lerp(Vector3.Dot(cardN, radial) >= 0 ? cardN : -cardN, radial, 0.75f).normalized;
                mb.Add(q, n, c, RectUV(uvRect, t, side < 0 ? 0f : 1f), flutter * t);
            }
        }
        for (int k = 0; k < segs; k++)
        {
            int a = s + k * 2, b = a + 1, c = a + 2, d = a + 3;
            mb.Tri(a, c, b); mb.Tri(b, c, d);
        }
    }

    static Vector3 RandomDir(System.Func<float> R)
    {
        while (true)
        {
            var v = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f);
            float m = v.sqrMagnitude;
            if (m > 0.01f && m <= 1f) return v / Mathf.Sqrt(m);
        }
    }

    // 덩어리에 잎 카드 두르기: 바깥·위쪽으로 치우친 방향에 카드를 세워 붙임
    static void LeafCards(MB mb, Vector3 c, float r, int count, float size, System.Func<float> R, Color dark, Color light,
                          Vector3 crown, float crownR, float flutter, Rect uvRect)
    {
        for (int i = 0; i < count; i++)
        {
            var d = RandomDir(R);
            var outward = (c - crown).sqrMagnitude > 0.01f ? (c - crown).normalized : Vector3.up;
            d = (d + outward * 0.6f + Vector3.up * 0.35f).normalized;
            var pos = c + d * r * (0.5f + 0.35f * R());
            // 카드 면: 바깥을 대략 향하되 비틀어서(옆에서 봐도 얇은 선으로 보이지 않게)
            var nrm = (d + RandomDir(R) * 0.7f).normalized;
            var right = Vector3.Cross(nrm, Mathf.Abs(nrm.y) > 0.95f ? Vector3.forward : Vector3.up).normalized;
            var up = Vector3.Cross(right, nrm).normalized;
            float spin = R() * Mathf.PI * 2f;
            var r2 = right * Mathf.Cos(spin) + up * Mathf.Sin(spin);
            var u2 = Vector3.Cross(nrm, r2);
            float sz = size * (0.85f + 0.3f * R()) * 0.5f;
            // 색: 크라운 위쪽·해 쪽(+x,+y)이 밝고 아래·안쪽이 어두움
            float h = Mathf.Clamp01(((pos - crown).y / Mathf.Max(0.5f, crownR)) * 0.5f + 0.5f);
            var col = Color.Lerp(dark, light, h * 0.8f + 0.2f * R());
            Card(mb, pos, r2 * sz, u2 * sz, uvRect, col, crown, flutter);
        }
    }

    // ---------------- 나무 ----------------
    struct Cluster { public Vector3 c; public float r; }

    public static Mesh Tree(TreeKind kind, int lod, int seed)
    {
        switch (kind)
        {
            case TreeKind.Conifer: return Conifer(lod, seed);
            case TreeKind.Redwood: return Redwood(lod, seed);
            case TreeKind.Palm: return Palm(lod, seed);
            default: return Leafy(kind, lod, seed);
        }
    }

    static (Color baseC, Color topC) LeafColors(TreeKind kind, System.Func<float> R)
    {
        switch (kind)
        {
            case TreeKind.Blossom:
                return (Color.Lerp(new Color(0.80f, 0.42f, 0.58f), new Color(0.88f, 0.52f, 0.66f), R()), new Color(1f, 0.80f, 0.86f));
            case TreeKind.Autumn:
                return R() < 0.5f
                    ? (new Color(0.80f, 0.36f, 0.10f), new Color(1f, 0.66f, 0.26f))
                    : (new Color(0.78f, 0.58f, 0.12f), new Color(1f, 0.84f, 0.36f));
            case TreeKind.Poplar:
                return (new Color(0.20f, 0.38f, 0.14f), new Color(0.50f, 0.66f, 0.24f));
            default:
                return (Color.Lerp(new Color(0.20f, 0.40f, 0.13f), new Color(0.28f, 0.46f, 0.12f), R()), new Color(0.56f, 0.72f, 0.26f));
        }
    }

    // 활엽수(참나무형·벚꽃·단풍·포플러): 줄기 → 굵은 가지 3~5개 → 잔가지, 가지 끝마다 잎 뭉치
    static Mesh Leafy(TreeKind kind, int lod, int seed)
    {
        var rng = new System.Random(seed * 7919 + (int)kind * 101);
        float R() => (float)rng.NextDouble();
        var mb = new MB();
        var bark = kind == TreeKind.Blossom ? new Color(0.30f, 0.21f, 0.17f) : new Color(0.34f, 0.25f, 0.18f);
        var (leafBase, leafTop) = LeafColors(kind, R);
        int trunkSeg = lod == 0 ? 8 : lod == 1 ? 6 : 4;

        bool poplar = kind == TreeKind.Poplar;
        float size = kind == TreeKind.Blossom ? 1.05f : poplar ? 0.85f : 1f + 0.15f * R();
        float trunkH = poplar ? 2.4f : kind == TreeKind.Blossom ? 2.2f + 0.6f * R() : 3.2f + 1.3f * R();
        var top = new Vector3((R() - 0.5f) * 0.7f, trunkH, (R() - 0.5f) * 0.7f);

        // ---- 골격(모든 LOD 공통) ----
        var limbs = new List<(Vector3 a, Vector3 b, float r)>();
        var clusters = new List<Cluster>();
        if (poplar)
        {
            // 기둥처럼 위로 쌓인 뭉치
            float ch = 9f + 3f * R();
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                float rad = Mathf.Lerp(1.5f, 0.8f, t) * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.25f, 0.95f, t)) + 0.5f;
                var c = top + Vector3.up * (ch * Mathf.Lerp(0.12f, 0.92f, t)) + new Vector3((R() - 0.5f) * 0.8f, 0f, (R() - 0.5f) * 0.8f);
                clusters.Add(new Cluster { c = c, r = rad * (0.9f + 0.2f * R()) });
            }
            limbs.Add((top, top + Vector3.up * ch * 0.8f, 0.16f));
        }
        else
        {
            int n = 4 + (R() < 0.4f ? 1 : 0);
            float spread = kind == TreeKind.Blossom ? 1.25f : 1f;
            for (int i = 0; i < n; i++)
            {
                float az = (i + R() * 0.6f) / n * Mathf.PI * 2f;
                float el = (35f + 22f * R()) * Mathf.Deg2Rad;
                if (kind == TreeKind.Blossom) el *= 0.75f;
                var dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                float len = (2.4f + 1.2f * R()) * size * spread;
                var start = top - Vector3.up * (0.3f + 0.6f * R());
                var end = start + dir * len;
                limbs.Add((start, end, 0.22f * size));
                clusters.Add(new Cluster { c = end + Vector3.up * 0.4f, r = (1.55f + 0.5f * R()) * size });
                // 잔가지: 가지 가운데에서 바깥·위로
                var mid = Vector3.Lerp(start, end, 0.55f);
                var sdir = (dir + new Vector3((R() - 0.5f) * 1.2f, 0.7f, (R() - 0.5f) * 1.2f)).normalized;
                var send = mid + sdir * (1.4f + 0.6f * R()) * size;
                limbs.Add((mid, send, 0.12f * size));
                clusters.Add(new Cluster { c = send + Vector3.up * 0.3f, r = (1.2f + 0.4f * R()) * size });
            }
            // 꼭대기 + 가운데 채움
            clusters.Add(new Cluster { c = top + Vector3.up * (2.6f + 0.6f * R()) * size, r = (1.9f + 0.4f * R()) * size });
            clusters.Add(new Cluster { c = top + Vector3.up * 1.2f * size, r = 1.8f * size });
        }
        var crown = Vector3.zero; foreach (var cl in clusters) crown += cl.c; crown /= clusters.Count;
        float crownR = 0f; foreach (var cl in clusters) crownR = Mathf.Max(crownR, (cl.c - crown).magnitude + cl.r);

        // ---- 줄기·가지 ----
        // 껍질 사진 위에 곱하는 색(밑동은 어둡게 — 땅 그늘·흙)
        var barkT = kind == TreeKind.Blossom ? new Color(0.72f, 0.62f, 0.62f) : new Color(0.95f, 0.9f, 0.84f);
        const NatureTextures.Bark bk = NatureTextures.Bark.Broadleaf;
        if (lod <= 2)
        {
            Cylinder(mb, Vector3.down * 0.2f, Vector3.up * 0.7f, 0.55f * size, 0.36f * size, trunkSeg, Shade(barkT, 0.6f), Shade(barkT, 0.85f), bk);   // 뿌리 쪽 퍼짐
            Cylinder(mb, Vector3.up * 0.6f, top, 0.37f * size, 0.24f * size, trunkSeg, Shade(barkT, 0.85f), barkT, bk, 0.6f);
        }
        if (lod <= 1)
            foreach (var (a, b, r) in limbs)
                Cylinder(mb, a, Vector3.Lerp(a, b, 0.9f), r, r * 0.45f, lod == 0 ? 5 : 4, Shade(barkT, 0.85f), Shade(barkT, 0.8f), bk);

        // ---- 잎 ----
        var dark = Shade(leafBase, 0.62f);
        if (lod <= 1)
        {
            // 카드 수는 뭉치 크기에 비례(큰 뭉치에 카드가 모자라면 안쪽 덩어리의 각진 면이 드러남)
            float cardSize = lod == 0 ? 1.2f : 1.5f;
            foreach (var cl in clusters)
            {
                int cards = Mathf.RoundToInt((lod == 0 ? 6.5f : 3.5f) * cl.r);
                // 안쪽 덩어리는 아주 어둡게(카드 틈으로 보일 때 매끈한 밝은 면이 아니라 수관 속 그늘로)
                Blob(mb, cl.c, new Vector3(cl.r, cl.r * 0.85f, cl.r) * 0.62f, lod == 0 ? 1 : 0, 0.2f, seed + cl.c.x * 3.1f,
                     d => Color.Lerp(Shade(dark, 0.35f), Shade(dark, 0.5f), Mathf.Clamp01(d.y * 0.5f + 0.5f)), crown, 0.8f);
                LeafCards(mb, cl.c, cl.r, cards, cl.r * cardSize, R, leafBase, leafTop, crown, crownR, 1f, FoliageAtlas.Leaf);
            }
        }
        else if (lod == 2)
        {
            // 뭉치마다 어두운 작은 덩어리 + 큰 잎 카드 몇 장(매끈한 공 대신 테두리가 잎 모양)
            foreach (var cl in clusters)
            {
                Blob(mb, cl.c, new Vector3(cl.r, cl.r * 0.85f, cl.r) * 0.6f, 0, 0.2f, seed + cl.c.x * 3.1f, _ => Shade(dark, 0.45f), crown, 0.8f);
                LeafCards(mb, cl.c, cl.r, 3, cl.r * 2.1f, R, leafBase, leafTop, crown, crownR, 1f, FoliageAtlas.Leaf);
            }
        }
        else
        {
            // 아주 멀리: 십자로 엇갈린 잎 카드 2장 + 줄기 카드 2장(삼각형 8개). 테두리가 잎 모양이라 덩어리보다 나무답고 훨씬 가벼움
            // → 먼 언덕까지 숲을 빽빽하게 덮을 수 있음
            var leafCol = Color.Lerp(leafBase, leafTop, 0.45f);
            float az = seed * 0.7f;
            for (int k = 0; k < 2; k++)
            {
                float a = az + k * Mathf.PI * 0.5f;
                var right = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Card(mb, crown, right * crownR * 1.15f, Vector3.up * crownR * 1.05f, FoliageAtlas.Leaf, leafCol, crown, 0f, 0.7f);
                float tw = 0.35f * size;
                Card(mb, new Vector3(0f, (top.y - 0.2f) * 0.5f, 0f), right * tw, Vector3.up * (top.y + 0.2f) * 0.5f, FoliageAtlas.Solid, Shade(bark, 0.85f), Vector3.up * top.y * 0.5f, 0f, 0.8f);
            }
        }
        return mb.ToMesh($"Tree_{kind}_{seed}_L{lod}");
    }

    // 바늘잎 가지 층: 줄기를 따라 층마다 처진 가지 카드(사진 전나무 잔가지)를 돌려 붙임
    //  y0~y1 = 층이 있는 높이, lenAt(t) = 층 높이 비율 t(0 아래 ~ 1 위)의 가지 길이, perTier = 층당 가지 수
    static void NeedleTiers(MB mb, System.Func<float> R, float y0, float y1, int tiers, System.Func<float, int> perTier,
                            System.Func<float, float> lenAt, System.Func<float, float> trunkR, float widthK, int segs,
                            float skip, Color dark, Color mid, Color tip, Vector3 axisC, float flutter, Vector3 lean)
    {
        for (int i = 0; i < tiers; i++)
        {
            float t = (float)i / (tiers - 1);
            float y = Mathf.Lerp(y0, y1, t) + (R() - 0.5f) * (y1 - y0) / tiers * 0.6f;
            int n = perTier(t);
            float off = R() * Mathf.PI * 2f;
            for (int k = 0; k < n; k++)
            {
                if (R() < skip) continue;   // 듬성듬성 빈자리(실루엣이 들쭉날쭉)
                float az = off + (k + (R() - 0.5f) * 0.7f) / n * Mathf.PI * 2f;
                float len = lenAt(t) * (0.72f + 0.5f * R());
                float droop = 0.18f + 0.3f * (1f - t) + 0.15f * R();
                var dir = new Vector3(Mathf.Cos(az), -droop, Mathf.Sin(az)).normalized;
                var tangent = new Vector3(-Mathf.Sin(az), 0f, Mathf.Cos(az));
                // 가지 면을 비틀어(위로 향한 면 + 옆) 어느 쪽에서 봐도 납작한 선으로 안 보이게
                var across = Quaternion.AngleAxis((R() - 0.5f) * 80f, dir) * tangent;
                var basePos = lean * (y / Mathf.Max(1f, y1)) + new Vector3(0f, y, 0f) + new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)) * trunkR(t) * 0.8f;
                // 아래·안쪽 가지는 어둡고 위·바깥 끝이 밝음(수관 속 그늘)
                var bc = Color.Lerp(dark, mid, 0.25f + 0.45f * t);
                var tc = Color.Lerp(mid, tip, 0.25f + 0.45f * t + 0.2f * R());
                Frond(mb, basePos, dir, across, len, len * widthK, len * (0.16f + 0.12f * R()), segs, FoliageAtlas.Needle, bc, tc, axisC, flutter);
            }
        }
    }

    // 멀리 보는 나무: 실루엣 카드 2장을 십자로(삼각형 4개). 수관을 감싸는 둥근 법선
    static void FarCards(MB mb, Rect uv, Vector3 center, float halfW, float halfH, float az, Color col)
    {
        for (int k = 0; k < 2; k++)
        {
            float a = az + k * Mathf.PI * 0.5f;
            var right = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Card(mb, center, right * halfW, Vector3.up * halfH, uv, col, center - Vector3.up * halfH * 0.2f, 0f, 0.75f);
        }
    }

    // 침엽수(전나무형): 줄기(사진 껍질) + 층층이 처진 가지 카드(사진 전나무 잔가지) + 안쪽 어두운 원뿔(틈 메움)
    //  LOD 0·1·2 = 같은 층 구조에 가지 수만 줄임, LOD 3 = 사진 가지로 만든 나무 실루엣 카드 십자(삼각형 4개)
    static Mesh Conifer(int lod, int seed)
    {
        var rng = new System.Random(seed * 7919 + 303);
        float R() => (float)rng.NextDouble();
        var mb = new MB();
        var barkT = new Color(0.85f, 0.78f, 0.72f);
        float h = 11f + 5f * R();
        float baseR = 3.0f + 0.6f * R();
        var leafDark = new Color(0.05f, 0.13f, 0.08f);
        var leafMid = Color.Lerp(new Color(0.11f, 0.24f, 0.12f), new Color(0.14f, 0.27f, 0.10f), R());
        var leafTip = new Color(0.30f, 0.44f, 0.18f);
        var axisC = Vector3.up * h * 0.5f;

        if (lod <= 2)
        {
            Cylinder(mb, Vector3.down * 0.2f, Vector3.up * h * 0.96f, 0.34f, 0.06f, lod == 0 ? 7 : lod == 1 ? 5 : 4,
                     Shade(barkT, 0.6f), barkT, NatureTextures.Bark.Pine);
            // 안쪽 원뿔(어두움): 가지 사이로 하늘이 비치지 않게(가지가 성긴 사진이라 더 중요)
            // 원뿔은 위를 향해 하늘빛을 많이 받으므로 아주 어둡게(나무 속 그늘로 보이게)
            ConeTier(mb, Vector3.up * h * 0.18f, baseR * 0.42f, h * 0.74f, lod == 0 ? 9 : 7, 0.12f, seed, Shade(leafDark, 0.3f), Shade(leafDark, 0.4f));
            int tiers = lod == 0 ? 18 : lod == 1 ? 11 : 7;
            System.Func<float, int> per = lod == 0 ? (t => t < 0.75f ? 9 : 6) : lod == 1 ? (t => t < 0.7f ? 6 : 5) : (t => 4);
            NeedleTiers(mb, R, h * 0.14f, h * 0.94f, tiers, per,
                        t => Mathf.Lerp(baseR * 1.05f, 0.8f, Mathf.Pow(t, 0.8f)), t => Mathf.Lerp(0.3f, 0.08f, t),
                        lod == 2 ? 1.1f : 0.95f, lod == 0 ? 2 : 1, lod == 0 ? 0.08f : 0f, leafDark, leafMid, leafTip, axisC, 0.8f, Vector3.zero);
            // 꼭대기 순
            Frond(mb, Vector3.up * h * 0.9f, Vector3.up, Vector3.right, h * 0.13f, 0.9f, 0f, 1, FoliageAtlas.Needle, leafMid, leafTip, axisC, 0.5f);
        }
        else
            FarCards(mb, FoliageAtlas.ConiferFar, Vector3.up * h * 0.5f, baseR * 1.08f, h * 0.5f, seed * 0.7f, Color.Lerp(leafMid, leafTip, 0.25f));
        return mb.ToMesh($"Tree_Conifer_{seed}_L{lod}");
    }

    // 거목(레드우드): 굵고 붉은 줄기(사진 껍질, 뿌리 버팀), 잎은 위쪽 절반에 짧고 처진 가지 층 + 가지 끝 뭉텅이
    static Mesh Redwood(int lod, int seed)
    {
        var rng = new System.Random(seed * 7919 + 911);
        float R() => (float)rng.NextDouble();
        var mb = new MB();
        float h = 34f + 12f * R();
        float r0 = 1.25f + 0.3f * R();
        // 껍질 사진(메타세쿼이아)을 붉게: 정점 색은 사진 위에 곱함(1 넘으면 밝아짐)
        var barkT = new Color(1.9f, 1.15f, 0.88f);
        var leafDark = new Color(0.05f, 0.13f, 0.08f);
        var leafMid = new Color(0.12f, 0.25f, 0.12f);
        var leafTip = new Color(0.30f, 0.44f, 0.18f);
        var axisC = Vector3.up * h * 0.72f;

        // 줄기: 아래가 확 퍼지고(버팀뿌리) 위로 가늘어짐
        int seg = lod == 0 ? 14 : lod == 1 ? 10 : lod == 2 ? 7 : 5;
        var pts = new List<Vector3>(); var radii = new List<float>();
        float lean = (R() - 0.5f) * 0.6f;
        int rows = lod <= 1 ? 8 : 4;
        for (int i = 0; i < rows; i++)
        {
            float t = (float)i / (rows - 1);
            float y = Mathf.Lerp(-0.4f, h * 0.97f, Mathf.Pow(t, 1.15f));
            pts.Add(new Vector3(lean * t, y, lean * 0.5f * t));
            float flare = 1f + 0.9f * Mathf.Exp(-y / 1.6f);
            radii.Add(Mathf.Lerp(r0, r0 * 0.18f, Mathf.Pow(t, 0.9f)) * flare);
        }
        Tube(mb, pts, radii, seg, (r, u) => Shade(barkT, Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(pts[r].y / 6f))), NatureTextures.Bark.Redwood);

        var leanV = new Vector3(lean, 0f, lean * 0.5f);
        if (lod <= 2)
        {
            ConeTier(mb, Vector3.up * h * 0.5f, r0 * 1.7f, h * 0.48f, lod == 0 ? 9 : 7, 0.14f, seed, Shade(leafDark, 0.3f), Shade(leafDark, 0.4f));
            int tiers = lod == 0 ? 14 : lod == 1 ? 8 : 5;
            System.Func<float, int> per = lod == 0 ? (t => 7) : lod == 1 ? (t => 5) : (t => 4);
            NeedleTiers(mb, R, h * 0.45f, h * 0.97f, tiers, per, t => Mathf.Lerp(6f, 1.8f, t), t => Mathf.Lerp(r0 * 0.6f, r0 * 0.2f, t),
                        lod == 2 ? 0.95f : 0.8f, lod == 0 ? 2 : 1, lod == 2 ? 0f : 0.2f, leafDark, leafMid, leafTip, axisC, 0.5f, leanV);
            // 가지 끝 잎 뭉텅이(Zwift 거목처럼 수관이 덩어리져 보이게)
            if (lod == 0)
                for (int i = 0; i < 14; i++)
                {
                    float t = R();
                    float az = R() * Mathf.PI * 2f;
                    float y = Mathf.Lerp(h * 0.5f, h * 0.93f, t);
                    var c = leanV * (y / h) + new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)) * Mathf.Lerp(4.5f, 1.5f, t) + Vector3.up * y;
                    LeafCards(mb, c, 1.4f, 4, 2.8f, R, leafMid, leafTip, axisC, h * 0.3f, 0.5f, FoliageAtlas.Needle);
                }
        }
        else
            FarCards(mb, FoliageAtlas.ConiferFar, leanV * 0.72f + Vector3.up * h * 0.71f, 5.4f, h * 0.29f, seed * 0.7f, Color.Lerp(leafMid, leafTip, 0.2f));
        return mb.ToMesh($"Tree_Redwood_{seed}_L{lod}");
    }

    // 야자: 휜 줄기(마디 띠) + 아치형으로 늘어진 깃털잎
    static Mesh Palm(int lod, int seed)
    {
        var rng = new System.Random(seed * 7919 + 517);
        float R() => (float)rng.NextDouble();
        var mb = new MB();
        float h = 7.5f + 3f * R();
        var bendDir = new Vector3(R() - 0.5f, 0f, R() - 0.5f).normalized * (1.2f + 1.2f * R());
        int rows = lod <= 1 ? 9 : 4;
        var pts = new List<Vector3>(); var radii = new List<float>();
        for (int i = 0; i < rows; i++)
        {
            float t = (float)i / (rows - 1);
            pts.Add(Vector3.up * (h * t) + bendDir * (t * t));
            radii.Add(Mathf.Lerp(0.3f, 0.17f, t) * (i == 0 ? 1.3f : 1f));
        }
        var ringA = new Color(1f, 0.95f, 0.88f); var ringB = new Color(0.82f, 0.77f, 0.7f);
        Tube(mb, pts, radii, lod == 0 ? 7 : 5, (r, u) => r % 2 == 0 ? ringA : ringB, NatureTextures.Bark.Palm);
        var crownPos = pts[^1];

        var leafBase = new Color(0.22f, 0.42f, 0.14f); var leafTip = new Color(0.52f, 0.66f, 0.22f);
        int fronds = lod == 0 ? 11 : lod == 1 ? 8 : lod == 2 ? 6 : 4;
        int segs = lod == 0 ? 4 : lod == 1 ? 3 : 2;
        for (int k = 0; k < fronds; k++)
        {
            float az = (k + R() * 0.4f) / fronds * Mathf.PI * 2f;
            float rise = 0.25f + 0.45f * R();
            var dir = new Vector3(Mathf.Cos(az), rise, Mathf.Sin(az)).normalized;
            var across = new Vector3(-Mathf.Sin(az), 0f, Mathf.Cos(az));
            float len = 3.6f + 1.2f * R();
            Frond(mb, crownPos, dir, across, len, 1.4f, len * (0.55f + 0.3f * R()), segs, FoliageAtlas.Frond, Shade(leafBase, 0.8f), leafTip, crownPos + Vector3.down * 1.5f, 1f);
        }
        if (lod == 0)
            for (int i = 0; i < 4; i++)
            {
                var c = crownPos + new Vector3(Mathf.Cos(i * 1.7f), -0.35f, Mathf.Sin(i * 1.7f)) * 0.32f;
                Blob(mb, c, Vector3.one * 0.18f, 0, 0.1f, i, _ => new Color(0.42f, 0.32f, 0.16f));
            }
        return mb.ToMesh($"Tree_Palm_{seed}_L{lod}");
    }

    // ---------------- 덤불·꽃 ----------------
    // 덩어리 3~5개(모든 LOD 같은 자리) + 잎 카드 + 꽃(작은 꽃송이)
    public static Mesh Bush(Color leaf, Color? flower, int lod, int seed)
    {
        var rng = new System.Random(seed * 131 + 7);
        float R() => (float)rng.NextDouble();
        var mb = new MB();
        var leafTop = Color.Lerp(leaf, new Color(0.85f, 0.95f, 0.5f), 0.35f);
        var center = Vector3.up * 0.55f;
        var blobs = new List<(Vector3 c, float r)>();
        int n = 3 + (int)(R() * 2.5f);
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f + R();
            blobs.Add((new Vector3(Mathf.Cos(a) * 0.5f, 0.5f + 0.3f * R(), Mathf.Sin(a) * 0.5f), 0.55f + 0.3f * R()));
        }
        blobs.Add((Vector3.up * 0.85f, 0.6f));
        var dark = Shade(leaf, 0.62f);
        var light = flower.HasValue ? Color.Lerp(flower.Value, Color.white, 0.25f) : leafTop;
        foreach (var (c, r) in blobs)
        {
            if (lod <= 1)
            {
                // 안쪽 덩어리는 작고 어둡게(카드 사이 틈만 메움, 각진 면이 겉으로 드러나지 않게)
                Blob(mb, c, new Vector3(r, r * 0.8f, r) * 0.62f, lod == 0 ? 1 : 0, 0.2f, seed + c.x * 9f,
                     d => Color.Lerp(Shade(dark, 0.4f), Shade(dark, 0.55f), Mathf.Clamp01(d.y * 0.5f + 0.5f)), center, 0.8f);
                LeafCards(mb, c, r, lod == 0 ? 10 : 5, r * (lod == 0 ? 1.25f : 1.6f), R, leaf, leafTop, center, 1.2f, 1f, FoliageAtlas.Leaf);
            }
            else
            {
                var baseCol = flower.HasValue ? Color.Lerp(leaf, flower.Value, 0.3f) : leaf;
                Blob(mb, c, new Vector3(r, r * 0.82f, r) * 1.05f, 0, 0.28f, seed + c.x * 9f,
                     d => Color.Lerp(Shade(baseCol, 0.7f), Shade(leafTop, 0.85f), Mathf.Clamp01(d.y * 0.5f + 0.5f)), center, 0.7f);
            }
        }
        if (flower.HasValue && lod <= 1)
        {
            int count = lod == 0 ? 16 : 9;
            float size = lod == 0 ? 0.13f : 0.2f;
            for (int i = 0; i < count; i++)
            {
                var d = RandomDir(R); d.y = Mathf.Abs(d.y) * 0.8f + 0.25f; d.Normalize();
                var p = center + Vector3.Scale(d, new Vector3(1.0f, 0.7f, 1.0f)) + Vector3.up * 0.1f;
                Blob(mb, p, Vector3.one * (size * (0.8f + 0.4f * R())), 0, 0.1f, i, _ => Color.Lerp(flower.Value, light, R()));
            }
        }
        return mb.ToMesh($"Bush_{seed}_L{lod}");
    }

    // 꽃대 무더기(라벤더처럼 세로로 긴 꽃). 모든 LOD 같은 꽃대 자리, 멀어지면 줄기·잎 뭉치만 생략
    public static Mesh FlowerSpikes(Color flower, int lod, int seed)
    {
        var rng = new System.Random(seed * 977 + 3);
        float R() => (float)rng.NextDouble();
        var mb = new MB();
        var stem = new Color(0.26f, 0.42f, 0.16f);
        var light = Color.Lerp(flower, Color.white, 0.3f);
        if (lod == 2)
        {
            // 멀리(꽃밭): 꽃 색 덩어리 하나(삼각형 20개) — 언덕 위 색 얼룩으로 보임
            Blob(mb, Vector3.up * 0.4f, new Vector3(0.95f, 0.3f, 0.95f), 0, 0.35f, seed,
                 d => Color.Lerp(Color.Lerp(stem, flower, 0.55f), Color.Lerp(flower, light, 0.3f), Mathf.Clamp01(d.y * 0.5f + 0.5f)), Vector3.zero, 0.6f);
            return mb.ToMesh($"Flower_{seed}_L{lod}");
        }
        for (int i = 0; i < 11; i++)
        {
            float a = R() * Mathf.PI * 2f, rr = Mathf.Sqrt(R()) * 0.75f;
            var basePos = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
            float h = 0.55f + 0.45f * R();
            var tip = basePos + new Vector3((R() - 0.5f) * 0.2f, h, (R() - 0.5f) * 0.2f);
            var col = Color.Lerp(flower, light, R() * 0.6f);
            if (lod == 0) Cylinder(mb, basePos, tip, 0.025f, 0.02f, 3, Shade(stem, 0.7f), stem);
            Blob(mb, tip + Vector3.up * 0.12f, new Vector3(0.09f, 0.24f, 0.09f), 0, 0.15f, i, d => Color.Lerp(Shade(col, 0.8f), col, d.y * 0.5f + 0.5f));
        }
        Blob(mb, Vector3.up * 0.18f, new Vector3(0.75f, 0.28f, 0.75f), lod == 0 ? 1 : 0, 0.3f, seed, d => Color.Lerp(Shade(stem, 0.7f), stem * 1.2f, d.y * 0.5f + 0.5f), Vector3.zero, 0.5f);
        return mb.ToMesh($"Flower_{seed}_L{lod}");
    }

    // 고사리: 가운데에서 아치형으로 뻗는 깃털잎 7~9장(숲 바닥)
    public static Mesh Fern(int lod, int seed)
    {
        var rng = new System.Random(seed * 613 + 11);
        float R() => (float)rng.NextDouble();
        var mb = new MB();
        var baseC = new Color(0.18f, 0.36f, 0.12f); var tipC = new Color(0.46f, 0.64f, 0.22f);
        int n = lod == 0 ? 9 : lod == 1 ? 6 : 4;
        for (int k = 0; k < n; k++)
        {
            float az = (k + R() * 0.5f) / n * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(az), 0.9f + 0.4f * R(), Mathf.Sin(az)).normalized;
            var across = new Vector3(-Mathf.Sin(az), 0f, Mathf.Cos(az));
            float len = (0.9f + 0.4f * R()) * (lod == 2 ? 1.1f : 1f);
            Frond(mb, Vector3.zero, dir, across, len, len * 0.42f, len * 0.6f, lod == 0 ? 3 : 2, FoliageAtlas.Frond, baseC, tipC, Vector3.down * 0.5f, 1f);
        }
        return mb.ToMesh($"Fern_{seed}_L{lod}");
    }

    // ---------------- 바위 ----------------
    // 깨진 화강암 덩어리(Zwift 바위처럼): 울퉁불퉁한 구를 평면 몇 개로 깎아 넓고 평평한 쪼개진 면 + 날카로운 모서리.
    // 법선은 각도 35° 이내 이웃 면끼리만 부드럽게(쪼개진 면은 평평, 모서리는 또렷). 결·이끼는 셰이더 3면 투영(사진 바위)이 입힘.
    // LOD마다 구 세분만 줄이고 같은 깎기 평면을 써서 멀어져도 모양이 그대로.
    public static Mesh Rock(int lod, int seed, Vector3 scale, Color tint)
    {
        var mb = new MB();
        var rng = new System.Random(seed * 4111 + 17);
        float R() => (float)rng.NextDouble();
        var (uv, ut) = Ico(lod == 0 ? 3 : lod == 1 ? 2 : 1);

        // 깎기 평면: 옆면 4~6개 + 비스듬한 윗면 1~2개(바위 위가 평평한 판)
        var cuts = new List<(Vector3 n, float d)>();
        int sides = 4 + (int)(R() * 3f);
        float az0 = R() * Mathf.PI * 2f;
        for (int k = 0; k < sides; k++)
        {
            float az = az0 + (k + (R() - 0.5f) * 0.6f) / sides * Mathf.PI * 2f;
            float el = (R() - 0.35f) * 0.9f;
            cuts.Add((new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el)), 0.62f + 0.22f * R()));
        }
        int tops = 1 + (R() < 0.5f ? 1 : 0);
        for (int k = 0; k < tops; k++)
        {
            float az = R() * Mathf.PI * 2f, tilt = 0.15f + 0.45f * R();
            cuts.Add((new Vector3(Mathf.Cos(az) * tilt, 1f, Mathf.Sin(az) * tilt).normalized, 0.5f + 0.25f * R()));
        }

        var pos = new Vector3[uv.Length];
        for (int i = 0; i < uv.Length; i++)
        {
            var d = uv[i];
            // 큰 굴곡(덩어리 모양) + 잔 굴곡(깎이지 않은 면이 매끈한 공처럼 보이지 않게)
            float r = 1f + 0.22f * (Noise3(d * 1.6f + Vector3.one * seed) - 0.5f) * 2f + 0.07f * (Noise3(d * 5.3f + Vector3.one * (seed + 9)) - 0.5f) * 2f;
            var p = d * r;
            foreach (var (n, cd) in cuts)
            {
                float over = Vector3.Dot(p, n) - cd;
                if (over > 0f) p -= n * over;
            }
            p = Vector3.Scale(p, scale);
            if (p.y < -scale.y * 0.35f) p.y = -scale.y * 0.35f + (p.y + scale.y * 0.35f) * 0.15f;   // 바닥은 납작
            pos[i] = Vector3.up * scale.y * 0.32f + p;
        }

        // 면 법선 → 꼭짓점마다 비슷한 방향의 이웃 면만 평균
        int triCount = ut.Length / 3;
        var fn = new Vector3[triCount];
        var around = new List<int>[uv.Length];
        for (int t = 0; t < triCount; t++)
        {
            int a = ut[t * 3], b = ut[t * 3 + 1], c = ut[t * 3 + 2];
            var n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            var center = (pos[a] + pos[b] + pos[c]) / 3f - Vector3.up * scale.y * 0.32f;
            if (Vector3.Dot(n, center) < 0f) n = -n;
            fn[t] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            foreach (int v in new[] { a, b, c }) (around[v] ??= new List<int>()).Add(t);
        }
        float cosLimit = Mathf.Cos(35f * Mathf.Deg2Rad);
        var dark = Shade(tint, 0.62f);
        for (int t = 0; t < triCount; t++)
        {
            int a = ut[t * 3], b = ut[t * 3 + 1], c = ut[t * 3 + 2];
            var corners = new[] { a, b, c };
            // 감기 방향: 바깥이 앞면
            var raw = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            if (Vector3.Dot(raw, fn[t]) < 0f) (corners[1], corners[2]) = (corners[2], corners[1]);
            int s = mb.V.Count;
            foreach (int v in corners)
            {
                var n = Vector3.zero;
                foreach (int o in around[v]) if (Vector3.Dot(fn[o], fn[t]) > cosLimit) n += fn[o];
                n = n.sqrMagnitude > 1e-12f ? n.normalized : fn[t];
                // 색: 땅에 가까울수록 어둡게(흙·그늘), 위쪽은 약간 밝게
                var local = pos[v] - Vector3.up * scale.y * 0.32f;
                float h01 = Mathf.Clamp01((pos[v].y) / Mathf.Max(0.3f, scale.y * 1.2f));
                float tone = 0.9f + 0.2f * Noise3(local * 0.7f + Vector3.one * seed);
                var col = Color.Lerp(dark, tint, Mathf.SmoothStep(0f, 1f, h01 * 1.6f)) * tone;
                col.a = 1f;
                mb.Add(pos[v], n, col);
            }
            mb.Tri(s, s + 1, s + 2);
        }
        return mb.ToMesh($"Rock_{seed}_L{lod}");
    }

    // ---------------- 풀 ----------------
    public static Mesh GrassClump(int blades, float minH, float maxH, Color root, Color tip, int seed)
    {
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        var mb = new MB();
        for (int b = 0; b < blades; b++)
        {
            float a = R() * Mathf.PI * 2f, r = R() * 0.22f;
            var basePos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            float face = R() * Mathf.PI * 2f;
            var side = new Vector3(Mathf.Cos(face), 0f, Mathf.Sin(face));
            var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.15f + 0.3f * R());
            float h = Mathf.Lerp(minH, maxH, R()), w = 0.035f + 0.025f * R();
            var mid = basePos + Vector3.up * h * 0.55f + lean * h * 0.25f;
            var top = basePos + Vector3.up * h + lean * h * 0.8f;
            float hue = 0.88f + 0.24f * R();
            var tipC = Shade(tip, hue); var rootC = Shade(root, hue);
            var n = (Vector3.up + lean * 0.5f).normalized;   // 위쪽 법선 → 풀밭이 부드럽게 밝음
            int i0 = mb.Add(basePos - side * w, n, rootC);
            mb.Add(basePos + side * w, n, rootC);
            mb.Add(mid - side * w * 0.7f, n, Color.Lerp(rootC, tipC, 0.6f));
            mb.Add(mid + side * w * 0.7f, n, Color.Lerp(rootC, tipC, 0.6f));
            mb.Add(top, n, tipC);
            mb.Tri(i0, i0 + 2, i0 + 1); mb.Tri(i0 + 1, i0 + 2, i0 + 3); mb.Tri(i0 + 2, i0 + 4, i0 + 3);
        }
        return mb.ToMesh($"Grass_{seed}");
    }
}
