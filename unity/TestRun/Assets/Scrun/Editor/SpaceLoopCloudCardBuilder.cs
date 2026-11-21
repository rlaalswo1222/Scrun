using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Scrun.EditorTools
{
    // 구름 위 구간의 구름 바다를 "구름 카드"로 만든다. 덩어리 메시(SL_Clouds, 5.4만 삼각형) 대신
    // Blender에서 렌더한 부피 구름 이미지(art/map/space_loop/scripts/cloud_sprites.py)를 카메라를 보는 사각형에 입힌다.
    // 카드 전체가 메시 하나·재질 하나(그리기 1번). 겹: 길가(작게, 길 아래) → 중간 → 아래층 → 먼 구름 바다 → 지평선 구름 둑.
    // 반투명이라 화면을 여러 겹 덮으면 아이폰에서 무겁다. 그래서 (1) 카드를 구름 모양에 맞게 자르고(cloud_cells.py의 칸 영역),
    // (2) 길가를 뺀 겹은 흩뿌리지 않고 월드 격자에 고르게 깐다(같은 덮임을 적은 장수로).
    // 메뉴: Scrun/Space Loop/Build Cloud Cards
    public static class SpaceLoopCloudCardBuilder
    {
        const string CenterlinePath = "Assets/Scrun/SpaceLoop/Data/space_loop_centerline.json";
        const string MeshPath = "Assets/Scrun/SpaceLoop/Models/CloudCards_Z0.asset";
        const string MatPath = "Assets/Scrun/SpaceLoop/Materials/SL_CloudCard.mat";
        const string FloorMatPath = "Assets/Scrun/SpaceLoop/Materials/SL_CloudFloor.mat";
        const string TexA = "Assets/Scrun/SpaceLoop/Textures/CloudCards_A.png";
        const string TexB = "Assets/Scrun/SpaceLoop/Textures/CloudCards_B.png";
        const string CellsPath = "Assets/Scrun/SpaceLoop/Textures/CloudCards_cells.json";
        const float Step = 5f;            // 중심선 점 간격(m)
        const float RoadHalf = 4f;
        const float S0 = -60f, S1 = 660f; // 구름을 까는 길 범위(구름 위 구간 0–600m와 양쪽 경계)

        // 아틀라스 칸(4x2): 0 넓은, 1 탑, 2 넓은, 3 작은, 4 탑, 5 넓은, 6 작은, 7 탑
        static readonly int[] Wide = { 0, 2, 5 }, Tower = { 1, 4, 7 }, Puff = { 3, 6 };
        static Vector4[] cellRect;        // 칸별 구름 영역 (xmin, ymin, xmax, ymax), 칸 기준 0-1
        static float Fill(int cell) { return cellRect[cell].z - cellRect[cell].x; }      // 칸 폭 대비 구름 폭
        static float Height(int cell) { return cellRect[cell].w - cellRect[cell].y; }    // 칸 높이 대비 구름 높이
        static float BaseFrac(int cell) { return cellRect[cell].y; }                     // 칸 아래에서 구름 바닥까지

        struct Card { public Vector3 c; public float size; public int cell; public bool flip; public float shade; }

        [MenuItem("Scrun/Space Loop/Build Cloud Cards")]
        public static void Build()
        {
            var pts = LoadCenterline();
            if (pts.Count == 0) { Debug.LogError("중심선 없음"); return; }
            GameObject root = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name.StartsWith("Zone_Z0_") && t.gameObject.scene.IsValid()) root = t.gameObject;
            if (root == null) { Debug.LogError("Zone_Z0 없음"); return; }
            var texA = AssetDatabase.LoadAssetAtPath<Texture2D>(TexA);
            var texB = AssetDatabase.LoadAssetAtPath<Texture2D>(TexB);
            if (texA == null || texB == null) { Debug.LogError("구름 아틀라스 없음: " + TexA); return; }
            SetupTexture(TexA, true); SetupTexture(TexB, false);
            cellRect = LoadCells();
            if (cellRect == null) { Debug.LogError("칸 영역 없음: " + CellsPath + " (cloud_cells.py로 만든다)"); return; }

            // 피할 것: 키트 에셋(건물·플랫폼·비행선)과 옛 관제 기지 자리
            var avoid = new List<Bounds>();
            var kit = root.transform.Find("SL_Kit");
            if (kit != null)
                for (int i = 0; i < kit.childCount; i++)
                {
                    var rs = kit.GetChild(i).GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) continue;
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    avoid.Add(b);
                }
            avoid.Add(new Bounds(At(pts, 300, 30, 0), new Vector3(90, 60, 90)));
            avoid.Add(new Bounds(At(pts, 390, 60, 4), new Vector3(80, 60, 80)));

            var rnd = new System.Random(20261112);
            var cards = new List<Card>();
            System.Func<float, float, float> R = (a, b) => a + (float)rnd.NextDouble() * (b - a);
            System.Func<int[], int> Pick = arr => arr[rnd.Next(arr.Length)];

            // 한 장 놓기. topY = 구름 꼭대기 높이(길 기준). 구름 바다는 길 아래에 깔려야 하므로 꼭대기로 높이를 정한다
            System.Action<Vector3, float, float, float, int, bool> PutAt = (c, roadY, topY, size, cell, checkAvoid) =>
            {
                float baseY = topY - size * Height(cell);
                c.y = roadY + baseY + size * (0.5f - BaseFrac(cell));
                if (checkAvoid)
                {
                    float half = size * Fill(cell) * 0.5f;
                    foreach (var b in avoid)
                    {
                        float dx = Mathf.Max(0, Mathf.Abs(c.x - b.center.x) - b.extents.x);
                        float dz = Mathf.Max(0, Mathf.Abs(c.z - b.center.z) - b.extents.z);
                        if (dx * dx + dz * dz < half * half && roadY + topY > b.min.y - 4 && roadY + baseY < b.max.y + 4) return;
                    }
                }
                cards.Add(new Card { c = c, size = size, cell = cell, flip = rnd.Next(2) == 0, shade = R(0.94f, 1f) });
            };
            System.Func<float> Side = () => rnd.Next(2) == 0 ? -1f : 1f;

            // 1) 길가: 길 아래로 지나가는 작은 구름(꼭대기가 길보다 낮음). 길을 침범하지 않게 폭의 절반만큼 띄움
            for (int i = 0; i < 64; i++)
            {
                int cell = rnd.Next(4) == 0 ? Pick(Puff) : Pick(Wide);
                float size = R(18, 34);
                float d = (RoadHalf + 6 + size * Fill(cell) * 0.5f + R(0, 30)) * Side();
                float s = S0 + (i + R(0, 1)) * (S1 - S0) / 64f;
                PutAt(At(pts, s, d, 0), At(pts, s, 0, 0).y, -R(2.5f, 12), size, cell, true);
            }
            // 구간 경계(달 쪽 0m, 화성 쪽 600m): 구름층을 뚫고 나가는 느낌으로 난간 높이까지 올라옴
            for (int i = 0; i < 24; i++)
            {
                int cell = Pick(Wide);
                float size = R(18, 32);
                float s = i % 2 == 0 ? R(-40, 50) : R(550, 640);
                float d = (RoadHalf + 6 + size * Fill(cell) * 0.5f + R(0, 24)) * Side();
                PutAt(At(pts, s, d, 0), At(pts, s, 0, 0).y, R(-3, 2.5f), size, cell, true);
            }

            // 나머지 겹은 월드 격자(간격 = 그 겹의 구름 크기)에 깐다. 길에서의 거리로 겹을 정하므로 굽은 길 안쪽에서도 뭉치지 않는다
            int i0 = Mathf.RoundToInt(S0 / Step), i1 = Mathf.RoundToInt(S1 / Step);
            var bb = new Bounds(At(pts, 0, 0, 0), Vector3.zero);
            for (int k = i0; k <= i1; k++) bb.Encapsulate(At(pts, k * Step, 0, 0));
            // (겹 이름, 거리 범위, 격자 간격)
            System.Action<float, float, float, System.Action<Vector3, float, float>> Grid = (dMin, dMax, cellSize, place) =>
            {
                for (float x = bb.min.x - dMax; x <= bb.max.x + dMax; x += cellSize)
                    for (float z = bb.min.z - dMax; z <= bb.max.z + dMax; z += cellSize)
                    {
                        var p = new Vector3(x + R(-0.38f, 0.38f) * cellSize, 0, z + R(-0.38f, 0.38f) * cellSize);
                        float best = float.MaxValue; int bk = i0;
                        for (int k = i0; k <= i1; k += 2)
                        {
                            var q = At(pts, k * Step, 0, 0);
                            float dd = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z);
                            if (dd < best) { best = dd; bk = k; }
                        }
                        if (bk <= i0 || bk >= i1 - 1) continue;      // 구간 끝 너머(이웃 구간 위)는 깔지 않음
                        float dist = Mathf.Sqrt(best);
                        if (dist < dMin || dist >= dMax) continue;
                        place(p, At(pts, bk * Step, 0, 0).y, dist);
                    }
            };
            // 2) 중간: 내려다보이는 구름 바다. 꼭대기가 눈높이 아래
            Grid(46, 230, 50, (p, ry, dist) =>
            {
                int k = rnd.Next(10);
                int cell = k < 7 ? Pick(Wide) : (k < 8 ? Pick(Tower) : Pick(Puff));
                float size = Mathf.Lerp(46, 88, (dist - 46) / 184f) * R(0.85f, 1.2f);
                PutAt(p, ry, R(-18, 0.5f), size, cell, true);
            });
            // 2-1) 아래층: 중간 구름 사이 틈으로 내려다보이는 낮은 구름(바닥 판이 그대로 보이지 않게)
            Grid(16, 250, 82, (p, ry, dist) => PutAt(p, ry, R(-30, -20), R(70, 100), Pick(Wide), false));
            // 3) 먼 구름 바다: 멀수록 크게. 여섯 중 하나는 지평선 위로 솟은 탑 구름
            Grid(230, 850, 135, (p, ry, dist) =>
            {
                bool tower = rnd.Next(6) == 0;
                float k = (dist - 230) / 620f;
                float size = tower ? R(150, 250) : Mathf.Lerp(120, 250, k) * R(0.85f, 1.15f);
                float top = tower ? R(25, 75) : Mathf.Lerp(R(-12, 6), R(-4, 18), k);
                PutAt(p, ry, top, size, tower ? Pick(Tower) : Pick(Wide), false);
            });
            // 4) 지평선 구름 둑
            Grid(850, 1600, 330, (p, ry, dist) =>
            {
                bool tower = rnd.Next(3) == 0;
                PutAt(p, ry, tower ? R(80, 170) : R(10, 60), tower ? R(300, 440) : R(340, 480), tower ? Pick(Tower) : Pick(Wide), false);
            });

            // 메시: 카드 1장 = 정점 4개(위치는 전부 카드 중심, 모서리는 uv0로)
            int n = cards.Count;
            var v = new Vector3[n * 4]; var uv0 = new List<Vector4>(n * 4); var uv1 = new List<Vector4>(n * 4);
            var col = new Color[n * 4]; var tris = new int[n * 6];
            var bounds = new Bounds(cards[0].c, Vector3.zero);
            for (int i = 0; i < n; i++)
            {
                var cd = cards[i];
                // 모서리는 칸 중심 기준(-0.5~0.5)이고, 구름이 있는 영역까지만 잘라 쓴다
                var rc = cellRect[cd.cell];
                var corners = new[] { new Vector2(rc.x - 0.5f, rc.y - 0.5f), new Vector2(rc.z - 0.5f, rc.y - 0.5f), new Vector2(rc.z - 0.5f, rc.w - 0.5f), new Vector2(rc.x - 0.5f, rc.w - 0.5f) };
                for (int k = 0; k < 4; k++)
                {
                    v[i * 4 + k] = cd.c;
                    uv0.Add(new Vector4(corners[k].x, corners[k].y, cd.size, cd.size));
                    uv1.Add(new Vector4(cd.cell % 4, 1 - cd.cell / 4, cd.flip ? -1 : 1, 0));   // 아틀라스 윗줄이 v 큰 쪽
                    col[i * 4 + k] = new Color(cd.shade, cd.shade, cd.shade, 1);
                }
                int b = i * 4, t = i * 6;
                tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1; tris[t + 3] = b; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
                bounds.Encapsulate(new Bounds(cd.c, Vector3.one * cd.size * 1.5f));
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, MeshPath); }
            mesh.Clear();
            mesh.name = "CloudCards_Z0";
            mesh.vertices = v; mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.colors = col; mesh.triangles = tris;
            mesh.bounds = bounds;
            EditorUtility.SetDirty(mesh);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null) { mat = new Material(Shader.Find("Scrun/CloudCard")); AssetDatabase.CreateAsset(mat, MatPath); }
            mat.shader = Shader.Find("Scrun/CloudCard");
            mat.SetTexture("_MapA", AssetDatabase.LoadAssetAtPath<Texture2D>(TexA));
            mat.SetTexture("_MapB", AssetDatabase.LoadAssetAtPath<Texture2D>(TexB));
            mat.SetVector("_Cells", new Vector4(4, 2, 0, 0));
            mat.SetFloat("_SunBoost", 1.25f); mat.SetFloat("_AmbTop", 0.62f); mat.SetFloat("_AmbBottom", 0.3f);
            mat.SetColor("_AmbTint", new Color(0.86f, 0.93f, 1.1f, 1f));
            EditorUtility.SetDirty(mat);

            var tr = root.transform.Find("SL_CloudCards");
            GameObject go = tr ? tr.gameObject : new GameObject("SL_CloudCards");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var mf = go.GetComponent<MeshFilter>(); if (!mf) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>(); if (!mr) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh; mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            if (!go.GetComponent<SpaceLoopCloudCards>()) go.AddComponent<SpaceLoopCloudCards>();

            // 구름 바닥 판: 흰 평면이 그대로 보이면 눈밭 같아서, 위 구름의 그늘에 잠긴 아래층 색으로 낮춘다
            var floorTr = root.transform.Find("SL_CloudFloor");
            if (floorTr)
            {
                var fm = AssetDatabase.LoadAssetAtPath<Material>(FloorMatPath);
                if (fm == null)
                {
                    fm = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Scrun/SpaceLoop/Materials/SL_Cloud.mat"));
                    AssetDatabase.CreateAsset(fm, FloorMatPath);
                }
                fm.SetColor("_BaseColor", new Color(0.70f, 0.76f, 0.88f, 1f));
                fm.SetFloat("_RimStrength", 0f);
                EditorUtility.SetDirty(fm);
                floorTr.GetComponent<Renderer>().sharedMaterial = fm;
            }

            // 옛 덩어리 구름은 숨김(지우지 않음). 구름 바닥(SL_CloudFloor)은 틈 메우기로 남김
            var old = root.transform.Find("SL_Clouds");
            if (old) old.gameObject.SetActive(false);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("구름 카드 " + n + "장 (삼각형 " + n * 2 + "), 피한 자리 " + avoid.Count + "곳");
        }

        static void SetupTexture(string path, bool alpha)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return;
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = false;   // 색이 이미 알파가 곱해진 값이라 테두리 보정을 하면 안 됨
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = 2048;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
        }

        static Vector4[] LoadCells()
        {
            if (!System.IO.File.Exists(CellsPath)) return null;
            var txt = System.IO.File.ReadAllText(CellsPath);
            int at = txt.IndexOf("\"cells\"");
            if (at < 0) return null;
            var list = new List<Vector4>();
            foreach (Match mt in Regex.Matches(txt.Substring(at), @"\[\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\]"))
                list.Add(new Vector4(float.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture),
                    float.Parse(mt.Groups[3].Value, CultureInfo.InvariantCulture), float.Parse(mt.Groups[4].Value, CultureInfo.InvariantCulture)));
            return list.Count >= 8 ? list.ToArray() : null;
        }

        // 길 위 거리 s, 옆으로 d(양수 = Blender 기준 왼쪽), 높이 z
        static Vector3 At(List<Vector3> pts, float s, float d, float z)
        {
            int n = pts.Count;
            float f = s / Step; int i0 = Mathf.FloorToInt(f); float t = f - i0;
            i0 = ((i0 % n) + n) % n; int i1 = (i0 + 1) % n;
            var p = Vector3.Lerp(pts[i0], pts[i1], t);
            var tan = pts[i1] - pts[i0]; tan.y = 0; tan.Normalize();
            var left = new Vector3(-tan.z, 0, tan.x);
            return p + left * d + Vector3.up * z;
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
    }
}
