using System.Collections.Generic;
using Scrun.App;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scrun
{
    // 앱 맵 화면에서 들어온 섬 씬의 달리기 부분(계획 0029). MapLaunchApplier가 앱에서 불릴 때만 붙인다.
    //  1. 내 아바타 속도: 키보드(FakeSpeedSource) → 진짜 센서(NativeSpeedSource, 에디터는 가짜 센서). 달리기 전(RunLink.Moving 꺼짐)엔 0
    //  2. 섬의 가짜 봇·무리·시험용 고스트는 끈다: 폰마다 따로 돌아 기기마다 위치가 다름(BL-23 ③). 페이스 봇은 서버가 정하는 것으로 S4 이후
    //  3. 같은 맵의 다른 사람(RunLink.Together)을 봇 몸을 복제해 그린다. 자리는 RemoteRunner가 내다본 위치
    //  4. 매 프레임 내 거리·속도를 RunLink에 적는다(HUD·실시간 방이 읽음)
    public sealed class AppRunBridge : MonoBehaviour
    {
        static readonly Color[] Shirts =
        {
            new Color(0.20f, 0.55f, 0.90f), new Color(0.30f, 0.75f, 0.35f), new Color(0.92f, 0.26f, 0.22f),
            new Color(0.55f, 0.35f, 0.85f), new Color(0.95f, 0.85f, 0.25f), new Color(0.15f, 0.70f, 0.75f),
            new Color(0.95f, 0.45f, 0.70f), new Color(0.98f, 0.62f, 0.15f),
        };
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        RunnerTag player;
        RouteTrack route;
        GameObject template; // 꺼 둔 봇 하나(다른 사람 몸 원본)
        Transform remoteRoot;
        readonly Dictionary<string, RemoteBody> bodies = new Dictionary<string, RemoteBody>();
        readonly List<string> gone = new List<string>();
        readonly HashSet<string> seen = new HashSet<string>();
        MaterialPropertyBlock block;

        public static void Attach(Scene scene)
        {
            var go = new GameObject("AppRunBridge");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<AppRunBridge>();
        }

        void Awake()
        {
            player = RunnerTag.Player;
            route = FindInScene<RouteTrack>();
            if (player == null || player.runner == null || route == null)
            {
                Debug.LogError("[같이 뛰기] 섬 씬에서 플레이어나 코스를 못 찾음 → 속도·다른 러너 연결 안 함");
                enabled = false;
                return;
            }
            UseSensorSpeed();
            HideIslandRunners();
            remoteRoot = new GameObject("RemoteRunners").transform;
            remoteRoot.SetParent(transform, false);
        }

        void UseSensorSpeed()
        {
            foreach (var fake in FindObjectsByType<FakeSpeedSource>(FindObjectsInactive.Include))
                if (fake.gameObject.scene == gameObject.scene) fake.gameObject.SetActive(false); // 키보드 ↑/↓가 두 번 먹지 않게
            var go = player.runner.gameObject;
            var native = go.AddComponent<NativeSpeedSource>();
            var gated = go.AddComponent<GatedSpeed>();
            gated.inner = native;
            player.runner.SetSpeedSource(gated);
        }

        void HideIslandRunners()
        {
            var bots = new List<RunnerTag>();
            var ghosts = new List<RunnerTag>();
            foreach (var t in RunnerTag.All)
            {
                if (t.kind == RunnerTag.Kind.Bot) bots.Add(t);
                else if (t.kind == RunnerTag.Kind.Ghost) ghosts.Add(t);
            }
            foreach (var t in bots)
            {
                if (template == null && !t.crowd) template = t.gameObject;
                t.gameObject.SetActive(false);
            }
            // 섬의 고스트는 이 기기에 남은 섬 단독 기록을 씬이 열리자마자 재생하는 시험용(달리기 시작·서버 기록과 상관없음).
            // 진짜 고스트(서버의 내 지난 기록, 지환)가 들어오면 그쪽이 다시 켠다
            foreach (var t in ghosts) t.gameObject.SetActive(false);
            // 녹화기도 끈다: 켜 두면 앱에서 1분 넘게 달린 기록이 섬 단독용 고스트 파일에 저장돼 다음 섬 Play에서 재생됨
            var recorder = player.runner.GetComponent<GhostRecorder>();
            if (recorder != null) recorder.enabled = false;
            if (template == null && bots.Count > 0) template = bots[0].gameObject;
            if (template == null) Debug.LogWarning("[같이 뛰기] 복제할 봇 몸이 없음 → 다른 사람이 안 보임");
        }

        void Update()
        {
            var me = player.runner;
            float lap = route.LapLength;
            // 입장 위치로 옮기기 전(MapLaunchApplier가 한 프레임 뒤에 옮김)의 자리를 알리지 않게
            if (!MapLaunch.Applied.IsCompleted) return;
            RunLink.ReportPlayer(me.Distance, me.SpeedKmh, lap);
            SyncRemotes(me.Distance, lap);
        }

        void SyncRemotes(float myDistance, float lap)
        {
            var together = RunLink.Together;
            seen.Clear();
            if (together != null && template != null && lap > 0f)
            {
                WarnIfLapDiffers(together.CourseLengthM, lap);
                double now = RealtimeClock.Now();
                float myLap = LoopMath.Wrap(myDistance, lap);
                foreach (var r in together.Runners)
                {
                    seen.Add(r.UserId);
                    if (!bodies.TryGetValue(r.UserId, out var body)) bodies[r.UserId] = body = Spawn(r);
                    // 내 누적 거리 근처로 펴서 놓음(이름표 간격 계산이 내 거리와 같은 기준이 되게)
                    float d = myDistance + LoopMath.Delta(myLap, r.PositionAt(now), lap);
                    if (d < 0f) d += lap;
                    body.speed.kmh = r.Running ? r.SpeedKmh : 0f;
                    body.runner.Teleport(d);
                }
            }
            gone.Clear();
            foreach (var id in bodies.Keys)
                if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                Destroy(bodies[id].runner.gameObject);
                bodies.Remove(id);
            }
        }

        bool lapChecked;

        // 위치는 섬 코스 길이로 감고(내 d), 서버·다른 러너는 서버 코스 길이로 감는다. 서버 길이는 섬과 같은 계산으로 만든 정수라
        // 1m 안쪽이면 시작선 근처에서도 문제없음(계획 0029). 크게 다르면 섬 맵과 서버 코스(seed_courses)가 어긋난 것
        void WarnIfLapDiffers(float serverLap, float islandLap)
        {
            if (lapChecked || serverLap <= 0f) return;
            lapChecked = true;
            if (Mathf.Abs(serverLap - islandLap) > 2f)
                Debug.LogWarning($"[같이 뛰기] 코스 길이가 다름: 섬 {islandLap:0.0}m, 서버 {serverLap:0.0}m → 시작선 근처에서 다른 러너 위치가 틀림. " +
                                 "서버 코스 길이(seed_courses)를 섬 맵에 맞추기");
        }

        RemoteBody Spawn(RemoteRunner r)
        {
            var go = Instantiate(template, remoteRoot); // 원본이 꺼져 있어 복제도 꺼진 채(Awake 전)
            go.name = "Remote_" + r.Nickname;
            foreach (var pace in go.GetComponents<PaceSpeed>()) DestroyImmediate(pace);
            var speed = go.AddComponent<FixedSpeed>();
            var rc = go.GetComponent<RunnerController>();
            int lane = r.UserId.GetHashCode() & 0x7fffffff; // Mathf.Abs(int.MinValue)는 넘침
            rc.Setup(speed, route, -2.4f + lane % 9 * 0.6f, 0f);
            rc.Driven = true;
            var color = Shirts[lane % Shirts.Length];
            var tag = go.GetComponent<RunnerTag>();
            tag.kind = RunnerTag.Kind.Remote;
            tag.crowd = false;
            tag.displayName = r.Nickname;
            tag.color = color;
            block ??= new MaterialPropertyBlock();
            foreach (var smr in go.GetComponentsInChildren<Renderer>(true))
            {
                smr.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                smr.SetPropertyBlock(block);
            }
            go.SetActive(true);
            return new RemoteBody { runner = rc, speed = speed };
        }

        void OnDestroy() => RunLink.IslandGone();

        T FindInScene<T>() where T : Component
        {
            foreach (var c in FindObjectsByType<T>())
                if (c.gameObject.scene == gameObject.scene) return c;
            return null;
        }

        struct RemoteBody
        {
            public RunnerController runner;
            public FixedSpeed speed;
        }

        // 달리기 전엔 0(카운트다운 전에 아바타가 먼저 가지 않게)
        sealed class GatedSpeed : MonoBehaviour, ISpeedSource
        {
            public NativeSpeedSource inner;
            public float SpeedKmh => RunLink.Moving && inner != null ? inner.SpeedKmh : 0f;
        }

        // 다른 사람: 받은 속도(애니메이션용). 자리는 Teleport로
        sealed class FixedSpeed : MonoBehaviour, ISpeedSource
        {
            public float kmh;
            public float SpeedKmh => kmh;
        }
    }
}
