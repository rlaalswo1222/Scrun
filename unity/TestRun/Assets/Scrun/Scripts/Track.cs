using UnityEngine;

namespace Scrun
{
    // 달리는 길. 위치는 "출발선부터의 거리(m)" 하나로 표현하고, 한 바퀴를 넘으면 반복한다.
    // 러너·봇·고스트·네트워크 동기화가 모두 이 거리만 주고받는다.
    public abstract class Track : MonoBehaviour, IPath
    {
        public abstract float LapLength { get; }
        public float Length => LapLength;
        public abstract Vector3 GetPoint(float distance);     // 길 중앙, 월드 좌표
        public abstract Vector3 GetForward(float distance);   // 진행 방향(수평, 정규화)

        // 진행 방향 오른쪽(수평)
        public Vector3 GetRight(float distance) => Vector3.Cross(Vector3.up, GetForward(distance)).normalized;
    }
}
