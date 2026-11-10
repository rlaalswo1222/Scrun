import CoreMotion
import Foundation
import UIKit

// 러닝머신 속도 센서(계획 0024). 폰 가속도 100 Hz + 에어팟 헤드폰 모션을 직렬 큐에서 SpeedEstimator에 넣고,
// 최신 결과를 잠금으로 보호한 칸 하나에 둔다. C#(IosSpeedSensor)은 매 프레임 scrun_speed_read로 그 칸만 읽는다.
// ScrunBridge(JSON 통로)는 쓰지 않는다(ADR-0009). C 함수 이름·칸 순서는 C# IosSpeedSensor와 짝이다. 바꾸면 양쪽을 같이.
// 모든 C 함수는 메인 스레드에서 불린다. 추정기는 모션 큐에서만 만지고, 메인은 필요하면 큐에 sync한다
// (큐는 메인을 기다리지 않으므로 서로 막히지 않는다).
final class ScrunSpeed {
    static let shared = ScrunSpeed()

    static let slotVersion = 1.0
    static let slotCount = 11
    // 흐름 판정(계획 0020, 숫자는 가설이고 아이폰에서 고침)
    static let phoneOkPerSec = 80, airOkPerSec = 20, lostAfterS = 2.0

    enum Flow: Double { case off = 0, waiting = 1, ok = 2, lost = 3 }

    private let phone = CMMotionManager()
    private let dq = DispatchQueue(label: "scrun.speed", qos: .userInitiated)
    private lazy var queue: OperationQueue = {
        let q = OperationQueue()
        q.maxConcurrentOperationCount = 1
        q.underlyingQueue = dq
        return q
    }()

    // ---- 모션 큐에서만 ----
    private var est = SpeedEstimator()

    // ---- 잠금으로 보호(큐가 쓰고 메인이 읽음) ----
    private let lock = NSLock()
    private var snap: SpeedEstimator.Snapshot?
    private var steps = 0             // 누적 걸음(봉우리 세기, 샘플마다 늘어남)
    private var phoneTimes: [Double] = [] // 최근 2초 샘플 시각(기기 부팅 뒤 초)
    private var airTimes: [Double] = []

    // ---- 메인에서만 ----
    private var running = false
    private var t0 = 0.0
    private var points: [SpeedEstimator.Point] = [] // 지금 보정(C#이 확정·불러온 것)
    private var openStage: (v: Double, t: Double)?
    private var stageBroken = false   // 단계 중에 백그라운드로 감
    private var background = false
    private var phoneFlow = Flow.off
    private var airFlow = Flow.off
    private var airSince = 0.0        // 에어팟을 쓸 수 있게 된 시각(도중에 연결해도 2초는 기다린 뒤 Lost)
    private var observers: [NSObjectProtocol] = []

    private init() {}

    private static var uptime: Double { ProcessInfo.processInfo.systemUptime }

    // MARK: 켜기·끄기

    func start() {
        guard !running else { return }
        running = true
        t0 = Self.uptime
        phoneFlow = .waiting
        airFlow = .off
        openStage = nil
        background = UIApplication.shared.applicationState == .background
        let saved = points
        dq.sync {
            est = SpeedEstimator()
            est.load(saved)
        }
        lock.lock()
        snap = nil
        steps = 0
        phoneTimes = []
        airTimes = []
        lock.unlock()

        if phone.isDeviceMotionAvailable {
            phone.deviceMotionUpdateInterval = 1.0 / SpeedEstimator.fs
            phone.startDeviceMotionUpdates(to: queue) { [weak self] m, _ in
                guard let self, let m else { return }
                let g = SpeedEstimator.g, a = m.userAcceleration, gr = m.gravity
                self.addPhone(m.timestamp, (a.x + gr.x) * g, (a.y + gr.y) * g, (a.z + gr.z) * g)
            }
        }
        ScrunHeadphones.shared.setSpeedHandler(queue) { [weak self] m, _ in
            guard let self, let m else { return }
            var t = m.timestamp
            // 에어팟 시각이 폰 시계와 크게 다르면(일부 iOS) 받은 시각을 쓴다(SpeedRecorder와 같음)
            let now = Self.uptime
            if abs(t - now) > 2 { t = now }
            let g = SpeedEstimator.g, a = m.userAcceleration, gr = m.gravity
            self.est.addAirPods(t, (a.x + gr.x) * g, (a.y + gr.y) * g, (a.z + gr.z) * g)
            self.lock.lock()
            Self.push(&self.airTimes, t)
            self.lock.unlock()
        }

        let nc = NotificationCenter.default
        observers = [
            nc.addObserver(forName: UIApplication.willResignActiveNotification, object: nil, queue: .main) { [weak self] _ in
                self?.setBackground(true)
            },
            nc.addObserver(forName: UIApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
                self?.setBackground(false)
            },
        ]
    }

