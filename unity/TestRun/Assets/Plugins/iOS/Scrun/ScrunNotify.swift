import Foundation
import UserNotifications

// 폰 로컬 알림(주간 목표 리마인더 BL-46, 계획 0079). 서버 푸시가 아니라 무료 개발 팀에서도 됨.
// C#(WeeklyReminder)이 언제·무엇을 보낼지 다 정해 날짜·시각(폰 시계 기준)으로 넘기고, 여기서는 지우고 예약만 한다.
// 달력 트리거에 시간대를 안 넣어(floating) 예약한 뒤 여행·시계 변경이 있어도 그 날 그 시각에 울림.
// 권한은 온보딩 A-07·설정 스위치에서 ScrunPermissions로 받음. 권한이 없으면 iOS가 조용히 안 보여 줌.
enum ScrunNotify {
    static func replace(_ a: [String: Any]) {
        let center = UNUserNotificationCenter.current()
        let ids = a["ids"] as? [String] ?? []
        center.removePendingNotificationRequests(withIdentifiers: ids)
        // 이미 받은 리마인더도 알림 센터에서 지움: 다시 짤 때는 숫자("N번 남았어요")가 바뀌었을 수 있어 옛 알림이 틀린 말이 됨(의도)
        center.removeDeliveredNotifications(withIdentifiers: ids)
        for item in a["items"] as? [[String: Any]] ?? [] {
            guard let id = item["id"] as? String,
                  let year = item["year"] as? Int, let month = item["month"] as? Int, let day = item["day"] as? Int,
                  let hour = item["hour"] as? Int, let minute = item["minute"] as? Int else { continue }
            let content = UNMutableNotificationContent()
            content.title = item["title"] as? String ?? ""
            content.body = item["body"] as? String ?? ""
            content.sound = .default
            let when = DateComponents(year: year, month: month, day: day, hour: hour, minute: minute)
            let trigger = UNCalendarNotificationTrigger(dateMatching: when, repeats: false)
            center.add(UNNotificationRequest(identifier: id, content: content, trigger: trigger)) { error in
                if let error = error { NSLog("%@", "[Scrun] 알림 예약 실패 \(id): \(error)") }
            }
        }
    }
}
