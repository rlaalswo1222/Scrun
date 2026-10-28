using UnityEditor;
using UnityEngine;

// 씬 빌더들이 함께 쓰는 에셋 생성 도구
public static class ScrunEditorUtil
{
    public const string Root = "Assets/Scrun";

    // 소품·랜드마크 재질. 빛나는 것(emission)만 URP Lit, 나머지는 섬 전체와 같은 손그림풍 셰이더(Scrun/Stylized:
    // 빛 감싸기·그늘 푸른 기운·해 방향 산란 안개)로 → 실사처럼 번들거리는 소품이 섞여 튀지 않게. smoothness는 Lit일 때만
    public static Material MakeMat(string name, Color color, Texture2D tex = null, Vector2 tiling = default,
                                   float smoothness = 0.1f, Color? emission = null)
    {
        EnsureFolder(Root, "Materials");
        string path = $"{Root}/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!emission.HasValue)
        {
            var sty = Shader.Find("Scrun/Stylized");
            if (!mat) { mat = new Material(sty); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = sty;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", tex ? tiling : Vector2.one);
            mat.SetFloat("_Wrap", 0.4f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetTexture("_BaseMap", tex);
        mat.SetTextureScale("_BaseMap", tex ? tiling : Vector2.one);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public enum TexKind { Color, Linear, Normal }

    // 절차 생성 텍스처를 PNG로 저장해 가져온다 (반복·밉맵·이방성 필터)
    public static Texture2D MakeTexture(string name, int w, int h, System.Func<float, float, Color> pixel,
                                        TexKind kind = TexKind.Color)
    {
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / h);
        return SaveTexture(name, w, h, pixels, kind);
    }

    public static Texture2D SaveTexture(string name, int w, int h, Color[] pixels, TexKind kind = TexKind.Color)
    {
        EnsureFolder(Root, "Textures");
        string path = $"{Root}/Textures/{name}.png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, kind != TexKind.Color);
        tex.SetPixels(pixels);
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = kind == TexKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = kind == TexKind.Color;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = 8;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // 원본 이미지 파일(jpg/png)을 가져오기 설정과 무관하게 그대로 읽는다(노멀맵도 RGB 그대로)
    public class RawImage
    {
        public int W, H;
        public Color[] Px;

        public static RawImage Load(string assetPath)
        {
            string full = System.IO.Path.Combine(Application.dataPath, "..", assetPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(System.IO.File.ReadAllBytes(full));
            var img = new RawImage { W = tex.width, H = tex.height, Px = tex.GetPixels() };
            Object.DestroyImmediate(tex);
            return img;
        }

        // 반복(wrap) 쌍선형 샘플
        public Color Sample(float u, float v)
        {
            float x = Mathf.Repeat(u, 1f) * W - 0.5f, y = Mathf.Repeat(v, 1f) * H - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Color P(int xi, int yi) => Px[((yi % H + H) % H) * W + ((xi % W + W) % W)];
            return Color.Lerp(Color.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Color.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
        }

        public Color Average()
        {
            Color s = Color.clear;
            for (int i = 0; i < Px.Length; i += 7) s += Px[i];
            return s / Mathf.Ceil(Px.Length / 7f);
        }
    }

    public static void SetNormalMap(Material mat, Texture2D normal, float scale = 1f)
    {
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", scale);
        if (normal) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
    }

    // 잎·꽃: 알파 잘라내기 + 양면
    public static void SetCutoutDoubleSided(Material mat, float cutoff = 0.45f)
    {
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", cutoff);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cull", 0f);
        mat.doubleSidedGI = true;
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
    }

    // 생성한 메쉬를 에셋으로 저장 (있으면 내용 교체 → 참조 유지)
    public static Mesh SaveMesh(Mesh mesh, string folder, string name)
    {
        EnsureFolderPath(folder);
        string path = $"{folder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            // CopySerialized는 GPU에 올라간 옛 메쉬를 갱신하지 않아 화면에 옛 모양이 남는다 → Mesh API로 다시 채움
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices);
            if (mesh.normals.Length > 0) existing.SetNormals(mesh.normals);
            if (mesh.tangents.Length > 0) existing.SetTangents(mesh.tangents);
            if (mesh.uv.Length > 0) existing.SetUVs(0, mesh.uv);
            if (mesh.uv2.Length > 0) existing.SetUVs(1, mesh.uv2);
            if (mesh.colors.Length > 0) existing.SetColors(mesh.colors);
            if (mesh.boneWeights.Length > 0) { existing.boneWeights = mesh.boneWeights; existing.bindposes = mesh.bindposes; }
            existing.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s);
            existing.RecalculateBounds();
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // 타일링되는 노이즈(텍스처 이음매 없음): u,v ∈ [0,1)
    public static float TileNoise(float u, float v, int period, float seed = 0f)
    {
        float x = u * period, y = v * period;
        float a = Mathf.PerlinNoise(x + seed, y + seed);
        float b = Mathf.PerlinNoise(x - period + seed, y + seed);
        float c = Mathf.PerlinNoise(x + seed, y - period + seed);
        float d = Mathf.PerlinNoise(x - period + seed, y - period + seed);
        float fu = u, fv = v;
        return Mathf.Lerp(Mathf.Lerp(a, b, fu), Mathf.Lerp(c, d, fu), fv);
    }

    public static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void SetFloat(Object target, string field, float value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void EnsureFolder(string parent, string name) => EnsureFolderPath($"{parent}/{name}");

    public static void EnsureFolderPath(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolderPath(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    public static void AddToBuildSettings(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
