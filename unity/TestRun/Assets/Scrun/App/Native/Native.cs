namespace Scrun.App
{
    // 화면 코드가 아이폰 기능을 부르는 입구. 화면은 이 모양만 쓰고, 진짜(아이폰)인지
    // 가짜(에디터)인지 몰라도 된다.
    //   var s = await Native.Permissions.RequestAsync(PermissionKind.Camera);
    //   Native.Headphones.Changed += OnHeadphones;
    // 에디터에서는 메뉴 Scrun → Fake Native 창에서 허용·거부·에어팟 연결을 바꿔 본다.
    public static class Native
    {
        // 아이폰용 진짜 구현(Swift 브리지)은 계획 0018의 5번에서 붙인다. 그 전까지는
        // 모든 플랫폼에서 가짜를 쓴다.
        public static IPermissions Permissions => FakeNative.Instance;
        public static IHeadphones Headphones => FakeNative.Instance;
    }
}
