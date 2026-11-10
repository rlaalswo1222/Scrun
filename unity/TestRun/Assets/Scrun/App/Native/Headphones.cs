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
    // 아이폰은 오디오 출력 경로 + 헤드폰 모션 연결 알림으로 판단한다(ScrunHeadphones.swift, 실기기 확인은 가설).
    public interface IHeadphones
    {
        HeadphoneStatus Status { get; }

        // 화면에 보여 줄 이름(예: "AirPods Pro"). 연결 안 됐으면 null.
        string DeviceName { get; }

        // 끼거나 빼면 메인 스레드에서 불린다.
        // 아이폰: 에어팟 모션 연결(Connected)은 누군가 이 이벤트를 구독하는 동안에만 지켜본다(배터리).
        // 구독자가 없을 때 Status를 읽으면 에어팟도 ConnectedNoMotion으로 보일 수 있다 → 상태를 보여 주는 화면은
        // OnShow에서 구독하고 OnHide에서 뺀다. 구독 직후 에어팟이 확인되면 Changed(Connected)가 온다.
        event Action<HeadphoneStatus> Changed;
    }
}
