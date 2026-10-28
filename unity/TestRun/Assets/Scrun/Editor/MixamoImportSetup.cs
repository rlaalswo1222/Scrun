using System.IO;
using UnityEditor;
using UnityEngine;

// Mixamo FBX("캐릭터@동작.fbx") 가져오기 설정 자동화
//  - Humanoid 리그. 스킨 있는 기준 파일(@Breathing Idle)의 Avatar를 나머지가 공유
//  - 클립: 반복, 회전·높이는 포즈에 고정. 앞뒤 이동(XZ)은 루트 모션으로 빼 두고
//    Animator.applyRootMotion=false로 버린다 → In Place 여부와 무관하게 제자리
//    (Mixamo In Place가 Sprint 등 일부 동작엔 적용되지 않음)
//  - 클립 이름 "mixamo.com" -> 파일의 동작 이름
public class MixamoImportSetup : AssetPostprocessor
{
    public const string Folder = "Assets/Scrun/Characters/";
    const string BaseSuffix = "@Breathing Idle.fbx";

    static bool IsMixamo(string path) =>
        path.StartsWith(Folder) &&
        path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) && path.Contains("@");

    void OnPreprocessModel()
    {
        if (!IsMixamo(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.importCameras = false;
        mi.importLights = false;

        string basePath = BasePathFor(assetPath);
        var baseAvatar = basePath == assetPath ? null : AssetDatabase.LoadAssetAtPath<Avatar>(basePath);
        if (baseAvatar)
        {
            mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            mi.sourceAvatar = baseAvatar;
        }
        else
        {
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }
    }

    void OnPreprocessAnimation()
    {
        if (!IsMixamo(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        string clipName = Path.GetFileNameWithoutExtension(assetPath).Split('@')[1];

        // 이미 설정된 클립(구간 자르기 등)이 있으면 유지하고 옵션만 덮어씀
        var clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = clipName;
            c.loopTime = true;
            c.lockRootRotation = true;
            c.keepOriginalOrientation = true;
            c.lockRootHeightY = true;
            c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = false;
            c.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = clips;
    }

    static string BasePathFor(string path)
    {
        string dir = Path.GetDirectoryName(path).Replace('\\', '/');
        string character = Path.GetFileName(path).Split('@')[0];
        return $"{dir}/{character}{BaseSuffix}";
    }

    // 기준 파일이 먼저 들어와야 나머지가 Avatar를 공유할 수 있으므로, 처음엔 이 메뉴로 순서대로 다시 가져온다
    [MenuItem("Scrun/Reimport Mixamo Characters")]
    public static void ReimportAll()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Folder.TrimEnd('/') }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(BaseSuffix)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Folder.TrimEnd('/') }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (IsMixamo(p) && !p.EndsWith(BaseSuffix)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
    }
}
