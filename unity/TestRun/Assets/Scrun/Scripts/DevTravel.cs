using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Scrun
{
    // 개발용 이동 도구(출시 빌드에선 끔): 지역 바로가기, 코스 바꾸기, 앞뒤 건너뛰기, 자유 비행, 섬 전체 보기
    //  1~5 지역 / Tab 코스 / [ ] ±250m / F 자유 비행 / M 섬 전체
    public class DevTravel : MonoBehaviour
    {
        [SerializeField] RoadNetwork network;
        [SerializeField] RouteTrack route;
        [SerializeField] RunnerController runner;
        [SerializeField] FollowCamera follow;
        [SerializeField] float jump = 250f;

        enum CamMode { Follow, Fly, Overview }
        CamMode mode = CamMode.Follow;
        float yaw, pitch;
        string message = "";
        float messageUntil;
        GUIStyle style;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !network || !route || !runner) return;

            for (int i = 0; i < 5 && i < network.regions.Count; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) GoToRegion(network.regions[i]);

            if (kb.tabKey.wasPressedThisFrame) NextRoute();
            if (kb.rightBracketKey.wasPressedThisFrame) Jump(jump);
            if (kb.leftBracketKey.wasPressedThisFrame) Jump(-jump);
            if (kb.fKey.wasPressedThisFrame) SetMode(mode == CamMode.Fly ? CamMode.Follow : CamMode.Fly);
            if (kb.mKey.wasPressedThisFrame) SetMode(mode == CamMode.Overview ? CamMode.Follow : CamMode.Overview);

            if (mode == CamMode.Fly) Fly(kb);
        }

        // 터치 버튼(폰 테스트용)에서 호출
        public int RegionCount => network ? network.regions.Count : 0;
        public void GoToRegion(int index) { if (index >= 0 && index < RegionCount) GoToRegion(network.regions[index]); }
        public void NextRouteFromUI() => NextRoute();

        void GoToRegion(RoadNetwork.Region region)
        {
            // 지역 중심에서 가장 가까운 코스 지점(모든 코스 중) 찾기
            string bestRoute = route.RouteId;
            float bestDist = 0f, best = float.MaxValue;
            foreach (var r in network.routes)
            {
                var line = network.BuildRoute(r.id);
                for (float d = 0; d < line.Length; d += 10f)
                {
                    var p = line.GetPoint(d);
                    float sq = (p.x - region.center.x) * (p.x - region.center.x) + (p.z - region.center.z) * (p.z - region.center.z);
                    if (sq < best) { best = sq; bestRoute = r.id; bestDist = d; }
                }
            }
            route.SetRoute(bestRoute);
            runner.Teleport(bestDist);
            RunnerTag.GatherBotsAround(bestDist);
            SetMode(CamMode.Follow);
            Say($"{region.name} → {route.RouteName} {bestDist / 1000f:0.00}km");
        }

        void NextRoute()
        {
            int i = network.routes.FindIndex(r => r.id == route.RouteId);
            var next = network.routes[(i + 1) % network.routes.Count];
            route.SetRoute(next.id);
            runner.Teleport(0f);
            RunnerTag.GatherBotsAround(0f);
            SetMode(CamMode.Follow);
            Say($"코스: {next.name} ({route.LapLength / 1000f:0.00}km)");
        }

        void Jump(float meters)
        {
            float d = Mathf.Repeat(runner.Distance + meters, route.LapLength);
            runner.Teleport(d);
            RunnerTag.GatherBotsAround(d);
            if (mode == CamMode.Follow) follow.Snap();
            Say($"{d / 1000f:0.00}km");
        }

        float baseFog = -1f;

        void SetMode(CamMode m)
        {
            // 섬 전체 보기는 3km 밖에서 보므로 안개를 옅게(돌아오면 원래대로)
            if (baseFog < 0f) baseFog = RenderSettings.fogDensity;
            RenderSettings.fogDensity = m == CamMode.Overview ? baseFog * 0.15f : baseFog;
            mode = m;
            var cam = follow.transform;
            follow.enabled = m == CamMode.Follow;
            if (m == CamMode.Follow) follow.Snap();
            if (m == CamMode.Fly)
            {
                var e = cam.eulerAngles;
                yaw = e.y; pitch = e.x > 180f ? e.x - 360f : e.x;
            }
            if (m == CamMode.Overview)
            {
                // 모든 도로를 감싸는 범위 위에서 비스듬히 내려다봄
                var b = new Bounds(network.nodes[0].position, Vector3.zero);
                foreach (var n in network.nodes) b.Encapsulate(n.position);
                float span = Mathf.Max(b.size.x, b.size.z);
                cam.position = b.center + new Vector3(0f, span * 0.75f, -span * 0.95f);
                cam.LookAt(b.center);
            }
        }

        void Fly(Keyboard kb)
        {
            var cam = follow.transform;
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue() * 0.15f;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
                cam.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            var move = Vector3.zero;
            if (kb.wKey.isPressed) move += cam.forward;
            if (kb.sKey.isPressed) move -= cam.forward;
            if (kb.dKey.isPressed) move += cam.right;
            if (kb.aKey.isPressed) move -= cam.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move -= Vector3.up;
            float speed = kb.leftShiftKey.isPressed ? 150f : 30f;
            cam.position += move * speed * Time.unscaledDeltaTime;
        }

        void Say(string text)
        {
            message = text;
            messageUntil = Time.unscaledTime + 3f;
        }

        string CurrentRegion()
        {
            var p = runner.transform.position;
            RoadNetwork.Region best = null;
            float bestSq = float.MaxValue;
            foreach (var r in network.regions)
            {
                float sq = (p - r.center).sqrMagnitude;
                if (sq < r.radius * r.radius * 1.6f && sq < bestSq) { bestSq = sq; best = r; }
            }
            return best != null ? best.name : "-";
        }

        void OnGUI()
        {
            if (!network || !route || !runner) return;
            if (Application.isMobilePlatform) return;   // 폰에선 키보드 안내 대신 MobileTestControls
            style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 48) };
            float fs = style.fontSize;
            var lines = new List<string>
            {
                $"코스: {route.RouteName}  {runner.Distance % route.LapLength / 1000f:0.00} / {route.LapLength / 1000f:0.00}km   지역: {CurrentRegion()}",
                "1 항구  2 호수  3 협곡  4 풍차  5 절벽  |  Tab 코스  [ ] ±250m  |  F 자유비행(WASD·우클릭·Shift·Q/E)  M 섬 전체  T 시간대",
            };
            if (Time.unscaledTime < messageUntil) lines.Add(message);
            float h = fs * 1.6f * lines.Count + 12f;
            var rect = new Rect(10, Screen.height - h - 10, Screen.width - 20, h);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height), string.Join("\n", lines), style);
        }
    }
}
