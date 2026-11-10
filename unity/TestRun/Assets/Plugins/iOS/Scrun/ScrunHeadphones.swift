import AVFoundation
import CoreMotion
import Foundation

// 이어폰 연결 상태. C# HeadphoneStatus와 같은 이름(IosNative.cs).
//   connected         헤드폰 모션이 연결됨(에어팟 3·Pro·Max 등 + 헤드폰 모션 권한 허용)
//   connectedNoMotion 오디오 출력은 이어폰인데 헤드폰 모션이 없음(모션 없는 이어폰, 권한 전, 또는 지켜보는 중이 아님)
//   notConnected      이어폰 없음
// 판단 근거(가설, 아이폰에서 확인): 오디오 출력 경로 + CMHeadphoneMotionManager 연결 알림.
// 연결 알림을 받으려면 모션 업데이트를 켜 둬야 한다고 보고, C#이 지켜보는 동안(watch, Headphones.Changed 구독 중)에만
// 켠다. 앱 내내 켜 두면 에어팟·아이폰 배터리를 쓴다.
// 속도 센서(ScrunSpeed, 계획 0024)도 이 manager를 같이 쓴다(헤드폰 모션 manager를 여러 개 만들지 않게).
// 속도 센서가 켜져 있으면 핸들러 있는 업데이트(샘플을 받음)로, 아니면 핸들러 없이(연결 알림만) 켠다.
final class ScrunHeadphones: NSObject, CMHeadphoneMotionManagerDelegate {
    static let shared = ScrunHeadphones()

    let motion = CMHeadphoneMotionManager()
    private var motionConnected = false
    private var updating = false
    private var updatingWithHandler = false
    private var speedHandler: (queue: OperationQueue, handler: CMHeadphoneMotionManager.DeviceMotionHandler)?
    // 핸들러 방식을 바꾸느라 껐다 켠 직후: 끌 때의 끊김 알림이 늦게(메인 async) 와서 연결 상태를 덮지 않게 이 시각까지 무시
    private var switchingUntil = 0.0
    static let switchQuietS = 1.0
    private var watching = false
    private var requesting = false
    private var started = false
    private var lastSent: (status: String, name: String?)?

    private static let headphonePorts: Set<AVAudioSession.Port> = [
        .headphones, .bluetoothA2DP, .bluetoothLE, .bluetoothHFP,
    ]

    private static var authorized: Bool { CMHeadphoneMotionManager.authorizationStatus() == .authorized }

    func start() {
        guard !started else { return }
        started = true
        motion.delegate = self
        NotificationCenter.default.addObserver(forName: AVAudioSession.routeChangeNotification,
                                               object: nil, queue: .main) { [weak self] _ in
            self?.emitIfChanged()
        }
        lastSent = current()
    }

    func current() -> (status: String, name: String?) {
        let port = AVAudioSession.sharedInstance().currentRoute.outputs
            .first { Self.headphonePorts.contains($0.portType) }
        if motionConnected { return ("connected", port?.portName ?? "이어폰") }
        if let p = port { return ("connectedNoMotion", p.portName) }
        return ("notConnected", nil)
    }

    // C#이 이어폰 상태를 지켜보기 시작·끝(Headphones.Changed 구독자가 생기거나 없어질 때)
    func setWatching(_ on: Bool) {
        watching = on
        syncUpdates()
        emitIfChanged()
    }

    // 속도 센서가 샘플을 받기 시작·끝(nil). 이어폰 연결 알림은 그대로 여기서 C#으로 보낸다
    func setSpeedHandler(_ queue: OperationQueue?, _ handler: CMHeadphoneMotionManager.DeviceMotionHandler?) {
        if let queue, let handler { speedHandler = (queue, handler) } else { speedHandler = nil }
        syncUpdates()
        emitIfChanged()
    }

    // 헤드폰 모션 샘플이 올 수 있는 상태(업데이트가 켜져 있고 연결 알림을 받았고 허용됨)
    var motionReady: Bool { updating && motionConnected && Self.authorized }

