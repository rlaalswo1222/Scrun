using UnityEngine;
using UnityEngine.InputSystem;

namespace Scrun.Experiments
{
    // 아바타 걷기·달리기 실험(팀 코드와 별개, 결과를 보고 RunnerAnimator에 제안).
    // 걷기 무리(Walk)와 달리기 무리(Run)를 섞지 않고 둘 중 하나만 재생한다. 속도가 경계를 넘으면 짧게 교차 전환.
    //  - 달리기 시작: runAboveKmh 이상, 걷기로 돌아감: walkBelowKmh 이하(사이는 지금 상태 유지 = 깜빡임 방지)
    //  - 각 무리 안에서는 블렌드 값 = 속도(기준점은 이 아바타로 잰 발 속도). 기준 범위를 벗어나면 재생 속도로 맞춘다
    //  - 발 미끄러짐을 줄이려고 재생 속도 = 속도 / 그 블렌드의 발 속도
    // 이동은 이 스크립트가 직접(앞으로 속도만큼). 루트 모션은 쓰지 않는다.
    [RequireComponent(typeof(Animator))]
    public class AvatarGaitDriver : MonoBehaviour
    {
        public enum Mode { Sweep, Keys, Fixed, External }

        public static readonly int SpeedParam = Animator.StringToHash("SpeedKmh");
        static readonly int WalkState = Animator.StringToHash("Walk");
        static readonly int RunState = Animator.StringToHash("Run");
        static readonly int IdleState = Animator.StringToHash("Idle");

        [Header("속도")]
        public Mode mode = Mode.Sweep;
        public float fixedKmh = 6f;
        public float sweepMaxKmh = 14f;
        public float sweepSeconds = 40f;       // 0 → 최대 → 0 한 바퀴
        [HideInInspector] public float speedKmh;

        [Header("걷기·달리기 경계 (Hreljac 외 연구 6.8–7.8 km/h, 가운데를 비워 깜빡임 방지)")]
        public float runAboveKmh = 7.5f;
        public float walkBelowKmh = 6.5f;
        public float crossFade = 0.25f;
        public float stopBelowKmh = 0.3f, startAboveKmh = 0.5f;

        [Header("이 아바타로 잰 발 속도(빌더가 채움)")]
        public float walkMinKmh = 4f, walkMaxKmh = 4f;     // 걷기 무리 기준점 범위
        public float runMinKmh = 6f, runMaxKmh = 18f;      // 달리기 무리 기준점 범위

        [Header("체형 보정(머리 크고 다리 짧은 체형). 달리기일 때만 runW만큼 들어감")]
        [Range(0, 30)] public float armOutDeg = 0f;        // 걷기·달리기 공통: 위팔을 몸 바깥으로
        [Range(0, 30)] public float runArmOutDeg = 0f;     // 달리기 추가: 손이 가슴 앞에서 교차하지 않게
        [Range(0, 25)] public float runSpineBackDeg = 0f;  // 달리기: 몸통을 뒤로 세움(앞으로 숙인 자세 줄이기)
        [Range(0, 25)] public float runHeadUpDeg = 0f;     // 달리기: 고개를 들어 얼굴이 앞을 보게
        float runW;
        Transform spine, head;

        public bool moveForward = true;
        [Tooltip("Mode = External일 때 속도를 읽을 러너(맵의 Runner). 이동은 러너가 하므로 moveForward는 끈다")]
        public RunnerController runner;
        public string gaitLabel = "서 있기";

        Animator an;
        int state;   // 0 서 있기, 1 걷기, 2 달리기
        float t;
        Transform lUpper, rUpper;

        void Awake()
        {
            an = GetComponent<Animator>();
            an.applyRootMotion = false;
            lUpper = an.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rUpper = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            spine = an.GetBoneTransform(HumanBodyBones.Spine);
            head = an.GetBoneTransform(HumanBodyBones.Head);
        }

        void Update()
        {
            t += Time.deltaTime;
            switch (mode)
            {
                case Mode.Sweep:
                    float p = Mathf.PingPong(t / (sweepSeconds * 0.5f), 1f);
                    speedKmh = Mathf.Round(p * sweepMaxKmh * 10f) / 10f;
                    break;
                case Mode.Keys:
                    var kb = Keyboard.current;
                    if (kb != null && kb.upArrowKey.wasPressedThisFrame) speedKmh += 0.5f;
                    if (kb != null && kb.downArrowKey.wasPressedThisFrame) speedKmh = Mathf.Max(0, speedKmh - 0.5f);
                    break;
                case Mode.External:
                    speedKmh = runner ? runner.SpeedKmh : 0f;
                    break;
                default:
                    speedKmh = fixedKmh;
                    break;
            }

            int next = state;
            if (state == 0 && speedKmh >= startAboveKmh) next = speedKmh >= runAboveKmh ? 2 : 1;
            else if (state == 1 && speedKmh >= runAboveKmh) next = 2;
            else if (state == 1 && speedKmh <= stopBelowKmh) next = 0;
            else if (state == 2 && speedKmh <= walkBelowKmh) next = speedKmh <= stopBelowKmh ? 0 : 1;
            if (next != state)
            {
                state = next;
                an.CrossFadeInFixedTime(state == 2 ? RunState : state == 1 ? WalkState : IdleState, crossFade);
            }

            float blend, play = 1f;
            if (state == 1) blend = Mathf.Clamp(speedKmh, walkMinKmh, walkMaxKmh);
            else if (state == 2) blend = Mathf.Clamp(speedKmh, runMinKmh, runMaxKmh);
            else blend = 0f;
            if (state != 0) play = Mathf.Clamp(speedKmh / Mathf.Max(blend, 0.1f), 0.05f, 2.5f);
            an.SetFloat(SpeedParam, blend);
            an.speed = play;
            gaitLabel = state == 2 ? "달리기" : state == 1 ? "걷기" : "서 있기";

            if (moveForward) transform.position += transform.forward * (speedKmh / 3.6f) * Time.deltaTime;
            runW = Mathf.MoveTowards(runW, state == 2 ? 1f : 0f, Time.deltaTime / Mathf.Max(crossFade, 0.01f));
        }

        // 애니메이션이 끝난 뒤 체형 보정. 축은 캐릭터 기준(오른쪽 축 = 앞뒤로 숙이기, 앞 축 = 팔 벌리기)
        void LateUpdate()
        {
            var right = transform.right;
            var fwd = transform.forward;
            float back = runSpineBackDeg * runW;
            if (spine != null && back > 0) spine.rotation = Quaternion.AngleAxis(-back, right) * spine.rotation;
            float up = runHeadUpDeg * runW;
            if (head != null && up > 0) head.rotation = Quaternion.AngleAxis(-up, right) * head.rotation;
            float arm = armOutDeg + runArmOutDeg * runW;
            if (arm > 0 && lUpper != null && rUpper != null)
            {
                lUpper.rotation = Quaternion.AngleAxis(-arm, fwd) * lUpper.rotation;
                rUpper.rotation = Quaternion.AngleAxis(arm, fwd) * rUpper.rotation;
            }
        }
    }
}
