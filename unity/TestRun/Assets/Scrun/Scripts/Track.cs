using UnityEngine;

namespace Scrun
{
    // 달리는 길. 위치는 "출발선부터의 거리(m)" 하나로 표현하고, 한 바퀴를 넘으면 반복한다.
    // 러너·봇·고스트·네트워크 동기화가 모두 이 거리만 주고받는다(실제 거리, 맵 위치는 × WorldScale).
    public abstract class Track : MonoBehaviour, IPath
    {
        // 세계 속도 배율(계획 0083): 맵 위에서 실제로 달린 거리의 몇 배만큼 나아가나. 1이면 실제 거리 = 맵 거리(섬 맵은 1 그대로).
        // 러너의 Distance(기록·인증·고스트·서버·같이 달리기)는 실제 거리이고, 맵에 놓을 때만 곱한다(RunnerController.TrackDistance).
        // 같은 맵의 러너는 모두 이 값 하나를 쓰므로 서로 간격이 맞는다. 서버 코스 길이는 RunLapLength와 같아야 한다
        [SerializeField, Min(0.1f)] float worldScale = 1f;
        public float WorldScale { get => worldScale; set => worldScale = Mathf.Max(0.1f, value); }

        public abstract float LapLength { get; }              // 맵 위 한 바퀴(m)
        public float RunLapLength => LapLength / worldScale;  // 실제로 달리는 한 바퀴(m): 앱·서버·같이 달리기는 이 길이로 감는다
        public float Length => LapLength;
        public abstract Vector3 GetPoint(float distance);     // 길 중앙, 월드 좌표
        public abstract Vector3 GetForward(float distance);   // 진행 방향(수평, 정규화)

        // 진행 방향 오른쪽(수평)
        public Vector3 GetRight(float distance) => Vector3.Cross(Vector3.up, GetForward(distance)).normalized;
    }
}
