using System.Linq;
using UnityEditor;
using UnityEngine.TextCore.Text;

namespace Scrun.App.Editor
{
    // 앱 글꼴 에셋(Pretendard)은 쓰는 글자만 그때그때 그리는 동적 에셋이라, 화면을 그릴 때마다(Play·에디트 모드 Game 창)
    // 그린 글자가 에셋에 쌓인다. 그대로 저장되면 git 변경이 계속 생기므로 저장 직전에 비운다
    // → 파일은 늘 빈 상태(저장소와 같음), 화면에 필요한 글자는 다음에 그릴 때 다시 만든다. 빌드할 때도 비움(ClearDynamicDataOnBuild).
    class AppFontReset : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            foreach (var p in paths.Where(p => AppSceneBuilder.FontAssetPaths.Contains(p)))
            {
                var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(p);
                if (fa != null) fa.ClearFontAssetData(true);
            }
            return paths;
        }
    }
}
