using UnityEngine;

namespace Scrun.App
{
    // 맵 입장 전 모션 권한 확인 결과
    public enum MotionGate
    {
        Go,      // 허용 → 바로 맵
        Ask,     // 아직 안 물어봄 → 시스템 팝업
        Blocked, // 거부·제한 → I-05(설정 열기)
    }

    // 권한 안내(A-07)에서 권한을 다 물은 뒤 갈 곳
    public enum AfterAsking
    {
        MotionDenied, // I-05
        Earphone,     // A-09·A-10
    }

    // 가입 뒤·앱 시작 때의 권한 안내(A-07) → (모션 거부면 I-05) → 이어폰(A-09·A-10) → 홈 흐름(계획 0025).
    // 이어폰 화면까지 끝냈는지는 이 기기에 저장한다(중간에 앱을 끄거나 설정 앱을 다녀오며 앱이 다시 켜져도 이어지게).
    // 끝낸 뒤에는 권한을 "나중에" 미뤘어도 다시 보여 주지 않는다(사용자가 고른 것). 빠진 모션 권한은 맵 입장 때 BeforeMap이 잡는다.
    // 판단은 순수 함수로 두고 화면과 떼어 테스트한다.
    public static class Onboarding
    {
        const string DoneKey = "scrun.onboarding.done";

        // A-07 "권한 설정하기"가 묻는 순서(Figma: 4개를 한 번에, 계획 0025 정한 것 1)
        public static readonly PermissionKind[] AskOrder =
        {
            PermissionKind.Motion, PermissionKind.HeadphoneMotion, PermissionKind.Camera, PermissionKind.Notifications,
        };

        // 권한 요청 하나를 이만큼 기다린다. 답이 안 오면(이어폰 없이 헤드폰 모션을 묻는 등, 가설) 지금 상태로 넘어가 버튼이 잠기지 않게
        public const float AskTimeoutSec = 10f;

        // 권한 안내부터 보여 줄지: 이어폰 화면까지 안 끝냈으면. 다시 깔면 저장(NSUserDefaults)도 지워져 다시 보여 줌
        public static bool NeedsOnboarding(bool done) => !done;

        // 권한을 다 물은 뒤: 맵에 못 들어가는 모션 상태(BeforeMap이 막는 것)면 I-05
        public static AfterAsking Next(PermissionStatus motion) =>
            BeforeMap(motion) == MotionGate.Blocked ? AfterAsking.MotionDenied : AfterAsking.Earphone;

        public static MotionGate BeforeMap(PermissionStatus motion) => motion switch
        {
            PermissionStatus.Granted => MotionGate.Go,
            PermissionStatus.NotDetermined => MotionGate.Ask,
            _ => MotionGate.Blocked,
        };

        // 가입이 끝났을 때·로그인된 채 앱을 켰을 때: 권한 안내부터 또는 바로 홈
        public static void Begin(Navigator nav)
        {
            if (NeedsOnboarding(IsDone)) nav.StartFlow(new PermissionsScreen());
            else nav.ShowTabs(AppTab.Home);
        }

        // 이어폰 화면이 끝나면
        public static void Finish(Navigator nav)
        {
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
            nav.ShowTabs(AppTab.Home);
        }

        static bool IsDone => PlayerPrefs.GetInt(DoneKey, 0) == 1;

        // 지금 모션 권한. 아이폰 구현이 없는 빌드(계획 0018의 5번 전)에서는 Native가 예외를 던진다 —
        // 허용으로 보고 넘어가지 않는다(Native.cs: 몰래 허용을 주면 권한 없이 달리기까지 가 버림)
        public static PermissionStatus MotionStatus() => Native.Permissions.GetStatus(PermissionKind.Motion);

