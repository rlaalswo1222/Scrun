using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Scrun.App
{
    // 폰에서 예약하는 알림 하나(서버 푸시 아님). Id가 같으면 덮어씀.
    // At은 폰 시계 기준 날짜·시각(시간대 없음): 예약한 뒤 시간대·시계가 바뀌어도 그 날 그 시각에 울림(iOS 달력 트리거)
    public sealed class LocalNotice
    {
        public string Id;
        public DateTime At;
        public string Title;
        public string Body;
    }

    // 폰 로컬 알림(주간 목표 리마인더 BL-46, 계획 0079).
    //   Native.Notifications.Replace(ids, notices); // ids를 모두 지우고 notices를 예약
    // 아이폰은 Swift(ScrunNotify.swift, UNUserNotificationCenter), 에디터는 예약 목록만 들고 로그. 권한은 Native.Permissions(Notifications)
    public interface INotifications
    {
        void Replace(IReadOnlyCollection<string> ids, IReadOnlyList<LocalNotice> notices);
    }

    internal sealed class FakeNotifications : INotifications
    {
        public static readonly FakeNotifications Instance = new FakeNotifications();

        readonly Dictionary<string, LocalNotice> pending = new Dictionary<string, LocalNotice>();
        public IReadOnlyList<LocalNotice> Pending => pending.Values.OrderBy(n => n.At).ToList();

        public void Replace(IReadOnlyCollection<string> ids, IReadOnlyList<LocalNotice> notices)
        {
            foreach (var id in ids) pending.Remove(id);
            foreach (var n in notices) pending[n.Id] = n;
            Debug.Log(notices.Count == 0 ? "[알림] 예약 없음"
                : "[알림] 예약: " + string.Join(" / ", notices.Select(n => $"{n.At:MM-dd HH:mm} {n.Body}")));
        }
    }
}
