using UnityEditor;
using UnityEngine;

namespace Scrun.App.Editor
{
    // 코스 화면이 생기기 전에 보정 화면을 에디터에서 열어 보는 메뉴(계획 0023). Play 중, 앱 씬에서.
    // 가짜 센서는 Fake Native 창 "속도 센서" 칸에서 사람 속도를 바꿔 가며 진행한다(안내대로 2 → 4 → … → 14).
    static class CalibrationDevMenu
    {
        [MenuItem("Scrun/개발/보정 화면 열기")]
        static void Open()
        {
            if (AppRoot.Nav == null)
            {
                Debug.LogWarning("[보정] 앱 씬(Assets/Scrun/Scenes/App/App.unity)을 Play한 뒤에 여세요.");
                return;
            }
            AppRoot.Nav.Push(new CalibrationIntroScreen(
                onDone: () => Debug.Log("[보정] 완료: " + Native.Speed.ExportCalibration()),
                onCancel: () => Debug.Log("[보정] 그만둠")));
        }

        [MenuItem("Scrun/개발/보정 화면 열기", true)]
        static bool CanOpen() => EditorApplication.isPlaying;
    }
}
