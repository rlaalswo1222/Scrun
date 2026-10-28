using UnityEngine;

namespace Scrun
{
    // 개발용: 디딘 발이 바닥 위에서 얼마나 미끄러지는지 잰다.
    // 지지 중인 발(낮은 쪽 발이 최근 최저 높이 근처일 때)의 수평 이동 속도를 누적 평균.
    // 달리기는 앞꿈치 착지라 발목 높이가 동작마다 달라서 고정 높이 대신 최근 최저 높이를 기준으로 삼는다.
    // 0에 가까울수록 좋고, +면 발이 앞으로 밀림(동작이 느림), -면 뒤로 끌림(동작이 빠름).
    [RequireComponent(typeof(Animator))]
    public class FootSlipProbe : MonoBehaviour
    {
        [SerializeField] RunnerController runner;
        [SerializeField] float plantedBand = 0.03f;   // 최근 최저 높이 + 이 값 안이면 디딘 것으로 봄
        [SerializeField] float minRise = 0.05f;       // 최저 높이 기억을 초당 이만큼 풀어 줌(동작 바뀔 때 따라가게)

        Transform left, right;
        Vector3 prevL, prevR;
        float sumSlip, sumTime;
        float minHeight = float.MaxValue;
        readonly System.Collections.Generic.List<float> samples = new();

        // 진행 방향 기준 평균 미끄러짐(m/s). +면 발이 앞으로 밀림
        public float MeanSlip => sumTime > 0 ? sumSlip / sumTime : 0f;
        // 중앙값: 착지·이륙 순간의 큰 값에 덜 휘둘림 (GaitAnalysis와 같은 기준)
        public float MedianSlip
        {
            get
            {
                if (samples.Count == 0) return 0f;
                var s = new System.Collections.Generic.List<float>(samples);
                s.Sort();
                return s[s.Count / 2];
            }
        }
        public float SampleSeconds => sumTime;

        void Start()
        {
            var a = GetComponent<Animator>();
            left = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            right = a.GetBoneTransform(HumanBodyBones.RightFoot);
            prevL = left.position;
            prevR = right.position;
        }

        public void ResetStats() { sumSlip = 0f; sumTime = 0f; samples.Clear(); }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !runner) return;

            float ground = runner.transform.position.y;
            var low = left.position.y < right.position.y ? left : right;
            var prev = low == left ? prevL : prevR;

            float h = low.position.y - ground;
            minHeight = Mathf.Min(minHeight + minRise * dt, h);

            if (h < minHeight + plantedBand)
            {
                var v = (low.position - prev) / dt;
                float fwd = Vector3.Dot(v, runner.transform.forward);
                sumSlip += fwd * dt;
                samples.Add(fwd);
                sumTime += dt;
            }
            prevL = left.position;
            prevR = right.position;
        }
    }
}