    // 권한이 바뀌었을 수 있을 때(ScrunPermissions가 부름)
    func permissionMaybeChanged() {
        syncUpdates()
        emitIfChanged()
    }

    // 헤드폰 모션 권한 팝업: 따로 묻는 API가 없어 모션 업데이트를 켜면 iOS가 묻는다.
    // 이어폰이 없을 때도 팝업이 뜨는지는 가설(아이폰에서 확인). 안 뜨면 ScrunPromptWait가 2초 뒤 끝낸다.
    func requestPermission(_ done: @escaping () -> Void) {
        guard motion.isDeviceMotionAvailable else {
            done() // 이 기기는 헤드폰 모션을 못 받음(시뮬레이터 등)
            return
        }
        requesting = true
        if !updating {
            motion.startDeviceMotionUpdates()
            updating = true
        }
        ScrunPromptWait.until({ CMHeadphoneMotionManager.authorizationStatus() != .notDetermined }) { [self] in
            requesting = false
            syncUpdates() // 지켜보는 중이고 허용됐을 때만 계속 켜 둠
            done()
        }
    }

    // 모션 업데이트는 ((지켜보는 중 || 속도 센서) && 허용) 또는 권한을 묻는 중일 때만 켠다.
    // 속도 센서가 켜져 있고 허용이면 핸들러 있는 업데이트. 방식이 바뀌면 껐다 바로 켠다(연결 상태는 그대로 두고,
    // 그 뒤 switchQuietS 동안 온 끊김 알림은 무시한 뒤 오디오 출력으로 다시 확인, PR #36 리뷰)
    private func syncUpdates() {
        let usable = Self.authorized && motion.isDeviceMotionAvailable
        let withHandler = speedHandler != nil && usable
        let want = requesting || withHandler || (watching && usable)
        if want && updating && withHandler != updatingWithHandler {
            motion.stopDeviceMotionUpdates()
            updating = false
            switchingUntil = ProcessInfo.processInfo.systemUptime + Self.switchQuietS
        }
        if want && !updating {
            if withHandler, let h = speedHandler {
                motion.startDeviceMotionUpdates(to: h.queue, withHandler: h.handler)
            } else {
                motion.startDeviceMotionUpdates()
            }
            updating = true
            updatingWithHandler = withHandler
        } else if !want && updating {
            motion.stopDeviceMotionUpdates()
            updating = false
            updatingWithHandler = false
            motionConnected = false // 업데이트를 끄면 연결 알림도 안 오므로 모른다고 본다
        }
    }

    private func emitIfChanged() {
        let now = current()
        if let last = lastSent, last.status == now.status, last.name == now.name { return }
        lastSent = now
        ScrunBridge.emit("headphones", ["status": now.status, "name": now.name ?? NSNull()])
    }

    // ---- CMHeadphoneMotionManagerDelegate (어느 스레드로 올지 정해져 있지 않아 메인으로 옮김) ----

    func headphoneMotionManagerDidConnect(_ manager: CMHeadphoneMotionManager) {
        DispatchQueue.main.async {
            guard self.updating else { return }
            self.motionConnected = true
            self.emitIfChanged()
        }
    }

    func headphoneMotionManagerDidDisconnect(_ manager: CMHeadphoneMotionManager) {
        DispatchQueue.main.async {
            let quiet = self.switchingUntil - ProcessInfo.processInfo.systemUptime
            if quiet > 0 && self.updating {
                // 방식 바꾸기로 껐다 켠 탓일 수 있음 → 조용한 시간이 끝난 뒤 이어폰이 오디오 출력에 없을 때만 끊김으로
                DispatchQueue.main.asyncAfter(deadline: .now() + quiet) {
                    if self.hasHeadphonePort { return }
                    self.motionConnected = false
                    self.emitIfChanged()
                }
                return
            }
            self.motionConnected = false
            self.emitIfChanged()
        }
    }

    private var hasHeadphonePort: Bool {
        AVAudioSession.sharedInstance().currentRoute.outputs.contains { Self.headphonePorts.contains($0.portType) }
    }
}
