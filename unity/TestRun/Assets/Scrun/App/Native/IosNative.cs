using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scrun.App
{
    // 아이폰 진짜 구현. 실제 일은 Swift(Assets/Plugins/iOS/Scrun*.swift)가 하고, 여기서는 이름·JSON만 오간다.
    // 통로(INativeChannel)만 바꿔 끼우면 에디터 테스트에서도 같은 코드가 돈다(IosNativeTests).
    // 이름·값 문자열은 Swift ScrunBridge.swift와 짝이다. 바꾸면 양쪽을 같이 고친다.
    internal sealed class IosNative : IPermissions, IHeadphones, ISecureStore, IKakaoLogin
    {
        readonly NativeBridge bridge;

        public event Action<PermissionKind, PermissionStatus> Changed;
        event Action<HeadphoneStatus> headphonesChanged;
        int headphoneWatchers;

        // 구독자가 있는 동안에만 Swift가 에어팟 모션 연결을 지켜본다(모션 업데이트를 켜 둠 = 배터리, ADR-0009)
        event Action<HeadphoneStatus> IHeadphones.Changed
        {
            add
            {
                if (value == null) return;
                headphonesChanged += value;
                if (++headphoneWatchers == 1) Watch(true);
            }
            remove
            {
                if (value == null || headphonesChanged == null) return;
                int before = headphonesChanged.GetInvocationList().Length;
                headphonesChanged -= value;
                int after = headphonesChanged?.GetInvocationList().Length ?? 0;
                if (after == before) return; // 구독한 적 없는 것을 빼려 함
                if (--headphoneWatchers == 0) Watch(false);
            }
        }

        public IosNative(NativeBridge bridge)
        {
            this.bridge = bridge;
            bridge.Event += OnEvent;
        }

        // ---- IPermissions ----

        public PermissionStatus GetStatus(PermissionKind kind) =>
            ParseStatus((string)bridge.Query("permissions.status", new { kind = KindName(kind) }));

        public async Awaitable<PermissionStatus> RequestAsync(PermissionKind kind) =>
            ParseStatus((string)await bridge.CallAsync("permissions.request", new { kind = KindName(kind) }));

        public void OpenSettings() => bridge.Query("app.openSettings");

        // ---- IHeadphones ----

        // 구독(+=)이 예외를 던지면 화면 코드가 깨지므로 실패는 로그만(그 경우 에어팟도 ConnectedNoMotion으로 보임)
        void Watch(bool on)
        {
            try
            {
                bridge.Query("headphones.watch", new { on });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public HeadphoneStatus Status => ParseHeadphones((string)HeadphonesNow()["status"]);
        public string DeviceName => (string)HeadphonesNow()["name"];

        JToken HeadphonesNow() => bridge.Query("headphones.status");

        // ---- ISecureStore (키체인) ----

        public string Get(string key) => (string)bridge.Query("secure.get", new { key });
        public void Set(string key, string value) => bridge.Query("secure.set", new { key, value });
        public void Delete(string key) => bridge.Query("secure.delete", new { key });

        // ---- IKakaoLogin ----

        // 카카오 access 토큰, 사용자가 취소하면 null(Swift가 null을 줌)
        public async Awaitable<string> LoginAsync() => (string)await bridge.CallAsync("kakao.login");

        // ---- 이벤트 ----

        void OnEvent(string name, JObject e)
        {
            switch (name)
            {
                case "permission":
                    Changed?.Invoke(ParseKind((string)e["kind"]), ParseStatus((string)e["status"]));
                    break;
                case "headphones":
                    headphonesChanged?.Invoke(ParseHeadphones((string)e["status"]));
                    break;
                default:
                    Debug.LogWarning("[Native] 모르는 이벤트: " + name);
                    break;
            }
        }

        // ---- 이름 ↔ 값 ----

        internal static string KindName(PermissionKind k) => k switch
        {
            PermissionKind.Motion => "motion",
            PermissionKind.Camera => "camera",
            PermissionKind.HeadphoneMotion => "headphoneMotion",
            PermissionKind.Notifications => "notifications",
            _ => throw new ArgumentOutOfRangeException(nameof(k), k, null),
        };

        internal static PermissionKind ParseKind(string s) => s switch
        {
            "motion" => PermissionKind.Motion,
            "camera" => PermissionKind.Camera,
            "headphoneMotion" => PermissionKind.HeadphoneMotion,
            "notifications" => PermissionKind.Notifications,
            _ => throw new NativeException("모르는 권한 이름: " + s),
        };

        internal static PermissionStatus ParseStatus(string s) => s switch
        {
            "notDetermined" => PermissionStatus.NotDetermined,
            "granted" => PermissionStatus.Granted,
            "denied" => PermissionStatus.Denied,
            "restricted" => PermissionStatus.Restricted,
            _ => throw new NativeException("모르는 권한 상태: " + s),
        };

        internal static HeadphoneStatus ParseHeadphones(string s) => s switch
        {
            "notConnected" => HeadphoneStatus.NotConnected,
            "connectedNoMotion" => HeadphoneStatus.ConnectedNoMotion,
            "connected" => HeadphoneStatus.Connected,
            _ => throw new NativeException("모르는 이어폰 상태: " + s),
        };
    }
}
