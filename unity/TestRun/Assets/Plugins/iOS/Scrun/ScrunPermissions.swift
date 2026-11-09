import AVFoundation
import CoreMotion
import Foundation
import UIKit
import UserNotifications

// 앱이 묻는 권한 4가지. C# PermissionKind·PermissionStatus와 같은 이름(IosNative.cs).
enum ScrunPermissionKind: String, CaseIterable {
    case motion          // 모션 및 피트니스(필수). Info.plist NSMotionUsageDescription
    case camera          // 카메라: 계기판 촬영(필수). NSCameraUsageDescription
    case headphoneMotion // 헤드폰 모션: 에어팟 센서(선택). 문구는 NSMotionUsageDescription을 같이 씀
    case notifications   // 알림(선택)
}

enum ScrunPermissionStatus: String {
    case notDetermined, granted, denied, restricted
}

final class ScrunPermissions {
    static let shared = ScrunPermissions()

    // 알림 상태는 iOS가 비동기로만 알려 줘서 여기 담아 두고, 앱이 앞으로 올 때마다 다시 읽는다.
    private var notificationStatus: ScrunPermissionStatus = .notDetermined
    // C#에 마지막으로 알린 상태. 바뀐 것만 이벤트로 보낸다.
    private var lastSent: [ScrunPermissionKind: ScrunPermissionStatus] = [:]
    // 팝업 답을 기다리는 요청. 같은 권한을 동시에 여러 번 요청해도 팝업은 하나, 답은 모두 같게.
    private var waiting: [ScrunPermissionKind: [(ScrunPermissionStatus) -> Void]] = [:]
    private let activity = CMMotionActivityManager()
    private var started = false

    func start() {
        guard !started else { return }
        started = true
        for k in ScrunPermissionKind.allCases { lastSent[k] = status(k) }
        // 알림 상태는 비동기로만 온다. 시작을 막지 않고 읽어서, 처음 값(notDetermined)과 다르면 이벤트로 알린다.
        refresh()
        // 팝업에 답했을 때, 설정 앱에서 바꾸고 돌아왔을 때 모두 앱이 다시 활성화된다.
        NotificationCenter.default.addObserver(forName: UIApplication.didBecomeActiveNotification,
                                               object: nil, queue: .main) { [weak self] _ in
            self?.refresh()
        }
    }

    func status(_ k: ScrunPermissionKind) -> ScrunPermissionStatus {
        switch k {
        case .motion: return Self.map(CMMotionActivityManager.authorizationStatus())
        case .camera: return Self.map(AVCaptureDevice.authorizationStatus(for: .video))
        case .headphoneMotion: return Self.map(CMHeadphoneMotionManager.authorizationStatus())
        case .notifications: return notificationStatus
        }
    }

    // 지금 상태를 다시 읽고 바뀐 것만 C#에 알린다.
    func refresh() {
        emitChanges()
        UNUserNotificationCenter.current().getNotificationSettings { s in
            DispatchQueue.main.async {
                self.notificationStatus = Self.map(s.authorizationStatus)
                self.emitChanges()
            }
        }
    }

    private func emitChanges() {
        for k in ScrunPermissionKind.allCases {
            let s = status(k)
            if lastSent[k] != s {
                lastSent[k] = s
                ScrunBridge.emit("permission", ["kind": k.rawValue, "status": s.rawValue])
            }
        }
        ScrunHeadphones.shared.permissionMaybeChanged()
    }

    // 안 물어봤으면 시스템 팝업을 띄우고 답을 기다린다. 이미 정해졌으면 바로 그 상태(iOS는 두 번 묻지 않음).
    // 팝업이 뜨지 않는 경우(시뮬레이터의 모션 등)엔 notDetermined 그대로 돌려준다.
    func request(_ k: ScrunPermissionKind, _ done: @escaping (ScrunPermissionStatus) -> Void) {
        if k != .notifications {
            let now = status(k)
            if now != .notDetermined {
                done(now)
                return
            }
        }
        if waiting[k] != nil {
            waiting[k]!.append(done)
            return
        }
        waiting[k] = [done]
        let finish = { [self] in
            emitChanges()
            let s = status(k)
            for w in waiting.removeValue(forKey: k) ?? [] { w(s) }
        }
        switch k {
        case .camera:
            AVCaptureDevice.requestAccess(for: .video) { _ in DispatchQueue.main.async(execute: finish) }
        case .motion:
            guard CMMotionActivityManager.isActivityAvailable() else {
                finish() // 이 기기엔 모션 활동이 없음(시뮬레이터 등): 팝업 없음
                return
            }
            // 모션 권한은 따로 묻는 API가 없어서, 활동 기록을 한 번 읽으면 팝업이 뜬다.
            let now = Date()
            activity.queryActivityStarting(from: now, to: now, to: .main) { _, _ in
                ScrunPromptWait.until({ self.status(.motion) != .notDetermined }, then: finish)
            }
        case .headphoneMotion:
            ScrunHeadphones.shared.requestPermission(finish)
        case .notifications:
            // 이미 정해졌으면 팝업 없이 바로 답이 온다(담아 둔 값이 낡았어도 여기서 새로 읽음).
            let center = UNUserNotificationCenter.current()
            center.requestAuthorization(options: [.alert, .sound, .badge]) { _, _ in
                center.getNotificationSettings { s in
                    DispatchQueue.main.async {
                        self.notificationStatus = Self.map(s.authorizationStatus)
                        finish()
                    }
                }
            }
        }
    }

    private static func map(_ s: CMAuthorizationStatus) -> ScrunPermissionStatus {
        switch s {
        case .authorized: return .granted
        case .denied: return .denied
        case .restricted: return .restricted
        case .notDetermined: return .notDetermined
        @unknown default: return .denied
        }
    }

    private static func map(_ s: AVAuthorizationStatus) -> ScrunPermissionStatus {
        switch s {
        case .authorized: return .granted
        case .denied: return .denied
        case .restricted: return .restricted
        case .notDetermined: return .notDetermined
        @unknown default: return .denied
        }
    }

    private static func map(_ s: UNAuthorizationStatus) -> ScrunPermissionStatus {
        switch s {
        case .authorized, .provisional, .ephemeral: return .granted
        case .denied: return .denied
        case .notDetermined: return .notDetermined
        @unknown default: return .denied
        }
    }
}

// 권한 팝업 답 기다리기. 모션·헤드폰 모션은 "팝업이 닫혔다"는 콜백이 따로 없어 상태를 짧게 다시 읽는다.
// 팝업이 떠 있는 동안엔 앱이 비활성이 되므로, "활성 상태로 2초 연속"이 될 때까지 기다린다(비활성이 되면 다시 셈).
// 팝업이 안 뜨는 경우(이어폰이 없거나 시뮬레이터라 iOS가 안 물음, 제어 센터를 내렸다 올림)도 2초 뒤 끝난다.
enum ScrunPromptWait {
    static let quiet: TimeInterval = 2.0

    static func until(_ decided: @escaping () -> Bool, then done: @escaping () -> Void) {
        var activeSince: Date? = nil
        func tick() {
            if UIApplication.shared.applicationState == .active {
                let since = activeSince ?? Date()
                activeSince = since
                if decided() || Date().timeIntervalSince(since) >= quiet {
                    done()
                    return
                }
            } else {
                activeSince = nil // 팝업이 떠 있음(또는 잠깐 다른 화면)
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.2, execute: tick)
        }
        tick()
    }
}
