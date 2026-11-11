using System.Collections;
using Scrun.App;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scrun
{
    // 앱 맵 화면이 섬 씬을 불러올 때 남긴 코스·입장 위치(Scrun.App.MapLaunch)를 적용한다(계획 0022). 달리기 연결은 AppRunBridge(계획 0029).
    // 씬 파일을 고치지 않도록(섬 씬은 각자 Build Island Scene으로 다시 만듦) 씬이 불릴 때 코드로 붙는다.
    // 앱 없이 섬 씬만 Play하면 남긴 것이 없어서 아무것도 안 함(씬의 기본 코스 그대로).
    public sealed class MapLaunchApplier : MonoBehaviour
    {
        MapLaunch.Request request;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != MapLaunch.SceneName || !MapLaunch.HasPending) return;
            var go = new GameObject("MapLaunchApplier");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<MapLaunchApplier>().request = MapLaunch.Take();
            HideDevTools(scene);
            AppRunBridge.Attach(scene); // 진짜 속도 → 내 아바타, 가짜 봇 끄기, 다른 사람 그리기(계획 0029)
        }

        // 섬 씬만 Play할 때 쓰는 개발 도구(왼쪽 위 속도판, 아래 단축키 안내·순간이동, 폰 테스트 버튼, 오른쪽 위 근처 러너 목록)는
        // 앱에서 들어오면 끈다. 앱 화면(나가기·코스 이름)과 겹치고, 속도·거리 표시는 앱 HUD(S3)가 맡는다. 머리 위 이름표는 남김
        static void HideDevTools(Scene scene)
        {
            Disable<SpeedHud>(scene);
            Disable<DevTravel>(scene);
            Disable<MobileTestControls>(scene);
            foreach (var l in FindObjectsByType<RunnerLabels>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.gameObject.scene == scene) l.showNearbyList = false;
        }

        static void Disable<T>(Scene scene) where T : Behaviour
        {
            foreach (var c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.gameObject.scene == scene) c.enabled = false;
        }

        // 씬의 러너·봇이 Start에서 자리를 잡은 뒤에 옮긴다(한 프레임 뒤)
        IEnumerator Start()
        {
            yield return null;
            try { Apply(); }
            finally { MapLaunch.MarkApplied(); } // 맵 화면이 이걸 기다렸다 흐린 배경을 찍음(실패해도 알림)
            Destroy(gameObject);
        }

        void Apply()
        {
            var scene = gameObject.scene;
            var route = Find<RouteTrack>(scene);
            var player = RunnerTag.Player;
            if (route == null || player == null || player.runner == null)
            {
                Debug.LogError("[MapLaunch] 섬 씬에서 코스(RouteTrack)나 플레이어를 못 찾음 → 기본 코스로 시작");
                return;
            }
            if (!route.HasRoute(request.RouteId))
            {
                Debug.LogError($"[MapLaunch] 도로망에 없는 코스: {request.RouteId}({request.CourseId}) → 기본 코스로 시작");
                return;
            }

            route.SetRoute(request.RouteId);
            float d = Mathf.Repeat(request.EntryPositionM, route.LapLength); // 서버와 같은 계산이라 한 바퀴 안이지만 혹시 몰라
            player.runner.Teleport(d);
            RunnerTag.GatherBotsAround(d);
            var follow = Find<FollowCamera>(scene);
            if (follow != null) follow.Snap();
            Debug.Log($"[MapLaunch] {route.RouteName}({request.RouteId}) {d:0}m에서 시작");
        }

        static T Find<T>(Scene scene) where T : Component
        {
            foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None))
                if (c.gameObject.scene == scene) return c;
            return null;
        }
    }
}
