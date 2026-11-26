using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scrun.App
{
    // 주간 목표 리마인더(BL-46, 계획 0079). 폰 로컬 알림이라 서버 푸시가 없다.
    // - 언제: 목표를 아직 못 채운 주기의 마지막 이틀(끝나기 전날·마지막 날) 오후 7시. 하루에 여러 번 달려도 각각 1회라 남은 날이 적어도 보냄
    // - 날짜는 서버 주기(사용자 시간대, 가입 때 Asia/Seoul) 그대로, 시각은 폰 시계 19시(달력 트리거: 여행·시계 변경에도 그 날 19시)
    // - 다음 주기 것도 미리 예약(앱을 안 열고 주기가 넘어가도 옴). 주간 목표를 받을 때마다 다시 짬(Api.Users, 인증 끝)
    // - 스위치(설정 H-10)는 이 기기에만. 한 번도 안 만졌으면 알림 권한을 따름(허용이면 켜짐)
    // 계산(Plan)은 순수 함수라 EditMode로 확인한다. 문구는 임시, 상준 확인
    public static class WeeklyReminder
    {
        public const int Hour = 19;
        const string PrefKey = "scrun.reminder.weekly"; // "1" 켬, "0" 끔, 없으면 권한으로
        static readonly string[] Ids = { "weekly.0", "weekly.1", "weekly.2", "weekly.3" }; // 이번·다음 주기 × 이틀
        public static IReadOnlyCollection<string> AllIds => Ids;

        public const string Title = "주간 목표";
        public static string DayBefore(int left) => $"이번 주 목표까지 {left}번 남았어요. 오늘 달려 볼까요?";
        public static string LastDay(int left) => $"오늘이 이번 주 목표 마지막 날이에요. {left}번 남았어요.";

        static WeeklyGoal latestGoal; // 스위치·권한이 바뀌면 바로 다시 짜려고 마지막으로 받은 주간 목표
        static bool watchingPermission;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            latestGoal = null;
            watchingPermission = false;
            Session.Cleared -= Forget;
            Session.Cleared += Forget;
        }

        // 로그아웃·계정 삭제: 다른 사람이 로그인할 수 있으니 예약과 받은 목표를 지움(스위치는 기기 설정이라 그대로)
        internal static void Forget()
        {
            latestGoal = null;
            Apply(Array.Empty<LocalNotice>());
        }

        // ---- 스위치 ----

        // 켜져 있으면 true, 꺼져 있으면 false, 스위치를 안 만졌고 권한을 아직 모르면 null
        // (아이폰은 권한 상태를 비동기로 알려 줌: 앱을 막 켰을 때 "모름"을 "꺼짐"으로 보고 예약을 지우지 않게)
        public static bool? Enabled
        {
            get
            {
                var v = PlayerPrefs.GetString(PrefKey, "");
                if (v == "1") return true;
                if (v == "0") return false;
                var status = NotificationStatus();
                return status == PermissionStatus.NotDetermined ? (bool?)null : status == PermissionStatus.Granted;
            }
        }

        // 설정 화면에 켜져 보이는지: 스위치 + 알림 허용(설정 앱에서 알림을 끄면 꺼져 보임)
        public static bool ShownOn => Enabled == true && NotificationStatus() == PermissionStatus.Granted;

        public static void SetEnabled(bool on)
        {
            PlayerPrefs.SetString(PrefKey, on ? "1" : "0");
            PlayerPrefs.Save();
            Reschedule(null);
        }

        static PermissionStatus NotificationStatus()
        {
            try { return Native.Permissions.GetStatus(PermissionKind.Notifications); }
            catch (Exception) { return PermissionStatus.Denied; } // 네이티브 없는 빌드
        }

        // ---- 다시 짜기 ----

        // 주간 목표를 받을 때마다(홈·주간 목표·목표 바꿈·인증 끝). goal이 null이면 마지막 값으로. 꺼졌거나 목표가 없으면 예약을 지움
        public static void Reschedule(WeeklyGoal goal)
        {
            WatchPermission();
            if (goal != null) latestGoal = goal;
            var on = Enabled;
            if (on == null) return; // 권한을 아직 모름 → 그대로 두고, 알게 되면(Changed) 다시 짬
            Apply(on == true && latestGoal != null ? Plan(latestGoal, DateTime.Now) : Array.Empty<LocalNotice>());
        }

        // 인증이 끝나 주간 목표가 바뀌었을 때: 다시 받아 다시 짬(Api.Users가 받으면서 Reschedule). 실패해도 화면엔 안 알림
        public static async System.Threading.Tasks.Task RefreshQuietlyAsync()
        {
            try { await Api.Users.GetWeeklyGoalAsync(); }
            catch (Exception e) { Debug.LogWarning("[알림] 주간 목표를 못 받아 리마인더를 그대로 둠: " + e.Message); }
        }

        // 설정 앱에서 허용·거부하고 돌아오거나 앱 시작 때 권한을 알게 되면 다시 짬
        static void WatchPermission()
        {
            if (watchingPermission) return;
            try
            {
                Native.Permissions.Changed += (kind, _) => { if (kind == PermissionKind.Notifications) Reschedule(null); };
                watchingPermission = true;
            }
            catch (Exception) { } // 네이티브 없는 빌드
        }

        // 개발 빌드 설정 화면에서 아이폰 확인용: 진짜 예약을 지우고 1분 뒤 하나만(다음에 주간 목표를 받으면 원래대로 다시 짬)
        public static void TestInOneMinute() =>
            Apply(new[] { new LocalNotice { Id = Ids[0], At = DateTime.Now.AddMinutes(1), Title = Title, Body = DayBefore(2) } });

        static void Apply(IReadOnlyList<LocalNotice> notices)
        {
            try { Native.Notifications.Replace(Ids, notices); }
            catch (Exception e) { Debug.LogWarning("[알림] 리마인더를 예약하지 못함: " + e.Message); } // 알림은 부가 기능 → 화면 흐름을 막지 않음
        }

        // 보낼 알림(폰 시계 nowLocal보다 지난 것은 뺌). 이번 주기: 남은 횟수가 있을 때. 다음 주기: 아직 0회라 다음 목표(바꿔 둔 값이 있으면 그것)
        public static List<LocalNotice> Plan(WeeklyGoal g, DateTime nowLocal)
        {
            var list = new List<LocalNotice>();
            if (g?.TargetCount is not int target || g.CycleEnd is not DateTime end) return list;
            AddCycle(list, 0, end.Date, target - g.DoneCount, nowLocal);
            AddCycle(list, 2, end.Date.AddDays(7), g.NextTargetCount ?? target, nowLocal); // 주기 = 7일(계획 0054)
            return list;
        }

        static void AddCycle(List<LocalNotice> list, int firstSlot, DateTime lastDay, int left, DateTime nowLocal)
        {
            if (left <= 0) return;
            AddIfAhead(list, Ids[firstSlot], lastDay.AddDays(-1).AddHours(Hour), DayBefore(left), nowLocal);
            AddIfAhead(list, Ids[firstSlot + 1], lastDay.AddHours(Hour), LastDay(left), nowLocal);
        }

        static void AddIfAhead(List<LocalNotice> list, string id, DateTime at, string body, DateTime nowLocal)
        {
            if (at > nowLocal) list.Add(new LocalNotice { Id = id, At = at, Title = Title, Body = body });
        }
    }
}
