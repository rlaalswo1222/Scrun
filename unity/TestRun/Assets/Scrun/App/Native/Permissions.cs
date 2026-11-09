using System;
using UnityEngine;

namespace Scrun.App
{
    // 앱이 묻는 아이폰 권한 4가지. 화면(필수·선택 안내)은 이 이름으로 구분한다.
    public enum PermissionKind
    {
        Motion,          // 모션 및 피트니스(필수)
        Camera,          // 카메라: 계기판 촬영(필수)
        HeadphoneMotion, // 헤드폰 모션: 에어팟 센서(선택)
        Notifications,   // 알림(선택)
    }

    public enum PermissionStatus
    {
        NotDetermined, // 아직 안 물어봄 → RequestAsync로 시스템 팝업을 띄울 수 있음
        Granted,       // 허용
        Denied,        // 거부 → 다시 물어봐도 팝업이 안 뜸, 설정 앱으로 보내야 함
        Restricted,    // 기기 관리·자녀 보호 등으로 사용자가 바꿀 수 없음
    }

    public interface IPermissions
    {
        // 지금 상태. 팝업 없이 바로 답한다.
        PermissionStatus GetStatus(PermissionKind kind);

        // 아직 안 물어봤으면 시스템 팝업을 띄우고 사용자가 고를 때까지 기다린다.
        // 이미 정해졌으면(허용·거부·제한) 팝업 없이 그 상태를 돌려준다(iOS는 두 번 묻지 않음).
        Awaitable<PermissionStatus> RequestAsync(PermissionKind kind);

        // 설정 앱의 이 앱 화면을 연다(거부한 권한을 바꾸라고 안내할 때).
        void OpenSettings();

        // 상태가 바뀌면 메인 스레드에서 불린다. 팝업 응답, 설정 앱에서 바꾸고 돌아왔을 때 모두.
        event Action<PermissionKind, PermissionStatus> Changed;
    }
}
