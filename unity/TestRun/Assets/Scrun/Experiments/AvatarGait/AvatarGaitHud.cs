using UnityEngine;

namespace Scrun.Experiments
{
    // 실험 화면 글자: 속도와 지금 걷기/달리기
    public class AvatarGaitHud : MonoBehaviour
    {
        public AvatarGaitDriver[] drivers;
        public float y = 16f;
        public bool gaitOnly;
        GUIStyle style;

        void OnGUI()
        {
            if (drivers == null || drivers.Length == 0 || drivers[0] == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.035f) };
            style.normal.textColor = Color.black;
            var d = drivers[0];
            string txt = gaitOnly ? "아바타: " + d.gaitLabel : d.speedKmh.ToString("0.0") + " km/h   " + d.gaitLabel;
            GUI.Label(new Rect(20, y, Screen.width, 80), txt, style);
        }
    }
}
