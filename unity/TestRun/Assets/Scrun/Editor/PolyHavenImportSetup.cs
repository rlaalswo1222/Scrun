using UnityEditor;

// Poly Haven 파일 가져오기 설정: 이름 규칙(_diff/_nor_gl/_rough/_alpha)으로 텍스처 종류를 정한다.
// 모델은 재질을 만들지 않는다(재질은 빌더가 URP용으로 직접 만듦).
public class PolyHavenImportSetup : AssetPostprocessor
{
    public const string Folder = "Assets/Scrun/ThirdParty/PolyHaven/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var ti = (TextureImporter)assetImporter;
        ti.maxTextureSize = 1024;
        ti.anisoLevel = 4;
        ti.mipmapEnabled = true;
        ti.isReadable = true;   // 빌더가 픽셀을 읽어 합성(도로 차선, 잎 알파 등)
        if (assetPath.Contains("_nor_gl_"))
        {
            ti.textureType = TextureImporterType.NormalMap;
        }
        else if (assetPath.Contains("_rough_") || assetPath.Contains("_alpha_"))
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
        }
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var mi = (ModelImporter)assetImporter;
        // 재질 이름(부위: planks/poles, hull/sails…)을 알기 위해 내장 재질로 가져옴. 실제 재질은 빌더가 URP용으로 새로 만듦
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importAnimation = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.isReadable = true;   // 바운드·정점 확인용
    }
}
