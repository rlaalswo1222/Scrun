using System.Collections.Generic;
using UnityEngine;

// 걷기/달리기 클립을 프레임 단위로 샘플링해 걸음 특성을 잰다 (에디터 전용)
//  - FootSpeedKmh: 디딘 발이 몸(엉덩이) 기준으로 뒤로 밀리는 속도의 중앙값 = 이 동작이 실제로 나아가는 속도.
//    엉덩이 기준이라 In Place 여부(루트 모션 유무)와 무관하다.
//  - LeftStrikes: 왼발 착지 시각(초). 블렌드할 동작끼리 박자를 맞추는 데 쓴다.
public static class GaitAnalysis
{
    public struct Result
    {
        public float FootSpeedKmh;
        public List<float> LeftStrikes;
        public float Length;
    }

    const float Dt = 1f / 240f;
    const float StanceBand = 0.03f;      // 최저 발 높이 + 3cm 안 = 디딤
    const float StrikeLevel = 0.25f;     // 왼발 높이 범위의 하위 25%로 내려오는 순간 = 착지

    public static Result Analyze(GameObject rig, AnimationClip clip)
    {
        var an = rig.GetComponent<Animator>();
        var lf = an.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
        var hips = an.GetBoneTransform(HumanBodyBones.Hips);
        var root = rig.transform;

        int n = Mathf.CeilToInt(clip.length / Dt);
        var l = new Vector3[n + 1];
        var r = new Vector3[n + 1];
        var h = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            clip.SampleAnimation(rig, Mathf.Min(i * Dt, clip.length));
            l[i] = root.InverseTransformPoint(lf.position);
            r[i] = root.InverseTransformPoint(rf.position);
            h[i] = root.InverseTransformPoint(hips.position);
        }

        // 디딘 발의 엉덩이 대비 뒤쪽 속도
        float minH = float.MaxValue;
        for (int i = 0; i <= n; i++) minH = Mathf.Min(minH, Mathf.Min(l[i].y, r[i].y));
        var speeds = new List<float>();
        for (int i = 1; i <= n; i++)
        {
            var p = l[i].y < r[i].y ? l : r;
            if (p[i].y >= minH + StanceBand) continue;
            float rel = (p[i].z - h[i].z) - (p[i - 1].z - h[i - 1].z);
            speeds.Add(-rel / Dt);
        }
        speeds.Sort();
        float median = speeds.Count > 0 ? speeds[speeds.Count / 2] : 0f;

        // 왼발 착지 시각
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i <= n; i++) { lo = Mathf.Min(lo, l[i].y); hi = Mathf.Max(hi, l[i].y); }
        float level = lo + StrikeLevel * (hi - lo);
        var strikes = new List<float>();
        bool up = l[0].y > level;
        for (int i = 1; i <= n; i++)
        {
            if (up && l[i].y <= level) { strikes.Add(i * Dt); up = false; }
            else if (l[i].y > level + 0.02f) up = true;
        }

        return new Result { FootSpeedKmh = median * 3.6f, LeftStrikes = strikes, Length = clip.length };
    }
}
