using System;
using System.Collections.Generic;

namespace Scrun.App
{
    public enum RunTogetherStatus
    {
        Connecting,   // 처음 붙는 중
        Connected,    // welcome을 받음
        Reconnecting, // 끊겨서 다시 붙는 중(다른 러너는 마지막 값으로 계속 보임)
        Stopped,      // 다시 붙지 않음(StopReason)
    }

    public enum RunTogetherStop
    {
        None,
        Closed,          // 앱이 끝냄(맵 나가기)
        ReplacedElsewhere, // 같은 계정이 다른 기기에서 붙음(4001)
        NeedsReentry,    // 토큰 만료·자리 없음(4401·4403) → 다시 입장해야 함
        ProtocolError,   // 서버가 메시지 모양이 틀렸다고 함(4400, 앱 버그)
    }

    // 내가 보내는 상태(초당 2번). D는 한 바퀴 안 위치(m)
    public readonly struct MyRunState
    {
        public readonly float D, SpeedKmh;
        public readonly bool Running;

        public MyRunState(float d, float speedKmh, bool running)
        {
            D = d;
            SpeedKmh = speedKmh;
            Running = running;
        }
    }

    // 같이 뛰기 연결(계획 0029, ADR-0010). 화면 코드는 이것만 본다 → 전송 방식을 바꿔도(예: Photon) 이 구현만 바꾼다.
    // 이벤트·목록은 메인 스레드에서만 바뀐다.
    public interface IRunTogether : IDisposable
    {
        RunTogetherStatus Status { get; }
        RunTogetherStop StopReason { get; }
        IReadOnlyList<RemoteRunner> Runners { get; }
        double? RoundTripMs { get; } // 마지막 ping 왕복(ms), 실측·개발 표시용
        float CourseLengthM { get; } // 서버가 아는 코스 한 바퀴(m, welcome 전엔 0). 섬 코스 길이와 같아야 함

        event Action<RemoteRunner> Joined;
        event Action<RemoteRunner> Left;
        event Action<RunTogetherStatus> StatusChanged;

        void Start();
    }
}
