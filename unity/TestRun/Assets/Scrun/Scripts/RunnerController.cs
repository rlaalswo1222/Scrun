using UnityEngine;

namespace Scrun
{
    // 속도 소스에서 km/h를 받아 누적 거리만큼 트랙 위를 이동. 플레이어·봇·고스트가 같은 방식으로 달린다.
    public class RunnerController : MonoBehaviour, ISpeedSource
    {
        [Tooltip("ISpeedSource를 구현한 컴포넌트")]
        [SerializeField] MonoBehaviour speedSourceBehaviour;
        [SerializeField] Track track;
        [SerializeField] float lateralOffset;    // 도로 중앙에서 오른쪽(+) 거리(m) — 여러 명이 겹치지 않게
        [SerializeField] float startDistance;
        [SerializeField] float groundOffset = 0.02f;   // 노면 위로(도로 메쉬가 지면보다 2cm 위)

        ISpeedSource speedSource;

        public float SpeedKmh => speedSource?.SpeedKmh ?? 0f;
        public float Distance { get; private set; }  // 누적 거리(m)
        public int Lap => track ? Mathf.FloorToInt(Distance / track.LapLength) : 0;
        public Track Track => track;
        public float LateralOffset { get => lateralOffset; set => lateralOffset = value; }

        public void Setup(MonoBehaviour speed, Track t, float lateral, float start)
        {
            speedSourceBehaviour = speed;
            track = t;
            lateralOffset = lateral;
            startDistance = start;
        }

        void Awake()
        {
            speedSource = speedSourceBehaviour as ISpeedSource;
            if (speedSource == null)
                Debug.LogError("RunnerController: speedSourceBehaviour가 ISpeedSource가 아님", this);
            Distance = startDistance;
        }

        void Start() => Place();

        // 순간이동: 코스 위 거리로 바로 옮김(개발 도구, 고스트 재생)
        public void Teleport(float distance)
        {
            Distance = Mathf.Max(0f, distance);
            Place();
        }

        void Update()
        {
            Distance += SpeedKmh / 3.6f * Time.deltaTime;
            Place();
        }

        void Place()
        {
            if (!track) return;
            var p = track.GetPoint(Distance);
            var fwd = track.GetForward(Distance);
            if (lateralOffset != 0f) p += track.GetRight(Distance) * lateralOffset;
            transform.SetPositionAndRotation(new Vector3(p.x, p.y + groundOffset, p.z), Quaternion.LookRotation(fwd));
        }
    }
}
