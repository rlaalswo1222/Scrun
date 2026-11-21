using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Scrun.EditorTools
{
    // 우주 루프 먼 산(원경 능선) 3겹을 만든다. 루프 전체를 둘러싸는 고리이고, 각 방향은 가장 가까운 길의 구간 스타일을 따른다.
    // 길에서 380·800·1400m 바깥. 먼 겹일수록 높고 Scrun 안개에 묻혀 거리감이 난다. 구간 뿌리(Zone_Z*) 아래 SL_FarRidges에 넣어 구간 켜고 끄기를 따른다.
    // 메뉴: Scrun/Space Loop/Build Far Ridges
    public static class SpaceLoopFarRidgeBuilder
    {
        const string CenterlinePath = "Assets/Scrun/SpaceLoop/Data/space_loop_centerline.json";
        const string OutDir = "Assets/Scrun/SpaceLoop/Models/";
        const int Columns = 900;
        const int Rows = 7;
        const float ZoneLen = 600f, Blend = 90f;
        static readonly float[] Offsets = { 380f, 800f, 1400f };

        class Style
        {
            public float hMin, hMax, scale, snow = 2f; public bool mesa; public Color bottom, top, snowCol = Color.white;
        }

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        static Style S(float a, float b, float sc, string bot, string top, bool mesa = false, float snow = 2f, string snowCol = "#FFFFFF")
            => new Style { hMin = a, hMax = b, scale = sc, bottom = H(bot), top = H(top), mesa = mesa, snow = snow, snowCol = H(snowCol) };

        // [구간][겹]: 구름 위는 산 없음(구름 바다)
        static Style[][] Styles() => new[]
        {
            new[] { S(0, 0, 300, "#000000", "#000000"), S(0, 0, 300, "#000000", "#000000"), S(0, 0, 300, "#000000", "#000000") },
            new[] { S(45, 110, 260, "#7A3A26", "#C8774A", mesa: true), S(90, 175, 320, "#8E4E38", "#D8946A", mesa: true), S(150, 270, 420, "#A86E5C", "#E4B094") },
            new[] { S(30, 85, 160, "#0B2224", "#1C4440"), S(170, 330, 300, "#1B3049", "#4E6A88", snow: 0.86f, snowCol: "#A9BDD2"), S(260, 470, 420, "#2A4466", "#7890AC", snow: 0.74f, snowCol: "#D2DEEA") },
            new[] { S(70, 150, 170, "#6A82A4", "#DCE8F6", snow: 0.42f, snowCol: "#F4F8FF"), S(160, 340, 260, "#556A8C", "#D6E4F4", snow: 0.5f, snowCol: "#F2F7FF"), S(270, 490, 380, "#7088AE", "#EAF2FC", snow: 0.46f, snowCol: "#F6FAFF") },
            new[] { S(14, 45, 120, "#4E4E50", "#8E8E8C"), S(60, 140, 220, "#5C5C5E", "#A6A6A4"), S(120, 230, 340, "#545456", "#9C9C9A") },
        };

        [MenuItem("Scrun/Space Loop/Build Far Ridges")]
        public static void Build()
        {
            var pts = LoadCenterline();
            if (pts.Count == 0) { Debug.LogError("중심선 없음"); return; }
            var roots = FindZoneRoots();
            if (roots == null) { Debug.LogError("Zone_Z0..Z4 없음"); return; }

            Vector3 c = Vector3.zero; foreach (var p in pts) c += p; c /= pts.Count; c.y = 0;
            // 방향별 길 반지름과 그 지점 거리(s)
            var rRoad = new float[Columns]; var sAt = new float[Columns];
            for (int i = 0; i < Columns; i++)
            {
                float th = i * Mathf.PI * 2 / Columns;
                var u = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
                float best = -1, bs = 0;
                for (int k = 0; k < pts.Count; k++)
                {
                    var v = new Vector2(pts[k].x - c.x, pts[k].z - c.z);
                    float along = Vector2.Dot(v, u);
                    if (along <= 0) continue;
                    float side = Mathf.Abs(v.x * u.y - v.y * u.x);
                    // 이 방향 근처(옆으로 120m 안)의 길 중 가장 바깥
                    if (side < 120 && along > best) { best = along; bs = k * 5f; }
                }
                rRoad[i] = best; sAt[i] = bs;
            }
            // 빈 방향은 이웃으로 채움
            for (int pass = 0; pass < Columns; pass++)
            {
                bool any = false;
                for (int i = 0; i < Columns; i++)
                {
                    if (rRoad[i] >= 0) continue;
                    int a = (i + Columns - 1) % Columns, b = (i + 1) % Columns;
                    if (rRoad[a] >= 0) { rRoad[i] = rRoad[a]; sAt[i] = sAt[a]; any = true; }
                    else if (rRoad[b] >= 0) { rRoad[i] = rRoad[b]; sAt[i] = sAt[b]; any = true; }
                }
                if (!any) break;
            }
            // 반지름을 부드럽게(길이 들쭉날쭉해도 능선은 매끈하게)
            var rs = (float[])rRoad.Clone();
            for (int it = 0; it < 6; it++)
                for (int i = 0; i < Columns; i++)
                    rs[i] = Mathf.Max(rRoad[i], (rs[(i + Columns - 1) % Columns] + rs[i] * 2 + rs[(i + 1) % Columns]) / 4);

            var styles = Styles();
            var matDefault = GetMat("SL_FarRidge", false);
            var matMars = GetMat("SL_FarRidge_Mars", true);
            var matForest = GetMat("SL_FarRidge_Forest", false, 0.42f);

            for (int z = 0; z < 5; z++)
            {
                var old = roots[z].transform.Find("SL_FarRidges");
                if (old) Object.DestroyImmediate(old.gameObject);
            }
            var parents = new Transform[5];
            for (int z = 0; z < 5; z++)
            {
                var g = new GameObject("SL_FarRidges"); g.transform.SetParent(roots[z].transform, false); parents[z] = g.transform;
                foreach (var n in new[] { "SL_Far" })
                {
                    if (z < 2) continue;   // 숲·얼음·달의 옛 원뿔 산은 끔
                    var f = roots[z].transform.Find(n); if (f) f.gameObject.SetActive(false);
                }
            }

            int totalTris = 0;
            for (int layer = 0; layer < Offsets.Length; layer++)
            {
                // 열마다 구간 가중치로 높이·색을 섞음
                var h = new float[Columns]; var cb = new Color[Columns]; var ct = new Color[Columns]; var snow = new float[Columns]; var sc = new Color[Columns];
                var mesaW = new float[Columns]; var dom = new int[Columns]; var R = new float[Columns];
                for (int i = 0; i < Columns; i++)
                {
                    float th = i * Mathf.PI * 2 / Columns;
                    R[i] = rs[i] + Offsets[layer];
                    var w = Weights(sAt[i]);
                    float arc = th * R[i];
                    int dz = 0; float dw = -1;
                    for (int z = 0; z < 5; z++)
                    {
                        if (w[z] <= 0) continue;
                        var st = styles[z][layer];
                        float n = st.mesa ? Mesa(arc / st.scale, layer * 31 + z * 7) : Ridged(arc / st.scale, layer * 31 + z * 7);
                        h[i] += w[z] * Mathf.Lerp(st.hMin, st.hMax, n);
                        cb[i] += w[z] * st.bottom; ct[i] += w[z] * st.top; snow[i] += w[z] * st.snow; sc[i] += w[z] * st.snowCol;
                        mesaW[i] += w[z] * (st.mesa ? 1 : 0);
                        if (w[z] > dw) { dw = w[z]; dz = z; }
                    }
                    dom[i] = dz;
                    if (w[0] > 0.3f) h[i] = 0;   // 구름 위 쪽은 산 없음
                }
                for (int z = 1; z < 5; z++)
                {
                    var mesh = BuildMesh(z, layer, c, R, h, cb, ct, snow, sc, mesaW, dom);
                    if (mesh == null) continue;
                    string path = OutDir + "FarRidge_Z" + z + "_L" + layer + ".asset";
                    var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (ex != null) { EditorUtility.CopySerialized(mesh, ex); mesh = ex; } else AssetDatabase.CreateAsset(mesh, path);
                    var go = new GameObject("FarRidge_L" + layer);
                    go.transform.SetParent(parents[z], false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = z == 1 ? matMars : (z == 2 ? matForest : matDefault);
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    totalTris += mesh.triangles.Length / 3;
                }
            }
            totalTris += BuildForestShore(c, rs, sAt, parents[2]);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(roots[0].scene);
            Debug.Log("먼 산 완료: 삼각형 " + totalTris);
        }

        static float Hash(float n) => Mathf.Repeat(Mathf.Sin(n * 127.1f + 311.7f) * 43758.5453f, 1f);

        // 숲: 호수 건너편 물가의 나무 줄 실루엣(길 + 300m) + 그 아래 낮은 물안개 띠(길 + 285m). 레퍼런스 ref4
        static int BuildForestShore(Vector3 c, float[] rs, float[] sAt, Transform parent)
        {
            const int Fine = 4;              // 나무 모양을 위해 열을 4배로
            const float Off = 300f, MistOff = 285f, Cell = 7f;
            int n = Columns * Fine;
            var v = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
            var mv = new List<Vector3>(); var mcol = new List<Color>(); var mtri = new List<int>();
            Color bot = H("#06171A").linear, top = H("#0E2B2C").linear, rim = H("#1E4A48").linear;
            Color mist = H("#5A8C94");
            int prevT = -1, prevM = -1;
            for (int j = 0; j <= n; j++)
            {
                int jj = j % n; int i = jj / Fine; float fr = (jj % Fine) / (float)Fine;
                int i2 = (i + 1) % Columns;
                float wF = Mathf.Min(Weights(sAt[i])[2], Weights(sAt[i2])[2]);
                if (wF < 0.5f) { prevT = -1; prevM = -1; continue; }
                float th = jj * Mathf.PI * 2 / n;
                var u = new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th));
                float rr = Mathf.Lerp(rs[i], rs[i2], fr);
                float R = rr + Off;
                float x = th * R;
                // 나무 줄: 7m 칸마다 한 그루(침엽 35%), 가끔 큰 나무
                float hh = 9f;
                int k0 = Mathf.FloorToInt(x / Cell);
                for (int k = k0 - 3; k <= k0 + 3; k++)
                {
                    float xc = k * Cell + Hash(k) * 5f;
                    float ht = 13f + 20f * Hash(k + 0.37f) + (Hash(k + 0.71f) > 0.93f ? 16f : 0f);
                    float wd = 4f + 4.5f * Hash(k + 0.13f);
                    float dx = Mathf.Abs(x - xc);
                    if (dx >= wd) continue;
                    float t = dx / wd;
                    float y = Hash(k + 0.53f) < 0.35f ? ht * (1 - t) : ht * (0.72f + 0.28f * Mathf.Sqrt(1 - t * t));
                    hh = Mathf.Max(hh, y);
                }
                hh *= Mathf.SmoothStep(0, 1, (wF - 0.5f) / 0.3f);   // 구간 경계에서 낮아짐
                int s0 = v.Count;
                float[] ys = { -3f, hh * 0.55f, hh };
                for (int r = 0; r < 3; r++)
                {
                    v.Add(c + u * R + Vector3.up * ys[r]);
                    col.Add(r == 0 ? bot : (r == 1 ? top : rim));
                }
                if (prevT >= 0)
                    for (int r = 0; r < 2; r++)
                    {
                        tri.Add(prevT + r); tri.Add(prevT + r + 1); tri.Add(s0 + r);
                        tri.Add(s0 + r); tri.Add(prevT + r + 1); tri.Add(s0 + r + 1);
                    }
                prevT = s0;
                // 물안개: 네 열마다 하나
                if (jj % Fine == 0)
                {
                    int m0 = mv.Count;
                    float mh = 16f + 8f * Mathf.PerlinNoise(x * 0.004f, 1.3f);
                    float a = 0.5f * Mathf.SmoothStep(0, 1, (wF - 0.5f) / 0.3f) * (0.6f + 0.4f * Mathf.PerlinNoise(x * 0.003f, 7.7f));
                    var mu = c + u * (rr + MistOff);
                    mv.Add(mu + Vector3.up * -1.5f); mcol.Add(new Color(mist.r, mist.g, mist.b, a));
                    mv.Add(mu + Vector3.up * (mh * 0.4f)); mcol.Add(new Color(mist.r, mist.g, mist.b, a * 0.7f));
                    mv.Add(mu + Vector3.up * mh); mcol.Add(new Color(mist.r, mist.g, mist.b, 0));
                    if (prevM >= 0)
                        for (int r = 0; r < 2; r++)
                        {
                            mtri.Add(prevM + r); mtri.Add(prevM + r + 1); mtri.Add(m0 + r);
                            mtri.Add(m0 + r); mtri.Add(prevM + r + 1); mtri.Add(m0 + r + 1);
                        }
                    prevM = m0;
                }
            }
            int tris = 0;
            if (tri.Count > 0)
            {
                var m = new Mesh { name = "FarRidge_Z2_Treeline", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v); m.SetColors(col); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
                m = SaveMesh(m, OutDir + "FarRidge_Z2_Treeline.asset");
                var go = new GameObject("FarRidge_Treeline"); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = GetMat("SL_FarRidge_Forest", false, 0.42f);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                tris += tri.Count / 3;
            }
            if (mtri.Count > 0)
            {
                var m = new Mesh { name = "FarRidge_Z2_Mist", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(mv); m.SetColors(mcol); m.SetTriangles(mtri, 0); m.RecalculateBounds();
                m = SaveMesh(m, OutDir + "FarRidge_Z2_Mist.asset");
                var go = new GameObject("FarRidge_Mist"); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = MistMat();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                tris += mtri.Count / 3;
            }
            return tris;
        }

        static Mesh SaveMesh(Mesh m, string path)
        {
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (ex != null) { EditorUtility.CopySerialized(m, ex); return ex; }
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static Material MistMat()
        {
            string path = "Assets/Scrun/SpaceLoop/Materials/SL_Mist.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Scrun/VertexGlowAlpha")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Intensity", 1f);
            m.SetColor("_Color", Color.white);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh BuildMesh(int zone, int layer, Vector3 c, float[] R, float[] h, Color[] cb, Color[] ct, float[] snow, Color[] sc, float[] mesaW, int[] dom)
        {
            var cols = new List<int>();
            for (int i = 0; i < Columns; i++) if (dom[i] == zone && h[i] > 3f) cols.Add(i);
            if (cols.Count == 0) return null;
            var v = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
            var idx = new Dictionary<int, int>();
            System.Func<int, int> colStart = (i) =>
            {
                if (idx.TryGetValue(i, out int s)) return s;
                s = v.Count; idx[i] = s;
                float th = i * Mathf.PI * 2 / Columns;
                var u = new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th));
                float hh = Mathf.Max(h[i], 0f);
                float lean = Mathf.Lerp(0.9f, 0.25f, mesaW[i]);   // 메사는 절벽이 가파름
                for (int r = 0; r < Rows; r++)
                {
                    float t = r / (float)(Rows - 1);
                    float tt = Mathf.Pow(t, 0.85f);
                    float y = Mathf.Lerp(-45f, hh, tt);
                    float jitter = (Mathf.PerlinNoise(i * 0.37f + layer * 9.1f, r * 1.7f) - 0.5f) * 30f * (1 - t * 0.5f);
                    float rad = R[i] + Mathf.Max(0, y) * lean + jitter;
                    v.Add(c + u * rad + Vector3.up * y);
                    float hn = Mathf.Clamp01(y / Mathf.Max(hh, 1f));
                    var k = Color.Lerp(cb[i], ct[i], Mathf.SmoothStep(0, 1, hn));
                    float sn = Mathf.Clamp01((hn - snow[i] + (Mathf.PerlinNoise(i * 0.21f, layer * 3.3f) - 0.5f) * 0.18f) / 0.08f);
                    k = Color.Lerp(k, sc[i], sn);
                    col.Add(k.linear);
                }
                return s;
            };
            for (int n = 0; n < cols.Count; n++)
            {
                int i = cols[n], j = (i + 1) % Columns;
                int a = colStart(i), b = colStart(j);
                for (int r = 0; r < Rows - 1; r++)
                {
                    tri.Add(a + r); tri.Add(a + r + 1); tri.Add(b + r);
                    tri.Add(b + r); tri.Add(a + r + 1); tri.Add(b + r + 1);
                }
            }
            var m = new Mesh { name = "FarRidge_Z" + zone + "_L" + layer, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetColors(col); m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            // 법선이 루프 안쪽(보는 쪽)을 향하게
            var nr = m.normals; float dot = 0;
            for (int k = 0; k < v.Count; k++) dot += Vector3.Dot(nr[k], (c - v[k]).normalized);
            if (dot < 0)
            {
                for (int k = 0; k < tri.Count; k += 3) { int t = tri[k + 1]; tri[k + 1] = tri[k + 2]; tri[k + 2] = t; }
                m.SetTriangles(tri, 0); m.RecalculateNormals();
            }
            m.RecalculateBounds();
            return m;
        }

        static float Ridged(float x, int seed)
        {
            float s = 0, a = 0.55f, f = 1, norm = 0;
            for (int o = 0; o < 4; o++)
            {
                float p = Mathf.PerlinNoise(x * f + seed * 13.1f, seed * 7.7f + o * 3.1f);
                float r = 1 - Mathf.Abs(2 * p - 1);
                s += r * r * a; norm += a; a *= 0.5f; f *= 2.1f;
            }
            return Mathf.Clamp01(s / norm * 1.25f - 0.15f);
        }

        static float Mesa(float x, int seed)
        {
            float p = Mathf.PerlinNoise(x + seed * 13.1f, seed * 7.7f) * 0.8f + Mathf.PerlinNoise(x * 3.3f, seed * 2.2f) * 0.2f;
            float v = Mathf.Clamp01(p) * 3f, f = Mathf.Floor(v), t = v - f;
            return Mathf.Clamp01((f + Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.75f, 1f, t))) / 3f);
        }

        static float[] Weights(float s)
        {
            var w = new float[5]; float tot = 0; float lap = ZoneLen * 5;
            for (int i = 0; i < 5; i++)
            {
                float a = i * ZoneLen, b = a + ZoneLen, best = 0;
                foreach (float off in new[] { -lap, 0f, lap })
                {
                    float x = s + off;
                    float win = Mathf.SmoothStep(0, 1, Mathf.Clamp01((x - (a - Blend)) / (2 * Blend)));
                    float wout = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((x - (b - Blend)) / (2 * Blend)));
                    best = Mathf.Max(best, Mathf.Min(win, wout));
                }
                w[i] = best; tot += best;
            }
            for (int i = 0; i < 5; i++) w[i] /= Mathf.Max(tot, 1e-4f);
            return w;
        }

        static Material GetMat(string name, bool strata, float baseK = 1f)
        {
            string path = "Assets/Scrun/SpaceLoop/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Scrun/Stylized")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_Wrap", 0.55f);
            m.SetFloat("_RimStrength", 0.12f);
            m.SetFloat("_StrataStrength", strata ? 0.45f : 0f);
            m.SetFloat("_StrataScale", 0.07f);
            m.SetColor("_BaseColor", new Color(baseK, baseK, baseK, 1f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static List<Vector3> LoadCenterline()
        {
            var list = new List<Vector3>();
            var txt = System.IO.File.ReadAllText(CenterlinePath);
            int at = txt.IndexOf("\"points\"");
            if (at < 0) return list;
            foreach (Match mt in Regex.Matches(txt.Substring(at), @"\[\s*(-?[\d.eE+-]+)\s*,\s*(-?[\d.eE+-]+)\s*,\s*(-?[\d.eE+-]+)\s*\]"))
            {
                float x = float.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
                float y = float.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture);
                float zz = float.Parse(mt.Groups[3].Value, CultureInfo.InvariantCulture);
                list.Add(new Vector3(-x, zz, -y));   // Blender (x, y, z) → Unity (-x, z, -y)
            }
            return list;
        }

        static GameObject[] FindZoneRoots()
        {
            var r = new GameObject[5];
            foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                for (int z = 0; z < 5; z++)
                    if (go.name.StartsWith("Zone_Z" + z + "_") && go.gameObject.scene.IsValid()) r[z] = go.gameObject;
            foreach (var g in r) if (g == null) return null;
            return r;
        }
    }
}
