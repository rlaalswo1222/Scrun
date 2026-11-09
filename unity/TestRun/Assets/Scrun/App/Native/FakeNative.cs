using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scrun.App
{
    // 에디터에서 쓰는 가짜 권한·이어폰(빌드에서는 쓰지 않음, Native.cs).
    // 처음 상태와 "팝업에서 무엇을 누를지"는 메뉴 Scrun → Fake Native 창에서 정하고,
    // Play 중에 그 창에서 상태를 바꾸면 Changed 이벤트가 진짜처럼 온다.
    public sealed class FakeNative : IPermissions, IHeadphones
    {
        static int KindCount => FakeNativeConfig.KindCount;

        // 에디터 창이 채워 넣는다(EditorPrefs에 저장). Instance보다 먼저 초기화돼야 한다(적힌 순서).
        public static FakeNativeConfig Config = new FakeNativeConfig();

        public static readonly FakeNative Instance = new FakeNative();

        readonly PermissionStatus[] status = new PermissionStatus[KindCount];

        // 팝업 답을 기다리는 요청들. 같은 권한을 동시에 여러 번 요청하면 모두 같은 답을 받는다.
        readonly List<AwaitableCompletionSource<PermissionStatus>>[] waiters = NewWaiters();

        // 처음 상태로 되돌릴 때마다 올린다. 그 전에 시작한 "팝업 답 타이머"는 이 값이 다르면 아무것도 안 한다.
        int generation;

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

        static List<AwaitableCompletionSource<PermissionStatus>>[] NewWaiters()
        {
            var w = new List<AwaitableCompletionSource<PermissionStatus>>[KindCount];
            for (int i = 0; i < w.Length; i++) w[i] = new List<AwaitableCompletionSource<PermissionStatus>>();
            return w;
        }

        // Play를 시작할 때마다 설정값으로 되돌리고 지난 Play의 구독자를 지운다
        // (이 프로젝트는 Play 때 도메인 리로드를 꺼 둬서 정적 상태가 남는다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            Instance.Changed = null;
            Instance.headphonesChanged = null;
            Instance.LoadStartState();
        }

        // 설정의 처음 상태를 이벤트 없이 그대로 넣는다(Play 시작, 에디터 창이 설정을 읽은 직후).
        // 지난 Play에서 기다리던 요청은 버린다(그 화면은 이미 없음).
        public void LoadStartState()
        {
            generation++;
            Cfg.Normalize();
            for (int i = 0; i < KindCount; i++)
            {
                status[i] = Cfg.startStatus[i];
                waiters[i].Clear();
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

        public Awaitable<PermissionStatus> RequestAsync(PermissionKind kind)
        {
            int i = (int)kind;
            var src = new AwaitableCompletionSource<PermissionStatus>();
            if (status[i] != PermissionStatus.NotDetermined)
            {
                src.SetResult(status[i]); // iOS는 두 번 묻지 않는다
                return src.Awaitable;
            }
            waiters[i].Add(src);
            if (waiters[i].Count == 1) AnswerPopupLater(kind, generation); // 팝업은 하나만
            return src.Awaitable;
        }

        // 사용자가 팝업을 누르는 시간만큼 기다렸다가 설정의 답을 누른다.
        async void AnswerPopupLater(PermissionKind kind, int gen)
        {
            // 실제 시간으로 잰다(게임 시간은 창이 뒤에 있거나 프레임이 밀리면 한 번에 크게 뜀)
            float end = Time.realtimeSinceStartup + Cfg.requestDelaySec;
            while (Time.realtimeSinceStartup < end)
            {
                await Awaitable.NextFrameAsync();
                if (gen != generation) return;
            }
            int i = (int)kind;
            if (gen != generation || waiters[i].Count == 0) return; // 그사이 처음 상태로 되돌림·이미 답함
            if (status[i] != PermissionStatus.NotDetermined)
            {
                FinishWaiters(i); // 그사이 창에서 정해 버린 경우
                return;
            }
            SimulateStatus(kind, Cfg.grantOnRequest[i] ? PermissionStatus.Granted : PermissionStatus.Denied);
        }

        void FinishWaiters(int i)
        {
            if (waiters[i].Count == 0) return;
            var done = waiters[i].ToArray();
            waiters[i].Clear();
            foreach (var w in done) w.SetResult(status[i]);
        }

        public void OpenSettings() =>
            Debug.Log("[FakeNative] 설정 앱 열기(가짜). Fake Native 창에서 상태를 바꿔 '설정 앱에서 바꾸고 돌아옴'을 흉내 내세요.");

        // ---- 에디터 창에서 부르는 흉내 ----

        // 팝업 응답 또는 설정 앱에서 바꾸고 돌아온 것처럼 상태를 바꾼다.
        // "안 물어봄"으로 되돌리면 기다리던 요청은 계속 기다리고, 타이머가 끝나면 팝업 답을 받는다.
        public void SimulateStatus(PermissionKind kind, PermissionStatus value)
        {
            int i = (int)kind;
            bool changed = status[i] != value;
            status[i] = value;
            if (value != PermissionStatus.NotDetermined) FinishWaiters(i);
            if (changed) Changed?.Invoke(kind, value);
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
        public static readonly int KindCount = Enum.GetValues(typeof(PermissionKind)).Length;

        // PermissionKind 순서(모션, 카메라, 헤드폰 모션, 알림)
        public PermissionStatus[] startStatus = new PermissionStatus[KindCount];
        public bool[] grantOnRequest = AllTrue(KindCount);
        public float requestDelaySec = 0.6f; // 사용자가 팝업을 누르기까지 걸리는 시간 흉내
        public HeadphoneStatus headphones = HeadphoneStatus.Connected;
        public string headphoneName = "AirPods Pro";

        static bool[] AllTrue(int n)
        {
            var a = new bool[n];
            for (int i = 0; i < n; i++) a[i] = true;
            return a;
        }

        // 저장된 값이 예전 모양(권한 개수가 다름)이어도 깨지지 않게 맞춘다.
        public void Normalize()
        {
            if (startStatus == null || startStatus.Length != KindCount) Array.Resize(ref startStatus, KindCount);
            if (grantOnRequest == null || grantOnRequest.Length != KindCount)
            {
                int old = grantOnRequest?.Length ?? 0;
                Array.Resize(ref grantOnRequest, KindCount);
                for (int i = old; i < KindCount; i++) grantOnRequest[i] = true;
            }
            if (requestDelaySec < 0f) requestDelaySec = 0f;
            if (string.IsNullOrEmpty(headphoneName)) headphoneName = "AirPods Pro";
        }
    }
}
