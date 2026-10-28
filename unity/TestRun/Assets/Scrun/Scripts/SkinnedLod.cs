using UnityEngine;

namespace Scrun
{
    // 봇·고스트: 카메라 거리에 따라 원본 → 중간(2cm 격자) → 단순(4.5cm 격자) 스킨 메쉬로 바꾸고, 아주 멀면 그리지 않는다.
    // 함께 달리는 무리는 플레이어 바로 곁에 모여 있으므로 원본은 아주 가까울 때만(모바일 삼각형 수 절약)
    public class SkinnedLod : MonoBehaviour
    {
        [SerializeField] SkinnedMeshRenderer[] renderers;
        [SerializeField] Mesh[] fullMeshes;
        [SerializeField] Mesh[] midMeshes;
        [SerializeField] Mesh[] lowMeshes;
        [SerializeField] float fullDistance = 6f;
        [SerializeField] float lowDistance = 18f;
        [SerializeField] float cullDistance = 140f;

        public static float DistanceScale = 1f;   // 품질 단계(ScrunQuality)가 설정

        int state = -1;   // 0 원본, 1 중간, 2 단순, 3 안 그림
        float next;

        public void Set(SkinnedMeshRenderer[] r, Mesh[] full, Mesh[] mid, Mesh[] low)
        {
            renderers = r; fullMeshes = full; midMeshes = mid; lowMeshes = low;
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.2f;
            var cam = Camera.main;
            if (!cam || renderers == null) return;
            float d = Vector3.Distance(cam.transform.position, transform.position) / Mathf.Max(0.1f, DistanceScale);
            int s = d > cullDistance ? 3 : d > lowDistance ? 2 : d > fullDistance ? 1 : 0;
            if (s == state) return;
            state = s;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                renderers[i].forceRenderingOff = s == 3;
                if (s == 3) continue;
                var mesh = s == 0 ? fullMeshes[i] : s == 1 && midMeshes != null && i < midMeshes.Length && midMeshes[i] ? midMeshes[i] : lowMeshes[i];
                renderers[i].sharedMesh = mesh;
            }
        }
    }
}
