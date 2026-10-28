using UnityEngine;

namespace Scrun
{
    // 러너 속도(km/h) -> Animator 블렌드 파라미터.
    // 블렌드 트리 기준점이 각 동작의 원래 이동 속도라 그 범위 안에선 발이 바닥과 맞는다.
    // 가장 빠른 동작보다 빠르면 재생 속도를 올려 맞춘다.
    [RequireComponent(typeof(Animator))]
    public class RunnerAnimator : MonoBehaviour
    {
        public static readonly int SpeedParam = Animator.StringToHash("SpeedKmh");

        [SerializeField] RunnerController runner;
        [SerializeField] float maxClipKmh = 18f;   // 가장 빠른 동작의 기준점(씬 빌더가 측정값으로 채움)
        [SerializeField] float dampTime = 0.15f;

        Animator animator;

        void Awake()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;   // 이동은 RunnerController가 담당
        }

        void Update()
        {
            if (!runner) return;
            float kmh = runner.SpeedKmh;
            animator.SetFloat(SpeedParam, Mathf.Min(kmh, maxClipKmh), dampTime, Time.deltaTime);
            animator.speed = kmh > maxClipKmh ? kmh / maxClipKmh : 1f;
        }
    }
}
