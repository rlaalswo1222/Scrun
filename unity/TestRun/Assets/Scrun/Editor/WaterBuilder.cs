using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ScrunEditorUtil;

// Scrun/Water 재질 준비: 지형 높이 지도(수심 계산용)와 잔물결 노멀 텍스처를 굽고, 물마다 재질을 만든다.
public static class WaterBuilder
{
    public class HeightMap
    {
        public Texture2D Texture;
        public Vector4 Rect;    // xMin, zMin, 1/w, 1/h
        public Vector2 Range;   // min, max
    }

    // 지형 높이를 R 채널(0~1 정규화)에 담은 텍스처. 수면 주변 수심만 필요하므로 범위는 -50~+50m로 고정
    public static HeightMap BakeHeights(string name, CourseTerrainBuilder terrain, int res = 1024)
    {
        const float lo = -50f, hi = 50f;
        float half = terrain.Size / 2f;
        float minX = terrain.Center.x - half, minZ = terrain.Center.y - half;
        var px = new Color[res * res];
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float x = minX + (i + 0.5f) / res * terrain.Size, z = minZ + (j + 0.5f) / res * terrain.Size;
                float h = Mathf.InverseLerp(lo, hi, terrain.Height(x, z));
                px[j * res + i] = new Color(h, h, h, 1f);
            }
        var tex = SaveTexture($"Water_{name}_Height", res, res, px, TexKind.Linear);
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.textureFormat = TextureImporterFormat.R16;
        ti.SaveAndReimport();
        return new HeightMap
        {
            Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex)),
            Rect = new Vector4(minX, minZ, 1f / terrain.Size, 1f / terrain.Size),
            Range = new Vector2(lo, hi),
        };
    }

    // 반복되는 잔물결 노멀(높이 노이즈 → 기울기)
    public static Texture2D WaveNormals()
    {
        const int N = 256;
        float H(float u, float v) => TileNoise(u, v, 8, 1f) * 0.6f + TileNoise(u, v, 16, 4f) * 0.3f + TileNoise(u, v, 32, 9f) * 0.1f;
        var px = new Color[N * N];
        float e = 1f / N;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                float dx = (H(Mathf.Repeat(u + e, 1f), v) - H(Mathf.Repeat(u - e, 1f), v)) * 6f;
                float dy = (H(u, Mathf.Repeat(v + e, 1f)) - H(u, Mathf.Repeat(v - e, 1f))) * 6f;
                var n = new Vector3(-dx, -dy, 1f).normalized;
                px[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        return SaveTexture("Water_WaveNormal", N, N, px, TexKind.Normal);
    }

    public static Material Material(string name, HeightMap map, float level, Color shallow, Color deep, float depthRange, float waveScale,
                                    float maxDepth = 100000f, float reflect = 0.4f)
    {
        EnsureFolder(Root, "Materials");
        string path = $"{Root}/Materials/{name}.mat";
        var shader = Shader.Find("Scrun/Water");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        mat.SetTexture("_NormalMap", WaveNormals());
        mat.SetTexture("_HeightMap", map?.Texture);
        mat.SetVector("_HeightRect", map != null ? map.Rect : Vector4.zero);
        mat.SetVector("_HeightRange", map != null ? (Vector4)map.Range : new Vector4(-50, 50, 0, 0));
        mat.SetFloat("_WaterLevel", level);
        mat.SetColor("_ShallowColor", shallow);
        mat.SetColor("_DeepColor", deep);
        mat.SetFloat("_DepthRange", depthRange);
        mat.SetFloat("_WaveScale", waveScale);
        mat.SetFloat("_MaxDepth", maxDepth);
        mat.SetFloat("_Reflect", reflect);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 물 평면(원점 기준 사각형). 버텍스는 네 귀퉁이뿐
    public static GameObject Plane(string name, Vector3 center, float sizeX, float sizeZ, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetPositionAndRotation(center, Quaternion.Euler(90f, 0f, 0f));
        go.transform.localScale = new Vector3(sizeX, sizeZ, 1f);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    public static Color SeaShallow => new(0.22f, 0.78f, 0.80f);
    public static Color SeaDeep => new(0.04f, 0.30f, 0.52f);
    public static Color LakeShallow => new(0.30f, 0.70f, 0.62f);
    public static Color LakeDeep => new(0.07f, 0.32f, 0.42f);
}
