using Scrun;
using UnityEditor;
using UnityEngine;
using static ScrunEditorUtil;

// 손그림풍 하늘(위경도 파노라마 텍스처 1장 + Skybox/Panoramic) 생성과 시간대 프리셋 구성.
// 뭉게구름은 하늘 위 평면에 노이즈를 투영해 그리고, 해 쪽은 밝게·반대쪽 아래는 그늘지게 칠한다.
public static class SkyBuilder
{
    public struct Palette
    {
        public Color Zenith, Horizon, Sun, CloudLit, CloudShade;
        public float Coverage;   // 0~1 구름 양
    }

    public static Palette Day => new()
    {
        Zenith = new Color(0.16f, 0.40f, 0.82f), Horizon = new Color(0.68f, 0.82f, 0.95f), Sun = new Color(1f, 0.96f, 0.86f),
        CloudLit = new Color(1f, 1f, 1f), CloudShade = new Color(0.62f, 0.69f, 0.82f), Coverage = 0.42f,
    };

    public static Palette Sunset => new()
    {
        Zenith = new Color(0.22f, 0.30f, 0.56f), Horizon = new Color(1.0f, 0.66f, 0.38f), Sun = new Color(1f, 0.78f, 0.5f),
        CloudLit = new Color(1f, 0.72f, 0.52f), CloudShade = new Color(0.46f, 0.38f, 0.52f), Coverage = 0.38f,
    };

    // Skybox/Panoramic 좌표와 같은 방향 변환(u: 경도, v: 위도)
    static Vector3 Dir(float u, float v)
    {
        float lon = (0.5f - u) * Mathf.PI * 2f, lat = (1f - v) * Mathf.PI;
        return new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
    }

    static float Fbm(float x, float y, int oct)
    {
        float s = 0, a = 0.5f, f = 1, n = 0;
        for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x * f + i * 17.3f, y * f - i * 9.1f); n += a; a *= 0.5f; f *= 2.1f; }
        return s / n;
    }

    public static Material Make(string name, Palette pal, Vector3 towardSun, int seed)
    {
        const int W = 2048, H = 1024;
        var px = new Color[W * H];
        towardSun.Normalize();
        var sunFlat = new Vector2(towardSun.x, towardSun.z).normalized;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                var d = Dir(u, v);
                float up = Mathf.Max(d.y, 0f);
                var c = Color.Lerp(pal.Horizon, pal.Zenith, Mathf.Pow(up, 0.55f));
                float sd = Mathf.Max(0f, Vector3.Dot(d, towardSun));
                c += pal.Sun * (Mathf.Pow(sd, 900f) * 3f + Mathf.Pow(sd, 24f) * 0.35f + Mathf.Pow(sd, 4f) * 0.08f);

                if (d.y > 0.005f)
                {
                    // 구름: 높이 1000 평면으로 투영, 지평선 쪽은 납작하게 모임
                    float k = 1f / (d.y + 0.06f);
                    float qx = d.x * k * 0.55f + seed * 3.1f, qz = d.z * k * 0.55f - seed * 1.7f;
                    float dens = Fbm(qx, qz, 5);
                    float cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - pal.Coverage, 1f - pal.Coverage + 0.12f, dens));
                    cover *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.005f, 0.06f, d.y));
                    if (cover > 0f)
                    {
                        // 해 쪽으로 조금 옮긴 곳의 밀도가 낮으면 빛을 받는 면
                        float dl = Fbm(qx + sunFlat.x * 0.08f, qz + sunFlat.y * 0.08f, 5);
                        float lit = Mathf.Clamp01(0.55f + (dens - dl) * 6f);
                        lit = Mathf.Lerp(lit, 1f, Mathf.Pow(sd, 6f) * 0.6f);
                        var cloud = Color.Lerp(pal.CloudShade, pal.CloudLit, Mathf.SmoothStep(0.2f, 0.85f, lit));
                        cloud = Color.Lerp(cloud, pal.Horizon, Mathf.Pow(1f - up, 6f) * 0.5f);   // 먼 구름은 하늘색에 묻힘
                        c = Color.Lerp(c, cloud, cover);
                    }
                }
                else
                {
                    c = Color.Lerp(pal.Horizon, pal.Horizon * 0.85f, Mathf.Clamp01(-d.y * 4f));
                }
                c.a = 1f;
                px[y * W + x] = c;
            }
        var tex = SaveTexture($"Sky_{name}", W, H, px);
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
        ti.wrapModeU = TextureWrapMode.Repeat;
        ti.wrapModeV = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false;
        ti.maxTextureSize = 2048;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();

        string path = $"{Root}/Materials/Sky_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("Skybox/Panoramic");
        mat.SetTexture("_MainTex", tex);
        mat.SetFloat("_Mapping", 1f);       // 위경도
        mat.SetFloat("_ImageType", 0f);     // 360°
        mat.SetFloat("_Exposure", 1f);
        mat.SetFloat("_Rotation", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 씬에 시간대 프리셋 붙이기(첫 번째 = 맑은 오후가 기본)
    public static LightingPresets Setup(Light sun)
    {
        var dayEuler = new Vector3(42f, 140f, 0f);
        var sunsetEuler = new Vector3(14f, -70f, 0f);
        Vector3 Toward(Vector3 e) => -(Quaternion.Euler(e) * Vector3.forward);

        var day = new LightingPresets.Preset
        {
            name = "맑은 오후", skybox = Make("Day", Day, Toward(dayEuler), 1), sunEuler = dayEuler,
            sunColor = new Color(1f, 0.96f, 0.88f), sunIntensity = 1.35f,
            ambientSky = new Color(0.60f, 0.70f, 0.86f), ambientEquator = new Color(0.62f, 0.68f, 0.72f), ambientGround = new Color(0.40f, 0.42f, 0.32f),
            fogColor = new Color(0.70f, 0.80f, 0.92f), fogDensity = 0.00042f,
            fogSunColor = new Color(0.98f, 0.94f, 0.84f), fogDistance = 0.00055f, fogHeightFalloff = 0.011f, fogBase = 0f, fogMax = 0.96f,
            skyHorizon = Day.Horizon, skyZenith = Day.Zenith,
        };
        var sunset = new LightingPresets.Preset
        {
            name = "노을", skybox = Make("Sunset", Sunset, Toward(sunsetEuler), 2), sunEuler = sunsetEuler,
            sunColor = new Color(1f, 0.74f, 0.5f), sunIntensity = 1.3f,
            ambientSky = new Color(0.52f, 0.46f, 0.58f), ambientEquator = new Color(0.66f, 0.52f, 0.46f), ambientGround = new Color(0.30f, 0.26f, 0.22f),
            fogColor = new Color(0.72f, 0.60f, 0.62f), fogDensity = 0.0005f,
            fogSunColor = new Color(1.0f, 0.70f, 0.42f), fogDistance = 0.0007f, fogHeightFalloff = 0.010f, fogBase = 0f, fogMax = 0.96f,
            skyHorizon = Sunset.Horizon, skyZenith = Sunset.Zenith,
        };
        var go = new GameObject("LightingPresets");
        var lp = go.AddComponent<LightingPresets>();
        lp.Set(sun, new[] { day, sunset }, 0);
        return lp;
    }
}
