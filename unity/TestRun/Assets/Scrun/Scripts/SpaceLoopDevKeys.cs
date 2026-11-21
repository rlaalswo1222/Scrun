using UnityEngine;
using UnityEngine.InputSystem;

namespace Scrun
{
    // 우주 루프 확인용 키(개발용): 1~5 구간 바로가기, [ ] 100m 뒤·앞, ↑↓ 속도(FakeSpeedSource), Space 정지, O 러닝복 단계(AvatarOutfit)
    public class SpaceLoopDevKeys : MonoBehaviour
    {
        [SerializeField] RunnerController runner;
        [SerializeField] FollowCamera follow;
        [SerializeField] AvatarOutfit outfit;   // 비면 러너 아래에서 찾음
        [SerializeField] float zoneLength = 600f;
        [SerializeField] float[] spots = { 120f, 860f, 1370f, 1965f, 2535f };   // 구간마다 볼거리 앞

        static readonly string[] Names = { "1 구름 위 기지", "2 화성 협곡", "3 밤 숲 호수", "4 얼음 위성", "5 달 기지" };
        string msg = "";
        float msgUntil;

        // 개발용이라 출시 빌드에서는 끔(꺼진 컴포넌트는 Update·OnGUI 모두 안 불림 → 구간 글자가 화면에 안 뜸)
        void Awake()
        {
            if (!Debug.isDebugBuild) enabled = false;
        }

        void Go(float d, string label)
        {
            runner.Teleport(d);
            if (follow) follow.Snap();
            msg = label; msgUntil = Time.time + 2f;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !runner) return;
            for (int i = 0; i < spots.Length; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) Go(spots[i], Names[i]);
            if (kb.leftBracketKey.wasPressedThisFrame) Go(Mathf.Max(0, runner.Distance - 100f), "-100m");
            if (kb.rightBracketKey.wasPressedThisFrame) Go(runner.Distance + 100f, "+100m");
            if (kb.oKey.wasPressedThisFrame)
            {
                if (!outfit) outfit = runner.GetComponentInChildren<AvatarOutfit>();
                if (outfit)
                {
                    outfit.Level = outfit.Level % AvatarOutfit.MaxLevel + 1;
                    msg = $"러닝복 Lv.{outfit.Level}"; msgUntil = Time.time + 2f;
                }
            }
        }

        void OnGUI()
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            s.normal.textColor = Color.white;
            float lap = zoneLength * 5;
            float d = runner ? Mathf.Repeat(runner.Distance, lap) : 0;
            int z = Mathf.Clamp((int)(d / zoneLength), 0, 4);
            GUI.Label(new Rect(16, 12, 900, 30), $"{Names[z]}  {d:0}m  {(runner ? runner.SpeedKmh : 0):0.0} km/h   [1~5 구간] [ [ ] ±100m ] [↑↓ 속도] [O 러닝복]", s);
            if (Time.time < msgUntil) GUI.Label(new Rect(16, 40, 600, 30), msg, s);
        }
    }
}