        // 시스템 팝업으로 묻고 답을 기다린다. AskTimeoutSec 안에 답이 없으면 기다리기를 그만두고 지금 상태를 돌려준다
        // (팝업 자체는 그대로 둠, 나중에 답하면 Permissions.Changed로 화면이 따라 바뀜)
        public static async Awaitable<PermissionStatus> Ask(PermissionKind kind)
        {
            bool answered = false;
            var result = PermissionStatus.NotDetermined;
            Wait();
            float end = Time.realtimeSinceStartup + AskTimeoutSec; // 창이 뒤에 있어도 실제 시간으로
            while (!answered && Time.realtimeSinceStartup < end) await Awaitable.NextFrameAsync();
            if (!answered) Debug.LogWarning($"[Onboarding] {kind} 권한 답이 {AskTimeoutSec}초 안에 안 옴 → 지금 상태로 진행");
            return answered ? result : Native.Permissions.GetStatus(kind);

            async void Wait()
            {
                result = await Native.Permissions.RequestAsync(kind);
                answered = true;
            }
        }
    }

    // 권한·이어폰 화면 문구(Figma A-07·A-09·A-10·I-05, 상준 문구 확정 전까지 임시)
    public static class OnboardingText
    {
        public readonly struct PermissionInfo
        {
            public readonly string Title, Body, IconClass;
            public readonly bool Required;

            public PermissionInfo(string title, string body, bool required, string iconClass)
            {
                Title = title;
                Body = body;
                Required = required;
                IconClass = iconClass;
            }
        }

        // 권한 줄 하나(이름·설명·필수·아이콘)
        public static PermissionInfo Permission(PermissionKind kind) => kind switch
        {
            PermissionKind.Motion => new PermissionInfo("모션 및 피트니스", "걸음 수와 발 충격으로 속도를 추정해요", true, "icon-motion"),
            PermissionKind.HeadphoneMotion => new PermissionInfo("헤드폰 모션 (AirPods)", "이어폰 센서로 공중 시간을 보완해요", false, "icon-headphone"),
            PermissionKind.Camera => new PermissionInfo("카메라", "운동 직후 계기판을 촬영해 인증해요", true, "icon-camera"),
            PermissionKind.Notifications => new PermissionInfo("알림", "친구가 달리기 시작할 때 알려줘요", false, "icon-bell"),
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        // 권한 줄 오른쪽 칩: 허용 "허용됨", 거부·제한 "꺼짐", 아직이면 필수·선택
        public static string Chip(bool required, PermissionStatus status) => status switch
        {
            PermissionStatus.Granted => "허용됨",
            PermissionStatus.Denied => "꺼짐",
            PermissionStatus.Restricted => "꺼짐",
            _ => required ? "필수" : "선택",
        };

        // 이어폰 화면: 제목, 설명, 이어폰 줄 이름·설명, 칩. 헤드폰 모션 권한이 꺼져 있으면 "사용 가능"이라고 하지 않는다
        public static (string title, string lead, string name, string detail, string chip) Earphone(
            HeadphoneStatus status, string deviceName, PermissionStatus headphoneMotion)
        {
            var name = deviceName ?? "이어폰";
            if (status == HeadphoneStatus.NotConnected)
                return ("이어폰을 연결해 보세요", "AirPods가 있으면 공중 시간까지 측정해 속도가 더 안정적이에요 (선택)",
                        "연결된 이어폰 없음", "블루투스에서 AirPods를 연결해 주세요", "미연결");
            if (status == HeadphoneStatus.ConnectedNoMotion)
                return ("이어폰이 연결됐어요", "이 이어폰은 움직임 센서가 없어 폰 센서로만 속도를 추정해요",
                        name, "헤드폰 모션 없음", "연결됨");
            if (headphoneMotion == PermissionStatus.Denied || headphoneMotion == PermissionStatus.Restricted)
                return ("이어폰이 연결됐어요", "헤드폰 모션이 꺼져 있어 폰 센서로만 속도를 추정해요",
                        name, "헤드폰 모션 꺼짐 · 설정에서 켤 수 있어요", "연결됨");
            if (headphoneMotion == PermissionStatus.NotDetermined) // 묻는 건 사용자가 "다음"을 눌렀을 때
                return ("이어폰이 연결됐어요", "다음을 누르면 헤드폰 모션 사용을 물어봐요",
                        name, "헤드폰 모션 권한 확인 전", "연결됨");
            return ("이어폰이 연결됐어요", "공중 시간 정보를 함께 사용해 속도를 추정해요", name, "헤드폰 모션 사용 가능", "연결됨");
        }
    }
}
