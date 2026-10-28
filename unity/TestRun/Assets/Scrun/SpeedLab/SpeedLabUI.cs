using UnityEngine;

namespace Scrun.SpeedLab
{
    // 실험 화면(OnGUI). 가로: 왼쪽 = 큰 속도·버튼, 오른쪽 = 속도 그래프·주파수 폭포. 세로면 위아래.
    // 그래프: 흰 = 실제(입력), 하늘 = 걸음, 주황 = 주파수, 초록 = 합침
    // 폭포: 가로 = 시간(90초), 세로 = 주파수(아래 4Hz ~ 위 최대), 밝을수록 주변보다 솟은 진동
    [RequireComponent(typeof(SpeedLab))]
    public class SpeedLabUI : MonoBehaviour
    {
        SpeedLab lab;
        Texture2D graph, fall;
        Color32[] gpx, fpx;
        int fallRows, fallCol;
        GUIStyle big, mid, small, btn, box;
        const int GW = SpeedLab.History, GH = 200, FH = 256;
        static readonly Color32 CTrue = new Color32(255, 255, 255, 255), CStep = new Color32(90, 200, 255, 255),
            CFreq = new Color32(255, 160, 40, 255), CFused = new Color32(80, 255, 120, 255), CBg = new Color32(16, 22, 28, 255),
            CGrid = new Color32(45, 55, 65, 255);

        void Awake()
        {
            lab = GetComponent<SpeedLab>();
            graph = new Texture2D(GW, GH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            gpx = new Color32[GW * GH];
            fall = new Texture2D(GW, FH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            fpx = new Color32[GW * FH];
            for (int i = 0; i < fpx.Length; i++) fpx[i] = CBg;
            fall.SetPixels32(fpx); fall.Apply();
            lab.SpectrumColumn += AddColumn;
        }

        void AddColumn(float[] peak)
        {
            var f = lab.Freq;
            for (int r = 0; r < FH; r++)
            {
                float hz = FreqSpeed.FMin + (f.FMax - FreqSpeed.FMin) * r / (FH - 1);
                float p = FreqSpeed.At(peak, f.BinHz, hz);
                float a = Mathf.Clamp01(p / 1.0f);
                fpx[r * GW + fallCol] = Color32.Lerp(CBg, new Color32(255, 230, 120, 255), a);
            }
            // 찾은 줄들이 가리키는 주파수(현재 추정 속도 기준) 표시
            if (lab.FreqOk)
                foreach (var l in lab.Lines)
                {
                    float hz = l.k * lab.FreqKmh;
                    int r = Mathf.RoundToInt((hz - FreqSpeed.FMin) / (f.FMax - FreqSpeed.FMin) * (FH - 1));
                    if (r >= 0 && r < FH) fpx[r * GW + fallCol] = CFused;
                }
            fallCol = (fallCol + 1) % GW;
            for (int r = 0; r < FH; r++) fpx[r * GW + fallCol] = new Color32(255, 60, 60, 255);   // 현재 위치
            fall.SetPixels32(fpx); fall.Apply();
        }

        void DrawGraph()
        {
            float vmax = 16f;
            for (int i = 0; i < SpeedLab.History; i++) vmax = Mathf.Max(vmax, lab.HTrue[i] + 2, lab.HFused[i] + 2);
            vmax = Mathf.Ceil(vmax / 2) * 2;
            for (int i = 0; i < gpx.Length; i++) gpx[i] = CBg;
            for (int v = 2; v < vmax; v += 2)
            {
                int y = (int)(v / vmax * (GH - 1));
                for (int x = 0; x < GW; x++) gpx[y * GW + x] = CGrid;
            }
            Plot(lab.HTrue, vmax, CTrue, 0);
            Plot(lab.HStep, vmax, CStep, 0);
            Plot(lab.HFreq, vmax, CFreq, 0);
            Plot(lab.HFused, vmax, CFused, 1);
            graph.SetPixels32(gpx); graph.Apply();
        }

        void Plot(float[] h, float vmax, Color32 c, int thick)
        {
            int prev = -1;
            for (int x = 0; x < GW; x++)
            {
                float v = h[(lab.HHead + x) % GW];
                if (float.IsNaN(v) || v <= 0) { prev = -1; continue; }
                int y = Mathf.Clamp((int)(v / vmax * (GH - 1)), 0, GH - 1);
                int a = prev < 0 ? y : Mathf.Min(prev, y), b = prev < 0 ? y : Mathf.Max(prev, y);
                for (int yy = a; yy <= b; yy++)
                    for (int t = -thick; t <= thick; t++)
                        if (yy + t >= 0 && yy + t < GH) gpx[(yy + t) * GW + x] = c;
                prev = y;
            }
        }

        float nextGraph;

        void Update()
        {
            if (Time.unscaledTime >= nextGraph) { nextGraph = Time.unscaledTime + SpeedLab.Tick; DrawGraph(); }
#if UNITY_EDITOR
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) lab.Sensor.FakeRunning = !lab.Sensor.FakeRunning;
#endif
        }

        void Styles(float u)
        {
            big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            mid ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true };
            btn ??= new GUIStyle(GUI.skin.button);
            box ??= new GUIStyle(GUI.skin.box);
            big.fontSize = (int)(u * 2.6f);
            mid.fontSize = (int)(u * 0.42f);
            small.fontSize = (int)(u * 0.32f);
            btn.fontSize = (int)(u * 0.4f);
        }

