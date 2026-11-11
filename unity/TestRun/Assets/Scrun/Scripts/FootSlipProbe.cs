using UnityEngine;

namespace Scrun
{
    // 개발용: 디딘 발이 바닥 위에서 얼마나 미끄러지는지 잰다.
    // 지지 중인 발(낮은 쪽 발이 최근 최저 높이 근처일 때)의 수평 이동 속도를 누적 평균.
    // 달리기는 앞꿈치 착지라 발목 높이가 동작마다 달라서 고정 높이 대신 최근 최저 높이를 기준으로 삼는다.
    // 땅에 닿은 점은 발목·발끝 네 점 중 가장 낮은 것(11/11): 발목만 보면 뒤꿈치 → 발끝으로 구르는 동안의 발목 이동을 미끄러짐으로 셌다.
    // 0에 가까울수록 좋고, +면 발이 앞으로 밀림(동작이 느림), -면 뒤로 끌림(동작이 빠름).
    [RequireComponent(typeof(Animator))]
    public class FootSlipProbe : MonoBehaviour
    {
        // 땅에 닿음 규칙. 발 속도 표(RunnerGaitMeasure)도 같은 값을 쓴다 → 표로 맞춘 결과를 이 도구로 그대로 확인할 수 있게
        public const float PlantedBand = 0.03f; // 최근 최저 높이 + 이 값 안이면 디딘 것으로 봄
        public const float MinRise = 0.05f;     // 최저 높이 기억을 초당 이만큼 풀어 줌(동작이 바뀌거나 섞여 걸음마다 발 높이가 다를 때 따라가게)

        [SerializeField] RunnerController runner;
        [SerializeField] float plantedBand = PlantedBand;
        [SerializeField] float minRise = MinRise;

        Transform[] points;   // 왼발목·오른발목·왼발끝·오른발끝(발끝 뼈가 없으면 발목만)
        Vector3[] prev;
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

        // 땅에 닿을 수 있는 점: 왼발목·오른발목·왼발끝·오른발끝(없는 뼈는 뺌). 휴머노이드가 아니면 빈 배열
        public static Transform[] ContactPoints(Animator a)
        {
            var list = new System.Collections.Generic.List<Transform>();
            if (a == null || !a.isHuman) return list.ToArray();
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                var t = a.GetBoneTransform(b);
                if (t) list.Add(t);
            }
            return list.ToArray();
        }

        void Start()
        {
            points = ContactPoints(GetComponent<Animator>());
            if (points.Length == 0) { enabled = false; return; }
            prev = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++) prev[i] = points[i].position;
        }

        public void ResetStats() { sumSlip = 0f; sumTime = 0f; samples.Clear(); }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !runner) return;

            float ground = runner.transform.position.y;
            int low = 0;
            for (int i = 1; i < points.Length; i++)
                if (points[i].position.y < points[low].position.y) low = i;

            float h = points[low].position.y - ground;
            minHeight = Mathf.Min(minHeight + minRise * dt, h);

            if (h < minHeight + plantedBand)
            {
                var v = (points[low].position - prev[low]) / dt;
                float fwd = Vector3.Dot(v, runner.transform.forward);
                sumSlip += fwd * dt;
                samples.Add(fwd);
                sumTime += dt;
            }
            for (int i = 0; i < points.Length; i++) prev[i] = points[i].position;
        }
    }
}
