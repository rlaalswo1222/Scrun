using UnityEditor;
using UnityEngine;
using static ScrunEditorUtil;

// 사진 텍스처(Poly Haven CC0)를 손그림풍 재질에 얹는 도구.
//  - 나무껍질: 4종을 세로로 이어 붙인 한 장 → 가져오기 설정으로 텍스처 배열(플랫폼별 압축 유지)
//  - 바위: 3면 투영용 색·노멀 + 평균색(셰이더가 평균색으로 나눠 정점 색이 기본 색이 되게)
public static class NatureTextures
{
    const string Tex = PolyHavenImportSetup.Folder + "Textures/";

    // 껍질 종류(StylizedFlora가 UV1.y = 종류 + 1로 표시)
    public enum Bark { Redwood, Pine, Broadleaf, Palm }
    static readonly string[] barkSources = { "metasequoia_bark", "pine_bark", "bark_brown_02", "palm_bark" };
    // 껍질 텍스처 한 장이 덮는 실제 크기(m): 둘레·높이 방향 반복 횟수 계산용
    public static readonly float[] BarkMeters = { 2.2f, 1.6f, 1.4f, 1.2f };

    public static Texture2DArray BarkArray()
    {
        const int S = 1024;
        int n = barkSources.Length;
        var px = new Color[S * S * n];
        for (int k = 0; k < n; k++)
        {
            var img = RawImage.Load($"{Tex}{barkSources[k]}/{barkSources[k]}_diff_1k.jpg");
            // 가져오기 순서: 위 칸부터 → k번째 종류를 위에서 k번째 칸에(텍스처 행은 아래가 0)
            int row0 = (n - 1 - k) * S;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var c = img.Px[(y * img.H / S) * img.W + x * img.W / S];
                    c.a = 1f;
                    px[(row0 + y) * S + x] = c;
                }
        }
        SaveTexture("Sty_BarkStrip", S, S * n, px);   // 두 번째부터는 배열로 가져와져 Texture2D로는 안 읽힘 → 경로로 다룸
        string path = $"{Root}/Textures/Sty_BarkStrip.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        var st = new TextureImporterSettings();
        ti.ReadTextureSettings(st);
        st.textureShape = TextureImporterShape.Texture2DArray;
        st.flipbookRows = n;
        st.flipbookColumns = 1;
        ti.SetTextureSettings(st);
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.maxTextureSize = S * n;
        ti.anisoLevel = 4;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
    }

    // 바위 재질 설정: scale = 1m당 반복, moss = 윗면 이끼 정도, steep > 0 = 가파른 면에만(지형)
    public static void ApplyRock(Material mat, float scale, float moss, float steep = 0f, float normal = 1f, float strength = 1f)
    {
        const string name = "rock_boulder_dry";
        string diff = $"{Tex}{name}/{name}_diff_1k.jpg", nor = $"{Tex}{name}/{name}_nor_gl_1k.jpg";
        var img = RawImage.Load(diff);
        var mean = Color.black;
        for (int i = 0; i < img.Px.Length; i += 7) mean += img.Px[i];
        mean /= Mathf.Ceil(img.Px.Length / 7f);
        // RawImage는 sRGB 값을 그대로 읽음. SetColor가 선형으로 바꿔 넣으므로 sRGB 값 그대로 넘김(셰이더에서 선형 텍스처 평균과 맞음)
        var meanGamma = new Color(mean.r, mean.g, mean.b, 1f);
        mat.SetTexture("_RockMap", AssetDatabase.LoadAssetAtPath<Texture2D>(diff));
        mat.SetTexture("_RockNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(nor));
        mat.SetColor("_RockMean", meanGamma);
        mat.SetFloat("_RockScale", scale);
        mat.SetFloat("_RockStrength", strength);
        mat.SetFloat("_RockNormalStrength", normal);
        mat.SetFloat("_RockSteep", steep);
        mat.SetFloat("_MossAmount", moss);
        mat.SetColor("_MossColor", new Color(0.36f, 0.46f, 0.16f));
        mat.SetFloat("_UseRock", 1f);
        mat.EnableKeyword("_ROCK_TRIPLANAR");
        EditorUtility.SetDirty(mat);
    }
}
