using Scrun;
using UnityEditor;
using UnityEngine;
using static ScrunEditorUtil;

// 씬에 러너 한 세트 배치: 속도 소스(키보드) + 러너(이동) + 캐릭터(애니메이션) + 따라가는 카메라 + HUD
public static class RunnerRigBuilder
{
    public const string CharacterModelPath = Root + "/Characters/YBot/Y Bot@Breathing Idle.fbx";

    // controller: RunnerAnimatorBuilder.Build() 결과(씬 만들기 전에 미리 생성해 둘 것)
    public static RunnerController Create(Track track, RuntimeAnimatorController controller, float groundHeight = 0f)
    {
        var fake = new GameObject("SpeedSource (Keyboard)").AddComponent<FakeSpeedSource>();

        // 러너: 빈 루트(이동) + 자식 모델(애니메이션)
        var runnerGo = new GameObject("Runner");
        var start = track.GetPoint(0f);
        runnerGo.transform.SetPositionAndRotation(new Vector3(start.x, groundHeight, start.z),
                                                  Quaternion.LookRotation(track.GetForward(0f)));
        var runner = runnerGo.AddComponent<RunnerController>();
        SetRef(runner, "speedSourceBehaviour", fake);
        SetRef(runner, "track", track);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath);
        if (model && controller)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.name = "YBot";
            body.transform.SetParent(runnerGo.transform, false);
            var animator = body.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            var ra = body.AddComponent<RunnerAnimator>();
            SetRef(ra, "runner", runner);
            SetFloat(ra, "maxClipKmh", RunnerAnimatorBuilder.TopSpeedKmh);
            SetRef(body.AddComponent<FootSlipProbe>(), "runner", runner);
        }
        else
        {
            // 캐릭터가 없으면 캡슐로 대체
            Debug.LogWarning($"[Scrun] {CharacterModelPath} 없음 → 캡슐 사용");
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Capsule";
            Object.DestroyImmediate(capsule.GetComponent<Collider>());
            capsule.transform.SetParent(runnerGo.transform, false);
            capsule.transform.localScale = Vector3.one * 0.9f; // 키 약 1.8m
            capsule.transform.localPosition = Vector3.up * 0.9f;
            capsule.GetComponent<Renderer>().sharedMaterial = MakeMat("Runner", new Color(0.95f, 0.45f, 0.15f));
        }

        // 카메라
        var cam = Camera.main;
        cam.transform.position = runnerGo.transform.position - runnerGo.transform.forward * 4.6f + Vector3.up * 2.2f;
        cam.transform.LookAt(runnerGo.transform.position + runnerGo.transform.forward * 10f + Vector3.up * 1.2f);
        cam.fieldOfView = 58f;
        var follow = cam.gameObject.AddComponent<FollowCamera>();
        SetRef(follow, "target", runnerGo.transform);
        SetRef(follow, "runner", runner);

        // HUD + 폰 테스트용 터치 버튼(폰에서만 보임)
        SetRef(new GameObject("HUD").AddComponent<SpeedHud>(), "runner", runner);
        SetRef(new GameObject("MobileTestControls").AddComponent<MobileTestControls>(), "speed", fake);
        return runner;
    }
}
