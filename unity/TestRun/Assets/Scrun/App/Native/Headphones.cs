using System;

namespace Scrun.App
{
    public enum HeadphoneStatus
    {
        NotConnected,      // 이어폰 없음 → 폰 센서만으로 달린다("이어폰 없이 계속")
        ConnectedNoMotion, // 이어폰은 있지만 헤드폰 모션을 못 줌(모션 센서 없는 이어폰)
        Connected,         // 헤드폰 모션을 주는 이어폰(에어팟 3·Pro·Max 등)
    }

    // 헤드폰 모션 권한은 IPermissions(PermissionKind.HeadphoneMotion)에서 따로 본다.
    // 연결 상태를 iOS에서 어떻게 읽을지(오디오 출력 경로 + 헤드폰 모션 연결 알림)는
    // Swift 구현(계획 0018의 5번)에서 아이폰으로 확인한다(가설).
    public interface IHeadphones
    {
        HeadphoneStatus Status { get; }

        // 화면에 보여 줄 이름(예: "AirPods Pro"). 연결 안 됐으면 null.
        string DeviceName { get; }

        // 끼거나 빼면 메인 스레드에서 불린다.
        event Action<HeadphoneStatus> Changed;
    }
}