    func stop() {
        guard running else { return }
        running = false
        openStage = nil
        phone.stopDeviceMotionUpdates()
        ScrunHeadphones.shared.setSpeedHandler(nil, nil)
        observers.forEach(NotificationCenter.default.removeObserver)
        observers = []
        dq.sync {} // 이미 큐에 들어간 샘플까지 끝낸 뒤 비운다
        lock.lock()
        snap = nil
        steps = 0
        phoneTimes = []
        airTimes = []
        lock.unlock()
        phoneFlow = .off
        airFlow = .off
    }

    // 화면이 꺼지거나 다른 앱으로 가면 가속도가 멈춘다 → 바로 끊김으로 보이고, 재는 단계는 실패(SensorLost)
    private func setBackground(_ on: Bool) {
        background = on
        if on && openStage != nil {
            stageBroken = true
            print("[ScrunSpeed] 단계 재는 중 앱이 비활성이 됨(제어 센터·알림·잠금·시스템 팝업) → 이 단계는 SensorLost")
        }
    }

    // MARK: 모션 큐

    private func addPhone(_ t: Double, _ x: Double, _ y: Double, _ z: Double) {
        let s = est.addPhone(t, x, y, z)
        lock.lock()
        Self.push(&phoneTimes, t)
        steps = est.steps
        if let s { snap = s }
        lock.unlock()
    }

    private static func push(_ times: inout [Double], _ t: Double) {
        times.append(t)
        if times.count > 400, let k = times.firstIndex(where: { $0 >= t - 2 }), k > 0 { times.removeFirst(k) }
    }

    // MARK: 읽기(메인, 매 프레임)

    func read(_ out: UnsafeMutablePointer<Double>, _ n: Int) -> Int {
        var v = [Double](repeating: 0, count: Self.slotCount)
        v[0] = Self.slotVersion
        v[2] = .nan
        if running {
            let now = Self.uptime
            lock.lock()
            let s = snap, st = steps
            let phoneN = Self.count(phoneTimes, since: now - 1), phoneLast = phoneTimes.last
            let airN = Self.count(airTimes, since: now - 1), airLast = airTimes.last
            lock.unlock()

            phoneFlow = Self.flow(phoneFlow, n: phoneN, okN: Self.phoneOkPerSec, last: phoneLast, now: now, since: t0)
            if background { phoneFlow = .lost }
            if ScrunHeadphones.shared.motionReady || (airLast.map { now - $0 <= Self.lostAfterS } ?? false) {
                if airFlow == .off { airSince = now }
                airFlow = Self.flow(airFlow, n: airN, okN: Self.airOkPerSec,
                                    last: airLast.map { max($0, airSince) }, now: now, since: airSince)
            } else {
                airFlow = .off // 연결·허용이 없으면 실패가 아니라 "이어폰 없이"
            }

            v[1] = now - t0
            if let s, s.state != 0 {
                v[2] = s.kmh ?? .nan
                v[3] = Double(s.state)
                v[4] = s.cad ?? 0
            }
            v[5] = Double(st)
            v[6] = (s?.byAirPods ?? false) ? 1 : 0
            v[7] = phoneFlow.rawValue
            v[8] = airFlow.rawValue
            v[9] = Double(phoneN)
            v[10] = Double(airN)
        }
        let k = max(0, min(n, Self.slotCount))
        for i in 0..<k { out[i] = v[i] }
        return k
    }

