using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scrun
{
    // 앱에서 우주 루프(SpaceLoop) 코스로 들어올 때, 섬 씬이 장면에 미리 갖고 있던 것을 코드로 붙인다(계획 0066).
    // 장면 파일은 상준이 계속 고치므로 건드리지 않는다(섬의 MapLaunchApplier와 같은 방식). 부르는 곳: MapLaunchApplier.OnSceneLoaded
    //  - 장면의 러너에 플레이어 표식(RunnerTag) → AppRunBridge·MapLaunchApplier가 찾음
    //  - 다른 사람 몸·고스트 몸 원본: 플레이어 러너(새 아바타 + AvatarGaitDriver)를 복제해 꺼 둠. 섬은 섬 봇을 원본으로 씀
    //  - 품질 단계(ScrunQuality): 30fps·렌더 배율·그림자·화면 꺼짐 방지·달리는 중 품질 낮추기(I-12)
    //  - 개발 도구(구간 키·아바타 상태 글자·키보드 속도)는 끔: 앱 HUD와 진짜 센서가 맡음
    public sealed class SpaceLoopAppSetup : MonoBehaviour
    {
        const float GhostLane = 1.2f;   // 고스트는 내 오른쪽 옆 줄(내 몸과 겹치지 않게)
        const string GhostMaterial = "RunnerGhost"; // Resources/RunnerGhost.mat(섬 Runner_Ghost와 같은 반투명)

        public GameObject Body { get; private set; }   // 다른 사람 몸 원본(꺼져 있음)
        public RunnerTag Ghost { get; private set; }   // 고스트 몸(꺼져 있음, 앱 고스트가 켬)

        public static void Prepare(Scene scene)
        {
            var runner = Find<RunnerController>(scene);
            var track = Find<Track>(scene);
            if (runner == null || track == null)
            {
                Debug.LogError("[우주 루프] 장면에서 러너나 코스(Track)를 못 찾음 → 앱 연결 안 함");
                return;
            }
            var go = new GameObject("SpaceLoopAppSetup");
            SceneManager.MoveGameObjectToScene(go, scene);
            var setup = go.AddComponent<SpaceLoopAppSetup>();

            Disable<SpaceLoopDevKeys>(scene);
            Disable<Experiments.AvatarGaitHud>(scene); // 화면 위 "아바타: 걷기/달리기" 실험 글자
            // 키보드 ↑/↓ 속도는 끔. 러너와 같은 오브젝트라 오브젝트가 아니라 컴포넌트만(AppRunBridge가 진짜 센서로 바꿈)
            foreach (var fake in runner.GetComponents<FakeSpeedSource>()) fake.enabled = false;

            // 복제는 플레이어 표식을 달기 전에(복제본이 플레이어로 등록되지 않게), 꺼진 상자 안에서(복제본 Awake가 돌지 않게)
            var holder = new GameObject("BodyTemplates");
            SceneManager.MoveGameObjectToScene(holder, scene);
            holder.SetActive(false);
            // 원본 표식은 Remote: Bot이면 ScrunQuality가 "섬 봇"으로 보고 다시 켬(복제본이 켜진 채 나와 설정 전에 Awake가 돎)
            setup.Body = MakeBody(runner, holder.transform, "Body_Template", RunnerTag.Kind.Remote, null);
            var ghostRc = MakeBody(runner, holder.transform, "Body_Ghost", RunnerTag.Kind.Ghost, Resources.Load<Material>(GhostMaterial))
                .GetComponent<RunnerController>();
            ghostRc.Setup(null, track, GhostLane, 0f); // 속도 소스는 앱 고스트가 켤 때 넣음(AppRunBridge.SyncGhost)
            setup.Ghost = ghostRc.GetComponent<RunnerTag>();
            setup.Ghost.transform.SetParent(null, false);   // 고스트는 상자 밖에서 켜짐(SyncGhost가 SetActive(true))
            SceneManager.MoveGameObjectToScene(setup.Ghost.gameObject, scene);

            var tag = runner.gameObject.AddComponent<RunnerTag>();
            tag.kind = RunnerTag.Kind.Player;
            tag.runner = runner;

            var quality = new GameObject("ScrunQuality");
            SceneManager.MoveGameObjectToScene(quality, scene);
            quality.AddComponent<ScrunQuality>();
        }

        // 플레이어 러너를 복제해 꺼 둔 몸. 키보드 속도는 떼고, 원하면 모든 재질을 반투명으로
        static GameObject MakeBody(RunnerController source, Transform holder, string name, RunnerTag.Kind kind, Material material)
        {
            var go = Instantiate(source.gameObject, holder);
            go.name = name;
            go.SetActive(false); // 상자에서 꺼낼 때도 꺼진 채로(AppRunBridge.Spawn이 복제 → 설정 → 켬)
            foreach (var fake in go.GetComponents<FakeSpeedSource>()) DestroyImmediate(fake);
            var tag = go.AddComponent<RunnerTag>();
            tag.kind = kind;
            tag.runner = go.GetComponent<RunnerController>();
            if (material != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                }
            return go;
        }

        static void Disable<T>(Scene scene) where T : Behaviour
        {
            foreach (var c in FindObjectsByType<T>(FindObjectsInactive.Include))
                if (c.gameObject.scene == scene) c.enabled = false;
        }

        static T Find<T>(Scene scene) where T : Component
        {
            foreach (var c in FindObjectsByType<T>(FindObjectsInactive.Include))
                if (c.gameObject.scene == scene) return c;
            return null;
        }
    }
}
