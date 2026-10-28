using UnityEngine;

namespace Scrun
{
    // 폰 테스트용 터치 버튼 + 성능 표시(OnGUI, 폰에서만 보임 — 에디터에선 showInEditor로 확인).
    // 오른쪽 아래: 속도 −/＋/정지, 아래: 지역 1~5·코스 바꾸기, 위: FPS(1초 평균·최저)·품질 단계(누르면 바뀜)
    // 실제 센서 연결 전까지의 임시 조작. 출시 빌드에선 끔.
    public class MobileTestControls : MonoBehaviour
    {
        [SerializeField] FakeSpeedSource speed;
        [SerializeField] bool showInEditor;

        DevTravel travel;
        ScrunQuality quality;
        GUIStyle button, label;
        float acc, worst, fps, worstShown;
        int frames;

        static readonly string[] RegionNames = { "항구", "호수", "협곡", "풍차", "절벽" };

        void Start()
        {
            travel = FindAnyObjectByType<DevTravel>();
            quality = FindAnyObjectByType<ScrunQuality>();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            acc += dt; frames++;
            worst = Mathf.Max(worst, dt);
            if (acc >= 1f)
            {
                fps = frames / acc;
                worstShown = worst > 0f ? 1f / worst : 0f;
                acc = 0f; frames = 0; worst = 0f;
            }
        }

        bool Visible => Application.isMobilePlatform || (Application.isEditor && showInEditor);

        void OnGUI()
        {
            if (!Visible) return;
            float u = Mathf.Max(28f, Screen.height / 11f);   // 버튼 한 칸(손가락 크기)
            button ??= new GUIStyle(GUI.skin.button);
            label ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
            button.fontSize = (int)(u * 0.36f);
            label.fontSize = (int)(u * 0.32f);
            var safe = Screen.safeArea;
            float right = safe.xMax - 12f, bottom = Screen.height - safe.yMin - 12f;

            // 속도: 오른쪽 아래 세로로 ＋ / − / 정지
            if (speed)
            {
                float x = right - u * 1.6f;
                if (GUI.Button(new Rect(x, bottom - u * 3.4f, u * 1.6f, u), "+0.5", button)) speed.Add(0.5f);
                if (GUI.Button(new Rect(x, bottom - u * 2.25f, u * 1.6f, u), "−0.5", button)) speed.Add(-0.5f);
                if (GUI.Button(new Rect(x, bottom - u * 1.1f, u * 1.6f, u * 1.1f), "정지", button)) speed.Stop();
                if (GUI.Button(new Rect(x - u * 1.7f, bottom - u * 3.4f, u * 1.6f, u), "+2", button)) speed.Add(2f);
                if (GUI.Button(new Rect(x - u * 1.7f, bottom - u * 2.25f, u * 1.6f, u), "−2", button)) speed.Add(-2f);
            }

            // 지역·코스: 아래 가운데
            if (travel)
            {
                int n = Mathf.Min(travel.RegionCount, RegionNames.Length);
                float w = u * 1.3f, x0 = safe.xMin + 12f;
                for (int i = 0; i < n; i++)
                    if (GUI.Button(new Rect(x0 + i * (w + 6f), bottom - u * 0.9f, w, u * 0.9f), RegionNames[i], button)) travel.GoToRegion(i);
                if (GUI.Button(new Rect(x0 + n * (w + 6f), bottom - u * 0.9f, w * 1.2f, u * 0.9f), "코스", button)) travel.NextRouteFromUI();
            }

            // 성능: 위 가운데(누르면 품질 단계 바꿈)
            string tier = ScrunQuality.Current.ToString();
            string text = $"{fps:0} fps (최저 {worstShown:0})  ·  {tier}  ·  {Screen.width}×{Screen.height}";
            var r = new Rect(Screen.width * 0.5f - u * 4.5f, Screen.height - safe.yMax + 8f, u * 9f, u * 0.8f);
            GUI.color = fps < 25f ? new Color(1f, 0.6f, 0.6f) : Color.white;
            if (GUI.Button(r, text, button) && quality)
            {
                var next = ScrunQuality.Current switch
                {
                    ScrunQuality.Tier.Low => ScrunQuality.Tier.Medium,
                    ScrunQuality.Tier.Medium => ScrunQuality.Tier.High,
                    _ => ScrunQuality.Tier.Low,
                };
                quality.Apply(next);
            }
            GUI.color = Color.white;
        }
    }
}