    private static func count(_ times: [Double], since t: Double) -> Int {
        var n = 0
        for x in times.reversed() { if x > t { n += 1 } else { break } }
        return n
    }

    // 지난 1초 okN개 이상 → Ok, 2초 넘게 안 옴(처음부터 포함) → Lost, 그 사이는 지난 상태 유지
    private static func flow(_ prev: Flow, n: Int, okN: Int, last: Double?, now: Double, since start: Double) -> Flow {
        if n >= okN { return .ok }
        if now - (last ?? start) > lostAfterS { return .lost }
        return prev == .off ? .waiting : prev
    }

    // MARK: 보정

    func stageBegin(_ kmh: Double) {
        openStage = running ? (kmh, Self.uptime) : nil
        stageBroken = background
    }

    func stageCancel() {
        openStage = nil
    }

    func stageEnd() -> [String: Any] {
        guard running, let b = openStage else { return ["result": "SensorLost", "point": NSNull()] }
        openStage = nil
        if stageBroken { return ["result": "SensorLost", "point": NSNull()] }
        let end = Self.uptime
        let (res, p) = dq.sync { est.measureStage(b.v, begin: b.t, end: end) }
        guard res == .ok, let p else {
            // 왜 실패했는지 콘솔에(아이폰 확인용). SensorLost면 샘플 공백(앞·뒤 끝 포함 2초 넘게 빔)
            print(String(format: "[ScrunSpeed] 단계 %@: %.0f km/h, 잰 시간 %.1f초(앞 %.0f초 버림)",
                         res.rawValue, b.v, end - b.t, SpeedEstimator.calSkipS))
            return ["result": res.rawValue, "point": NSNull()]
        }
        return ["result": res.rawValue, "point": [
            "v": p.v, "limp": p.limp, "cad": p.cad, "amp": p.amp as Any? ?? NSNull(), "running": p.running,
        ] as [String: Any]]
    }

    // C# CalibrationModel JSON. nil·빈 문자열이면 보정 지움. 모양이 틀리면 false(지금 보정 그대로)
    func load(_ json: String?) -> Bool {
        var pts: [SpeedEstimator.Point] = []
        if let json, !json.trimmingCharacters(in: .whitespaces).isEmpty {
            struct Model: Decodable { let version: Int; let points: [SpeedEstimator.Point] }
            guard let d = json.data(using: .utf8), let m = try? JSONDecoder().decode(Model.self, from: d),
                  m.version == 1, !m.points.isEmpty,
                  m.points.allSatisfy({ $0.v > 0 && $0.cad > 0 && $0.limp.isFinite })
            else { return false }
            pts = m.points
        }
        points = pts
        if running { dq.sync { est.load(pts) } }
        return true
    }
}

// ---- C#이 부르는 함수(IosSpeedSensor.cs, [DllImport("__Internal")]). 메인 스레드에서 ----

@_cdecl("scrun_speed_start")
public func scrun_speed_start() { ScrunSpeed.shared.start() }

@_cdecl("scrun_speed_stop")
public func scrun_speed_stop() { ScrunSpeed.shared.stop() }

@_cdecl("scrun_speed_read")
public func scrun_speed_read(_ out: UnsafeMutablePointer<Double>?, _ n: Int32) -> Int32 {
    guard let out else { return 0 }
    return Int32(ScrunSpeed.shared.read(out, Int(n)))
}

@_cdecl("scrun_speed_stage_begin")
public func scrun_speed_stage_begin(_ kmh: Double) { ScrunSpeed.shared.stageBegin(kmh) }

// 받은 쪽이 scrun_free
@_cdecl("scrun_speed_stage_end")
public func scrun_speed_stage_end() -> UnsafeMutablePointer<CChar>? {
    strdup(ScrunBridge.encode(ScrunSpeed.shared.stageEnd()))
}

@_cdecl("scrun_speed_stage_cancel")
public func scrun_speed_stage_cancel() { ScrunSpeed.shared.stageCancel() }

@_cdecl("scrun_speed_load")
public func scrun_speed_load(_ json: UnsafePointer<CChar>?) -> Int32 {
    ScrunSpeed.shared.load(json.map { String(cString: $0) }) ? 1 : 0
}
