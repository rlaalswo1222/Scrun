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
        public static ISecureStore SecureStore => FakeSecureStore.Instance;
        public static IKakaoLogin Kakao => FakeNative.Instance;
        public static ISpeedSensor Speed => FakeSpeedSensor.Instance;
#elif UNITY_IOS
        // 아이폰: Swift 브리지(IosChannel → Assets/Plugins/iOS/ScrunBridge.swift, ADR-0009)
        public static IPermissions Permissions => IosChannel.Instance;
        public static IHeadphones Headphones => IosChannel.Instance;
        public static ISecureStore SecureStore => IosChannel.Instance;
        public static IKakaoLogin Kakao => IosChannel.Instance;
        // Swift 속도 플러그인은 S2(BL-5, 계획 0020). 그 전엔 NativeSpeedSource가 예외를 한 번 알리고 속도 0
        public static ISpeedSensor Speed => throw new PlatformNotSupportedException(
            "아이폰 속도 플러그인(Swift)이 아직 없습니다(S2, 계획 0020). 가짜로 보려면 SCRUN_FAKE_NATIVE를 켜세요.");
#else
        // 출시 대상은 아이폰뿐(ADR-0007). 다른 플랫폼 빌드에서 가짜가 몰래 "허용"을 주면
        // 권한 없이 달리기까지 가 버리므로, 바로 멈추게 한다.
        // 기기에서 가짜로 화면만 보려면 Scripting Define Symbols에 SCRUN_FAKE_NATIVE.
        public static IPermissions Permissions => throw NotReady();
        public static IHeadphones Headphones => throw NotReady();
        public static ISecureStore SecureStore => throw NotReady();
        public static IKakaoLogin Kakao => throw NotReady();
        public static ISpeedSensor Speed => throw NotReady();

        static Exception NotReady() => new PlatformNotSupportedException(
            "이 플랫폼에는 Native 구현이 없습니다(아이폰만, ADR-0007). 가짜로 보려면 SCRUN_FAKE_NATIVE를 켜세요.");
#endif
    }
}
