using UnityEditor;
using UnityEngine.TextCore.Text;

namespace Scrun.App.Editor
{
    // 앱 글꼴 에셋(Pretendard)은 쓰는 글자만 그때그때 그리는 동적 에셋이라, Play 중에 그린 글자가 에셋에 쌓인다.
    // 그대로 저장되면 Play할 때마다 git 변경이 생기므로 Play가 끝나면 비운다(빌드할 때도 비움: ClearDynamicDataOnBuild).
    [InitializeOnLoad]
    static class AppFontReset
    {
        static readonly string[] Paths =
        {
            "Assets/Scrun/App/UI/Fonts/Pretendard-Regular.asset",
            "Assets/Scrun/App/UI/Fonts/Pretendard-SemiBold.asset",
            "Assets/Scrun/App/UI/Fonts/Pretendard-Bold.asset",
        };

        static AppFontReset() => EditorApplication.playModeStateChanged += s =>
        {
            if (s != PlayModeStateChange.EnteredEditMode) return;
            foreach (var p in Paths)
            {
                var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(p);
                if (fa == null) continue;
                fa.ClearFontAssetData(true);
                AssetDatabase.SaveAssetIfDirty(fa); // 비운 상태 = 저장소에 올린 상태
            }
        };
    }
}
