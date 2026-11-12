using Scrun.App;
using UnityEngine;

namespace Scrun
{
    // 러너 속도(km/h) -> Animator 블렌드 값과 재생 속도.
    // 발 속도 표(Resources/RunnerGait, 계획 0027)가 있으면 그 블렌드의 실제 발 속도로 재생 속도를 나눠 디딘 발을 바닥에 붙인다.
    // 걷는 동안엔 서 있기와 섞지 않고(0에서 3 km/h 미끄러짐), 느리면 걷기를 느리게 재생한다.
    // 멈추면 블렌드가 서 있기로 내려오는 동안 동작을 그 자리에 세운다(다리만 달리지 않게). 플레이어·봇·고스트가 모두 이걸 쓴다.
    // 표가 없으면 예전처럼: 블렌드 값 = 속도, 가장 빠른 동작보다 빠르면 재생 속도만 올림.
    [RequireComponent(typeof(Animator))]
    public class RunnerAnimator : MonoBehaviour
    {
        public static readonly int SpeedParam = Animator.StringToHash("SpeedKmh");

        [SerializeField] RunnerController runner;
        [SerializeField] float maxClipKmh = 18f;   // 가장 빠른 동작의 기준점(씬 빌더가 측정값으로 채움). 표가 없을 때만 씀
        [SerializeField] float dampTime = 0.15f;

        Animator animator;
        RunnerGait gait;
        bool moving;

        static bool warned; // 표가 잘못됐다는 경고는 러너 수만큼이 아니라 한 번만

        // 도메인 리로드를 끈 Play에서 지난 Play의 값이 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => warned = false;

        void Awake()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;   // 이동은 RunnerController가 담당
            gait = Resources.Load<RunnerGait>(RunnerGait.ResourcePath);
            if (gait != null && !gait.IsValid)
            {
                if (!warned) Debug.LogWarning("[RunnerAnimator] 발 속도 표가 비었거나 잘못됨 → 표 없이(Play 중 Scrun/Measure Runner Gait로 다시 만들기)");
                warned = true;
                gait = null;
            }
        }

        void Update()
        {
            if (!runner) return;
            float kmh = runner.SpeedKmh;
            if (gait == null)
            {
                animator.SetFloat(SpeedParam, Mathf.Min(kmh, maxClipKmh), dampTime, Time.deltaTime);
                animator.speed = kmh > maxClipKmh ? kmh / maxClipKmh : 1f;
                return;
            }

            moving = RunnerGait.IsMoving(kmh, moving);
            animator.SetFloat(SpeedParam, gait.TargetParam(kmh, moving), dampTime, Time.deltaTime);
            animator.speed = gait.PlaybackSpeed(kmh, moving, animator.GetFloat(SpeedParam));
        }
    }
}
