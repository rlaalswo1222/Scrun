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
        public float Distance { get; private set; }  // 누적 거리(m) — 실제로 달린 거리(기록·인증·고스트·서버)
        public float WorldScale => track ? track.WorldScale : 1f;
        public float TrackDistance => Distance * WorldScale;  // 맵 위 위치(m). 몸·카메라·구간·하늘은 이 값(계획 0083)
        public int Lap => track ? Mathf.FloorToInt(TrackDistance / track.LapLength) : 0;
        public Track Track => track;
        public float LateralOffset { get => lateralOffset; set => lateralOffset = value; }

        // 밖에서 매 프레임 Teleport로 자리를 정함(실시간 다른 러너). 속도는 애니메이션에만 씀
        public bool Driven { get; set; }

        public void Setup(MonoBehaviour speed, Track t, float lateral, float start)
        {
            speedSourceBehaviour = speed;
            track = t;
            lateralOffset = lateral;
            startDistance = start;
        }

        // 실행 중에 속도 소스 바꾸기(앱에서 들어오면 키보드 → 진짜 센서, 계획 0029)
        public void SetSpeedSource(ISpeedSource source)
        {
            speedSource = source;
            speedSourceBehaviour = source as MonoBehaviour;
        }

        void Awake()
        {
            speedSource = speedSourceBehaviour as ISpeedSource;
            if (speedSource == null)
                Debug.LogError("RunnerController: speedSourceBehaviour가 ISpeedSource가 아님", this);
            Distance = startDistance;
        }

        void Start() => Place();

        // 순간이동: 코스 위 거리로 바로 옮김(개발 도구, 고스트 재생). 실제 거리 기준
        public void Teleport(float distance)
        {
            Distance = Mathf.Max(0f, distance);
            Place();
        }

        // 맵 위 위치로 순간이동(구간 바로가기 등 맵에 박힌 자리). Distance는 맵 위치 ÷ 배율
        public void TeleportTrack(float trackDistance) => Teleport(trackDistance / WorldScale);

        void Update()
        {
            if (Driven) return;
            Distance += SpeedKmh / 3.6f * Time.deltaTime;
            Place();
        }

        void Place()
        {
            if (!track) return;
            float d = TrackDistance;
            var p = track.GetPoint(d);
            var fwd = track.GetForward(d);
            if (lateralOffset != 0f) p += track.GetRight(d) * lateralOffset;
            transform.SetPositionAndRotation(new Vector3(p.x, p.y + groundOffset, p.z), Quaternion.LookRotation(fwd));
        }
    }
}
