using UnityEngine;

namespace Scrun
{
    // 뼈대 단계용 임시 HUD (OnGUI). 디자인 들어가면 UI Toolkit/uGUI로 교체.
    public class SpeedHud : MonoBehaviour
    {
        [SerializeField] RunnerController runner;

        GUIStyle style;

        void OnGUI()
        {
            if (!runner) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(18, Screen.height / 28) };

            float kmh = runner.SpeedKmh;
            string pace = kmh > 0.1f ? FormatPace(60f / kmh) : "--'--\"";
            string text =
                $"속도  {kmh:0.0} km/h\n" +
                $"페이스  {pace} /km\n" +
                $"거리  {runner.Distance / 1000f:0.00} km  ({runner.Lap}바퀴)\n" +
                (Application.isMobilePlatform ? "" : "↑↓ 속도 ±0.5   Space 정지");

            GUI.Box(new Rect(10, 10, style.fontSize * 16, style.fontSize * 6.2f), GUIContent.none);
            GUI.Label(new Rect(20, 16, style.fontSize * 16, style.fontSize * 6f), text, style);
        }

        static string FormatPace(float minPerKm)
        {
            int total = Mathf.RoundToInt(minPerKm * 60f);
            return $"{total / 60}'{total % 60:00}\"";
        }
    }
}