        void OnGUI()
        {
            var safe = Screen.safeArea;
            var area = new Rect(safe.x, Screen.height - safe.yMax, safe.width, safe.height);
            bool land = area.width > area.height;
            float u = Mathf.Max(24f, (land ? area.height : area.width) / 11f);
            Styles(u);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, CBg, 0, 0);

            float pad = u * 0.2f;
            Rect left, right;
            if (land)
            {
                left = new Rect(area.x + pad, area.y + pad, area.width * 0.46f - pad, area.height - 2 * pad);
                right = new Rect(left.xMax + pad, area.y + pad, area.xMax - left.xMax - 2 * pad, area.height - 2 * pad);
            }
            else
            {
                left = new Rect(area.x + pad, area.y + pad, area.width - 2 * pad, area.height * 0.56f);
                right = new Rect(area.x + pad, left.yMax + pad, area.width - 2 * pad, area.yMax - left.yMax - 2 * pad);
            }
            Controls(left, u);
            Plots(right, u);
        }

        void Controls(Rect r, float u)
        {
            float y = r.y, w = r.width;
            string which = lab.Show == SpeedLab.Method.Fused ? (lab.FreqOk ? "합침(주파수)" : lab.StepOk ? "합침(걸음)" : "합침")
                : lab.Show == SpeedLab.Method.Step ? "걸음" : "주파수";
            bool ok = lab.Show == SpeedLab.Method.Step ? lab.StepOk : lab.Show == SpeedLab.Method.Freq ? lab.FreqOk : (lab.StepOk || lab.FreqOk);
            bool guiding = lab.Guide != SpeedLab.GuideMode.None;
            // 안내 중: 큰 숫자 = 맞출 속도(노랑), 추정값은 아래 걸음 줄에
            GUI.color = guiding ? new Color(1f, 0.85f, 0.3f) : ok ? Color.white : new Color(1, 1, 1, 0.35f);
            GUI.Label(new Rect(r.x, y, w * 0.72f, u * 2.4f), (guiding ? lab.TrueKmh : lab.Shown).ToString("0.0"), big);
            GUI.color = Color.white;
            if (GUI.Button(new Rect(r.x + w * 0.72f, y + u * 0.3f, w * 0.28f, u * 0.9f), which, btn))
                lab.Show = (SpeedLab.Method)(((int)lab.Show + 1) % 3);
            GUI.Label(new Rect(r.x + w * 0.72f, y + u * 1.3f, w * 0.28f, u * 0.7f), "km/h", mid);
            y += u * 2.5f;

            if (guiding)
            {
                int left = Mathf.CeilToInt(lab.GuideLeft), n = lab.GuidePlan.Length;
                GUI.contentColor = new Color(1f, 0.85f, 0.3f);
                GUI.Label(new Rect(r.x, y, w, u * 0.6f), lab.GuideSettling
                    ? $"러닝머신을 {lab.TrueKmh:0.0} km/h로 맞추세요  {left}초  ({lab.GuideStep + 1}/{n})"
                    : $"{lab.TrueKmh:0.0} km/h 그대로 달리세요  기록 {left}초  ({lab.GuideStep + 1}/{n})", mid);
                GUI.contentColor = Color.white;
                y += u * 0.6f;
            }

            GUI.contentColor = new Color(0.55f, 0.85f, 1f);
            GUI.Label(new Rect(r.x, y, w, u * 0.6f), lab.ImpCadModel.ok || lab.ImpModel.ok || lab.StepModel.ok
                ? $"걸음  {(lab.StepOk ? lab.StepKmh.ToString("0.0") : "–")} km/h   {lab.Cad:0} spm  충격 {lab.Impact:0.0}  q {lab.Q:0.00}"
                : $"걸음  보정 필요   {lab.Cad:0} spm  충격 {lab.Impact:0.0}  q {lab.Q:0.00}", mid);
            y += u * 0.6f;
            GUI.contentColor = new Color(1f, 0.7f, 0.3f);
            GUI.Label(new Rect(r.x, y, w, u * 0.6f), lab.Lines.Count > 0
                ? $"주파수  {(lab.FreqOk ? lab.FreqKmh.ToString("0.0") : "–")} km/h   신뢰 {lab.FreqConf:0.00}  줄 {lab.Lines.Count}"
                : "주파수  보정 필요 (서로 다른 속도 2개 이상)", mid);
            GUI.contentColor = Color.white;
            y += u * 0.75f;

            // 실제 속도(러닝머신 화면 값) — 보정에도, 기록 비교에도 쓴다
            GUI.Label(new Rect(r.x, y, w, u * 0.5f), "실제 속도 (러닝머신에 맞춰 바꿔 주세요)", small);
            y += u * 0.5f;
            GUI.enabled = !guiding;
            float bw = w / 6.2f;
            if (GUI.Button(new Rect(r.x, y, bw, u), "−1", btn)) lab.SetTrue(lab.TrueKmh - 1);
            if (GUI.Button(new Rect(r.x + bw * 1.05f, y, bw, u), "−0.5", btn)) lab.SetTrue(lab.TrueKmh - 0.5f);
            GUI.Label(new Rect(r.x + bw * 2.1f, y, bw * 2, u), $"  {lab.TrueKmh:0.0}", mid);
            if (GUI.Button(new Rect(r.x + bw * 4.1f, y, bw, u), "+0.5", btn)) lab.SetTrue(lab.TrueKmh + 0.5f);
            if (GUI.Button(new Rect(r.x + bw * 5.15f, y, bw, u), "+1", btn)) lab.SetTrue(lab.TrueKmh + 1);
            y += u * 1.15f;

            string cal = lab.CalState == 1 ? $"준비… {Mathf.CeilToInt(lab.CalLeft)} (누르면 취소)"
                : lab.CalState == 2 ? $"보정 기록 중 {Mathf.CeilToInt(lab.CalLeft)}초 (누르면 취소)"
                : $"{lab.TrueKmh:0.0} km/h로 보정 (5초 뒤 20초)";
            GUI.color = lab.CalState == 2 ? new Color(1f, 0.6f, 0.6f) : lab.CalState == 1 ? new Color(1f, 0.9f, 0.5f) : Color.white;
            if (GUI.Button(new Rect(r.x, y, w * 0.64f, u), cal, btn)) lab.StartCalibration();
            GUI.color = Color.white;
            if (GUI.Button(new Rect(r.x + w * 0.66f, y, w * 0.16f, u), "마지막\n지움", btn)) lab.DeleteLast();
            if (GUI.Button(new Rect(r.x + w * 0.84f, y, w * 0.16f, u), "모두\n지움", btn)) lab.ClearAll();
            y += u * 1.1f;
            GUI.enabled = true;

            // 안내 모드: 앱이 속도를 하나씩 지시 (계획 0003)
            string seg = lab.GuideShort ? "5초" : "20초";
            GuideButton(new Rect(r.x, y, w * 0.27f, u), SpeedLab.GuideMode.Calib, $"보정\n{SpeedLab.CalibPlan.Length}×{seg}", guiding);
            GuideButton(new Rect(r.x + w * 0.28f, y, w * 0.27f, u), SpeedLab.GuideMode.Test, $"검증(달리기)\n{SpeedLab.TestPlan.Length}×{seg}", guiding);
            GuideButton(new Rect(r.x + w * 0.56f, y, w * 0.27f, u), SpeedLab.GuideMode.Walk, $"걷기 기록\n{SpeedLab.WalkPlan.Length}×{seg}", guiding);
            GUI.enabled = !guiding;
            if (GUI.Button(new Rect(r.x + w * 0.84f, y, w * 0.16f, u), lab.GuideShort ? "짧게" : "정식", btn)) lab.GuideShort = !lab.GuideShort;
            GUI.enabled = true;
            y += u * 1.1f;

            var sb = new System.Text.StringBuilder("보정: ");
            if (lab.Points.Count == 0) sb.Append("없음");
            foreach (var p in lab.Points)
                sb.Append($"{p.kmh:0.0}({(p.q >= 0.3f ? p.cad.ToString("0") + "spm 충격" + p.impact.ToString("0.0") : "벨트만")}) ");
            if (lab.ModelText.Length > 0) sb.Append("\n" + lab.ModelText);
            if (lab.Lines.Count > 0)
            {
                sb.Append("\n주파수 줄(Hz per km/h): ");
                foreach (var l in lab.Lines) sb.Append($"{l.k:0.00}({l.score:0.0}) ");
            }
            if (lab.Message.Length > 0) sb.Append("\n").Append(lab.Message);
            GUI.Label(new Rect(r.x, y, w, r.yMax - y - u * 1.0f), sb.ToString(), small);

            // 맨 아래: 걸음 창 길이, 새 기록
            float by = r.yMax - u * 0.9f;
            if (GUI.Button(new Rect(r.x, by, w * 0.32f, u * 0.9f), $"걸음 창 {lab.StepWindow:0}초", btn))
                lab.StepWindow = lab.StepWindow >= 6 ? 2 : lab.StepWindow + 1;
            if (GUI.Button(new Rect(r.x + w * 0.34f, by, w * 0.32f, u * 0.9f), $"주파수 기준 {lab.FreqConfMin:0.00}", btn))
                lab.FreqConfMin = lab.FreqConfMin >= 0.85f ? 0.25f : lab.FreqConfMin + 0.1f;
            if (GUI.Button(new Rect(r.x + w * 0.68f, by, w * 0.32f, u * 0.9f), "새 기록", btn)) lab.NewSession();
        }

