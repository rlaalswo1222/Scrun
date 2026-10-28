using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Scrun
{
    // 이름표(머리 위) + 오른쪽 "근처 러너" 목록. 모바일에서도 가볍게 OnGUI 한 번에 그림
    public class RunnerLabels : MonoBehaviour
    {
        GUIStyle tag, list;
        Texture2D white;
        Camera cam;

        void OnGUI()
        {
            var me = RunnerTag.Player;
            if (!me || !me.runner) return;
            cam ??= Camera.main;
            if (!cam) return;
            white ??= Texture2D.whiteTexture;
            float fs = Mathf.Max(13, Screen.height / 52);
            tag ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            list ??= new GUIStyle(GUI.skin.label);
            tag.fontSize = (int)fs;
            list.fontSize = (int)fs;

            float myD = me.runner.Distance;
            float mySpeed = Mathf.Max(1f, me.runner.SpeedKmh / 3.6f);
            var rows = new List<(RunnerTag t, float gap)>();
            var drawn = new List<Rect>();
            foreach (var t in RunnerTag.All)
            {
                if (t == me || !t.Visible || t.crowd) continue;
                float gap = t.runner.Distance - myD;
                if (Mathf.Abs(gap) > 2000f) continue;
                rows.Add((t, gap));

                // 머리 위 이름표: 80m 안, 화면 앞쪽만
                var head = t.runner.transform.position + Vector3.up * 2.15f;
                var sp = cam.WorldToScreenPoint(head);
                if (sp.z <= 0f || sp.z > 80f) continue;
                var size = tag.CalcSize(new GUIContent(t.displayName));
                var r = new Rect(sp.x - size.x / 2 - 6, Screen.height - sp.y - size.y, size.x + 12, size.y + 2);
                // 다른 이름표와 겹치면 위로 쌓음
                for (int k = 0; k < 6 && drawn.Exists(o => o.Overlaps(r)); k++) r.y -= r.height + 2;
                drawn.Add(r);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(r, white);
                GUI.color = t.color;
                GUI.DrawTexture(new Rect(r.x, r.yMax - 3, r.width, 3), white);
                GUI.color = Color.white;
                GUI.Label(r, t.displayName, tag);
            }

            // 근처 러너 목록(앞선 사람이 위)
            rows.Sort((a, b) => b.gap.CompareTo(a.gap));
            float w = fs * 19f, rowH = fs * 1.6f;
            float x = Screen.width - w - 10, y = 10;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(x, y, w, rowH * (rows.Count + 2) + 8), white);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 8, y + 4, w, rowH), "근처 러너", list);
            y += rowH + 4;
            bool meDrawn = false;
            void Row(string name, string gapText, Color c, bool highlight, string timeText = "")
            {
                if (highlight) { GUI.color = new Color(0.2f, 0.55f, 1f, 0.6f); GUI.DrawTexture(new Rect(x, y, w, rowH), white); }
                GUI.color = c;
                GUI.DrawTexture(new Rect(x + 4, y + rowH * 0.25f, 4, rowH * 0.5f), white);
                GUI.color = Color.white;
                GUI.Label(new Rect(x + 14, y, w * 0.56f, rowH), name, list);
                GUI.Label(new Rect(x + w * 0.58f, y, w * 0.2f, rowH), gapText, list);
                GUI.Label(new Rect(x + w * 0.79f, y, w * 0.21f, rowH), timeText, list);
                y += rowH;
            }
            foreach (var (t, gap) in rows)
            {
                if (!meDrawn && gap < 0f) { Row(me.displayName, "", me.color, true); meDrawn = true; }
                float sec = gap / mySpeed;
                string text = Mathf.Abs(gap) < 1000f ? $"{(gap >= 0 ? "+" : "-")}{Mathf.Abs(gap):0}m" : $"{(gap >= 0 ? "+" : "-")}{Mathf.Abs(gap) / 1000f:0.0}km";
                string time = Mathf.Abs(sec) < 600f ? $"{(sec >= 0 ? "+" : "-")}{(int)Mathf.Abs(sec) / 60}:{(int)Mathf.Abs(sec) % 60:00}" : "";
                Row(t.displayName, text, t.color, false, time);
            }
            if (!meDrawn) Row(me.displayName, "", me.color, true);
        }
    }
}
