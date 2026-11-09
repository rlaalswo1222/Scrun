using System;

namespace Scrun.App
{
    // 화면 코드가 아이폰 기능을 부르는 입구. 화면은 이 모양만 쓰고, 진짜(아이폰)인지
    // 가짜(에디터)인지 몰라도 된다.
    //   var s = await Native.Permissions.RequestAsync(PermissionKind.Camera);
    //   Native.Headphones.Changed += OnHeadphones;
    // 에디터에서는 메뉴 Scrun → Fake Native 창에서 허용·거부·에어팟 연결을 바꿔 본다.
    public static class Native
    {
#if UNITY_EDITOR || SCRUN_FAKE_NATIVE
        public static IPermissions Permissions => FakeNative.Instance;
        public static IHeadphones Headphones => FakeNative.Instance;
#else
        // 아이폰용 진짜 구현(Swift 브리지)은 계획 0018의 5번에서 붙인다. 그 전의 빌드에서
        // 가짜가 몰래 "허용"을 주면 권한 없이 달리기까지 가 버리므로, 바로 멈추게 한다.
        // 기기에서 가짜로 화면만 보려면 Scripting Define Symbols에 SCRUN_FAKE_NATIVE.
        public static IPermissions Permissions => throw NotReady();
        public static IHeadphones Headphones => throw NotReady();

        static Exception NotReady() => new PlatformNotSupportedException(
            "이 플랫폼의 Native 구현이 아직 없습니다(계획 0018의 5번). 가짜로 보려면 SCRUN_FAKE_NATIVE를 켜세요.");
#endif
    }
}
