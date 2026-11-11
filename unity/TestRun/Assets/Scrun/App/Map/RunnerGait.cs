using System;
using UnityEngine;

namespace Scrun.App
{
    // 아바타 달리기 동작의 "블렌드 값 → 실제 발 속도" 표(계획 0027). 섬 쪽 RunnerAnimator가 Resources에서 읽어 재생 속도를 맞춘다.
    // 블렌드 트리 기준점(클립 분석값)과 실행 중 Animator의 실제 발 속도가 달라서(걷기 3.5 vs 6.2 km/h, 4.5에서 8 km/h는 6 근처로 거의 그대로)
    // 디딘 발이 미끄러졌다(11/11 땅에 닿은 점 기준: 1 km/h 80%, 8 km/h 28%). 실제 발 속도로 재생 속도를 나누면 발이 바닥에 붙는다(전 속도 5% 이내).
    // 표는 Play 중 메뉴 Scrun/Measure Runner Gait로 만든다(캐릭터·동작·블렌드 기준점이 바뀌면 다시).
    // 계산만 여기 두는 이유: 섬 스크립트 묶음은 테스트 묶음에서 부를 수 없다(MapLaunch와 같은 이음매).
    public sealed class RunnerGait : ScriptableObject
    {
        public const string ResourcePath = "RunnerGait";
        public const float StartKmh = 0.5f; // 서 있다가 이보다 빨라지면 걷기 시작
        public const float StopKmh = 0.3f;  // 걷다가 이보다 느려지면 서 있기(문턱 둘: 그 근처에서 떨지 않게)
        public const float StandingBlend = 0.5f; // 블렌드 값이 이 아래면 거의 서 있기 동작(걷기 비중 15% 이하)
        const float MinPlayback = 0.05f, MaxPlayback = 3f; // 0.05배 × 걷기 발 속도 약 6 km/h = 0.3 km/h(StopKmh)까지 발이 붙음

        [SerializeField] float[] param = Array.Empty<float>();   // 블렌드 값(km/h), 오름차순. 첫 값 = 걷기 기준점
        [SerializeField] float[] footKmh = Array.Empty<float>(); // 그 블렌드를 1배로 재생할 때 디딘 발이 땅을 미는 속도

        public bool IsValid
        {
            get
            {
                if (param == null || footKmh == null || param.Length < 2 || param.Length != footKmh.Length) return false;
                for (int i = 0; i < param.Length; i++)
                {
                    if (footKmh[i] <= 0f) return false;
                    if (i > 0 && param[i] <= param[i - 1]) return false;
                }
                return true;
            }
        }

        public float MinParam => param[0];
        public float MaxParam => param[param.Length - 1];
        public int Count => param.Length;

        public void Set(float[] blendParams, float[] footSpeedsKmh)
        {
            param = (float[])blendParams.Clone();
            footKmh = (float[])footSpeedsKmh.Clone();
        }

        // 서 있기 ↔ 걷기(문턱 둘)
        public static bool IsMoving(float kmh, bool wasMoving) => wasMoving ? kmh >= StopKmh : kmh >= StartKmh;

        // 달리는 속도 → 블렌드 목표값. 걷는 중엔 서 있기와 섞지 않고(0에서 3 km/h 미끄러짐), 표 끝을 넘으면 끝 동작
        public float TargetParam(float kmh, bool moving) => moving ? Mathf.Clamp(kmh, MinParam, MaxParam) : 0f;

        // 표 사이는 곧게 이어서
        public float FootKmh(float p)
        {
            if (p <= param[0]) return footKmh[0];
            int last = param.Length - 1;
            if (p >= param[last]) return footKmh[last];
            int i = 1;
            while (param[i] < p) i++;
            float t = (p - param[i - 1]) / (param[i] - param[i - 1]);
            return Mathf.Lerp(footKmh[i - 1], footKmh[i], t);
        }

        // 지금 블렌드 값(바뀌는 중일 수 있음)에서 발이 바닥에 붙는 재생 속도.
        // 멈췄는데 블렌드에 아직 걷기·달리기가 남아 있으면(감쇠 중) 동작을 그 자리에 세운다(0배): 몸은 멈췄는데 다리만 달리지 않게.
        // 거의 서 있기 동작이 되면 1배(숨쉬기)
        public float PlaybackSpeed(float kmh, bool moving, float currentParam)
        {
            if (!moving) return currentParam > StandingBlend ? 0f : 1f;
            return Mathf.Clamp(kmh / FootKmh(Mathf.Max(currentParam, MinParam)), MinPlayback, MaxPlayback);
        }
    }
}