        // 안내 버튼: 진행 중인 모드면 "안내 중지", 다른 모드가 진행 중이면 눌러도 무시
        void GuideButton(Rect r, SpeedLab.GuideMode mode, string label, bool guiding)
        {
            if (GUI.Button(r, lab.Guide == mode ? "안내 중지" : label, btn) && (!guiding || lab.Guide == mode))
                lab.StartGuide(mode);
        }

        void Plots(Rect r, float u)
        {
            var s = lab.Sensor;
            string info = s.Fs == 0 ? "센서 속도 재는 중…" : $"센서 {s.RawRate:0}Hz → {s.Fs}Hz  버림 {s.Dropped}  {s.Info}";
#if UNITY_EDITOR
            info += "  [에디터: R = 달리기 켬/끔]";
#endif
            GUI.Label(new Rect(r.x, r.y, r.width, u * 0.45f), info, small);
            float top = r.y + u * 0.5f, h = (r.yMax - top - u * 0.5f);
            var g = new Rect(r.x, top, r.width, h * 0.45f);
            GUI.DrawTexture(g, graph, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(g.x + 4, g.y, g.width, u * 0.45f), "속도 90초  흰=실제 하늘=걸음 주황=주파수 초록=합침", small);

            var f = new Rect(r.x, g.yMax + u * 0.15f, r.width, r.yMax - g.yMax - u * 0.6f);
            // 폭포는 원형 버퍼: 가장 오래된 열(현재 열 다음)부터 왼쪽에 오게 밀어서 그림
            float off = (float)(fallCol + 1) / GW;
            GUI.DrawTextureWithTexCoords(f, fall, new Rect(off, 0, 1, 1));
            if (lab.Freq != null)
            {
                GUI.Label(new Rect(f.x + 4, f.y, f.width, u * 0.45f), $"주파수 {lab.Freq.FMax:0}Hz", small);
                GUI.Label(new Rect(f.x + 4, f.yMax - u * 0.45f, f.width, u * 0.45f), $"{FreqSpeed.FMin:0}Hz   (90초, 밝음 = 솟은 진동, 초록 = 맞춘 줄)", small);
            }
            GUI.Label(new Rect(r.x, r.yMax - u * 0.45f, r.width, u * 0.45f), "기록: " + s.SessionDir, small);
        }
    }
}
