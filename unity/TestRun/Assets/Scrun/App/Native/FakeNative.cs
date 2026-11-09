using System;
using UnityEngine;

namespace Scrun.App
{
    // 에디터(와 아직 진짜 구현이 없는 플랫폼)에서 쓰는 가짜 권한·이어폰.
    // 처음 상태와 "팝업에서 무엇을 누를지"는 메뉴 Scrun → Fake Native 창에서 정하고,
    // Play 중에 그 창에서 상태를 바꾸면 Changed 이벤트가 진짜처럼 온다.
    public sealed class FakeNative : IPermissions, IHeadphones
    {
        // 정적 필드는 적힌 순서대로 초기화된다. Instance가 Config·KindCount를 쓰므로 맨 뒤에 둔다.
        static readonly int KindCount = Enum.GetValues(typeof(PermissionKind)).Length;

        // 에디터 창이 Play 전에 채워 넣는다(EditorPrefs에 저장). 빌드에서는 기본값.
        public static FakeNativeConfig Config = new FakeNativeConfig();

        public static readonly FakeNative Instance = new FakeNative();

        readonly PermissionStatus[] status = new PermissionStatus[KindCount];
        readonly bool[] asking = new bool[KindCount];
        readonly float[] answerAt = new float[KindCount];

        public event Action<PermissionKind, PermissionStatus> Changed;
        public HeadphoneStatus Status { get; private set; }
        public string DeviceName { get; private set; }
        event Action<HeadphoneStatus> headphonesChanged;
        event Action<HeadphoneStatus> IHeadphones.Changed
        {
            add => headphonesChanged += value;
            remove => headphonesChanged -= value;
        }

        // Instance는 정적 Config를 매번 읽는다(창의 "기본값으로"가 Config를 새 객체로 바꿔도 따라감).
        // 테스트는 자기 설정을 넘겨 따로 만든다.
        readonly FakeNativeConfig ownConfig;
        FakeNativeConfig Cfg => ownConfig ?? Config;

        public FakeNative(FakeNativeConfig config = null)
        {
            ownConfig = config;
            LoadStartState();
        }

        // Play를 시작할 때마다 설정값으로 되돌리고 지난 Play의 구독자를 지운다
        // (도메인 리로드를 꺼 둬도 지난 Play 상태가 안 남게).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            Instance.Changed = null;
            Instance.headphonesChanged = null;
            Instance.LoadStartState();
        }

        void LoadStartState()
        {
            Cfg.Normalize();
            for (int i = 0; i < KindCount; i++)
            {
                status[i] = Cfg.startStatus[i];
                asking[i] = false;
            }
            Status = Cfg.headphones;
            DeviceName = Status == HeadphoneStatus.NotConnected ? null : Cfg.headphoneName;
        }

        // Play 중에 설정의 처음 상태로 되돌린다. 바뀐 것마다 이벤트가 간다(화면이 따라 바뀜).
        public void SimulateStartState()
        {
            Cfg.Normalize();
            for (int i = 0; i < KindCount; i++) SimulateStatus((PermissionKind)i, Cfg.startStatus[i]);
            SimulateHeadphones(Cfg.headphones, Cfg.headphoneName);
        }

        // ---- IPermissions ----

        public PermissionStatus GetStatus(PermissionKind kind) => status[(int)kind];

        public async Awaitable<PermissionStatus> RequestAsync(PermissionKind kind)
        {
            int i = (int)kind;
            if (status[i] != PermissionStatus.NotDetermined) return status[i];

            // 팝업은 한 번만 뜬다. 같은 권한을 동시에 여러 번 요청해도 모두 같은 답을 받는다.
            if (!asking[i])
            {
                asking[i] = true;
                answerAt[i] = Time.realtimeSinceStartup + Cfg.requestDelaySec;
            }
            while (status[i] == PermissionStatus.NotDetermined)
            {
                if (asking[i] && Time.realtimeSinceStartup >= answerAt[i])
                {
                    SimulateStatus(kind, Cfg.grantOnRequest[i] ? PermissionStatus.Granted : PermissionStatus.Denied);
                    break;
                }
                await Awaitable.NextFrameAsync();
            }
            return status[i];
        }

        public void OpenSettings() =>
            Debug.Log("[FakeNative] 설정 앱 열기(가짜). Fake Native 창에서 상태를 바꿔 '설정 앱에서 바꾸고 돌아옴'을 흉내 내세요.");

        // ---- 에디터 창에서 부르는 흉내 ----

        // 팝업 응답 또는 설정 앱에서 바꾸고 돌아온 것처럼 상태를 바꾼다.
        public void SimulateStatus(PermissionKind kind, PermissionStatus value)
        {
            int i = (int)kind;
            asking[i] = false;
            if (status[i] == value) return;
            status[i] = value;
            Changed?.Invoke(kind, value);
        }

        // 이어폰을 끼거나 뺀 것처럼.
        public void SimulateHeadphones(HeadphoneStatus value, string name)
        {
            var newName = value == HeadphoneStatus.NotConnected ? null : name;
            if (Status == value && DeviceName == newName) return;
            Status = value;
            DeviceName = newName;
            headphonesChanged?.Invoke(value);
        }
    }

    [Serializable]
    public class FakeNativeConfig
    {
        // PermissionKind 순서(모션, 카메라, 헤드폰 모션, 알림)
        public PermissionStatus[] startStatus = new PermissionStatus[4];
        public bool[] grantOnRequest = { true, true, true, true };
        public float requestDelaySec = 0.6f; // 사용자가 팝업을 누르기까지 걸리는 시간 흉내
        public HeadphoneStatus headphones = HeadphoneStatus.Connected;
        public string headphoneName = "AirPods Pro";

        // 저장된 값이 예전 모양(권한 개수가 다름)이어도 깨지지 않게 맞춘다.
        public void Normalize()
        {
            int n = Enum.GetValues(typeof(PermissionKind)).Length;
            if (startStatus == null || startStatus.Length != n) Array.Resize(ref startStatus, n);
            if (grantOnRequest == null || grantOnRequest.Length != n)
            {
                int old = grantOnRequest?.Length ?? 0;
                Array.Resize(ref grantOnRequest, n);
                for (int i = old; i < n; i++) grantOnRequest[i] = true;
            }
            if (requestDelaySec < 0f) requestDelaySec = 0f;
            if (string.IsNullOrEmpty(headphoneName)) headphoneName = "AirPods Pro";
        }
    }
}
