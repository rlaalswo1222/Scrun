using UnityEngine;

namespace Scrun
{
    // 뒤쪽 약간 위에서 따라가는 3인칭 카메라(Zwift식 구도: 낮고 가깝게, 지평선이 화면 위쪽 40% 근처).
    // 러너가 있으면 코스 위 "뒤쪽 지점"에 서서 "앞쪽 도로"를 본다 → 커브에서 카메라가 길 밖으로 밀려나지 않고
    // 길이 휘는 쪽으로 미리 고개를 돌린다. 오르막·내리막에서는 앞길 높이를 따라 고개를 들고 숙인다.
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] RunnerController runner;   // 없으면 target 뒤쪽 고정 거리
        [SerializeField] float distance = 4.6f;
        [SerializeField] float height = 2.2f;
        [SerializeField] float lookAhead = 10f;
        [SerializeField] float lookHeight = 1.2f;
        [SerializeField] float smooth = 5f;
        [SerializeField] float lookSmooth = 4f;

        Vector3 look;

        public void SetTarget(Transform t, RunnerController r) { target = t; runner = r; }

        void Want(out Vector3 pos, out Vector3 at)
        {
            var track = runner ? runner.Track : null;
            if (track)
            {
                float d = runner.Distance;
                float lateral = runner.LateralOffset;
                pos = track.GetPoint(d - distance) + track.GetRight(d - distance) * lateral + Vector3.up * height;
                at = track.GetPoint(d + lookAhead) + track.GetRight(d + lookAhead) * lateral * 0.5f + Vector3.up * lookHeight;
                // 러너 몸 높이(경사에서 루트가 길 위에 있음)
                pos.y = Mathf.Max(pos.y, target.position.y + height * 0.8f);
                return;
            }
            pos = target.position - target.forward * distance + Vector3.up * height;
            at = target.position + target.forward * lookAhead + Vector3.up * lookHeight;
        }

        // 순간이동 직후 따라가는 지연 없이 바로 뒤에 붙음
        public void Snap()
        {
            if (!target) return;
            Want(out var pos, out look);
            transform.position = pos;
            transform.LookAt(look);
        }

        void Start() => Snap();

        void LateUpdate()
        {
            if (!target) return;
            Want(out var pos, out var at);
            float dt = Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-smooth * dt));
            look = Vector3.Lerp(look, at, 1f - Mathf.Exp(-lookSmooth * dt));
            transform.LookAt(look);
        }
    }
}
