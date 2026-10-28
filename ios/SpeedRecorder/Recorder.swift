import CoreMotion
import Foundation

/// 아이폰 가속도·자이로(100Hz)와 에어팟 머리 움직임(약 25Hz)을 모아 두었다가 0.5초마다 노트북으로 보낸다.
/// 못 보낸 샘플은 다음에 다시 보내고, 모든 샘플은 폰의 문서 폴더에도 CSV로 남긴다 (연결이 끊겨도 잃지 않게).
final class Recorder: NSObject, ObservableObject, CMHeadphoneMotionManagerDelegate {
    static let columns: [String: [String]] = [
        "iphone_acc": ["x", "y", "z"], "iphone_gyro": ["x", "y", "z"],
        "airpods_acc": ["x", "y", "z"], "airpods_gyro": ["x", "y", "z"], "airpods_grav": ["x", "y", "z"],
    ]

    @Published var headConnected = false
    @Published var headAvailable = false
    @Published var rates: [String: Double] = [:]   // 초당 샘플 수
    @Published var sendError: String?
    @Published var sentCount = 0

    private let phone = CMMotionManager()
    private let head = CMHeadphoneMotionManager()
    private let queue: OperationQueue = {
        let q = OperationQueue()
        q.maxConcurrentOperationCount = 1
        q.qualityOfService = .userInitiated
        return q
    }()
    private let lock = NSLock()
    private var pending: [String: [[Double]]] = [:]
    private var counts: [String: Int] = [:]
    private var bootEpoch = 0.0
    private var server: ServerClient?
    private var runId: String?
    private var files: [String: FileHandle] = [:]
    private var sendTask: Task<Void, Never>?
    private var rateTimer: Timer?
    private var lastCounts: [String: Int] = [:]

    override init() {
        super.init()
        head.delegate = self
        headAvailable = head.isDeviceMotionAvailable
    }

    // MARK: 에어팟 연결 상태
    func headphoneMotionManagerDidConnect(_ manager: CMHeadphoneMotionManager) {
        DispatchQueue.main.async { self.headConnected = true }
    }

    func headphoneMotionManagerDidDisconnect(_ manager: CMHeadphoneMotionManager) {
        DispatchQueue.main.async { self.headConnected = false }
    }

    /// 기록 전에도 에어팟·센서 상태를 볼 수 있게 센서만 켠다 (보내지는 않음)
    func startSensors() {
        guard !phone.isDeviceMotionActive else { return }
        bootEpoch = Date().timeIntervalSince1970 - ProcessInfo.processInfo.systemUptime
        let g = 9.80665
        phone.deviceMotionUpdateInterval = 1.0 / 100
        phone.startDeviceMotionUpdates(to: queue) { [weak self] m, _ in
            guard let self, let m else { return }
            let t = self.stamp(m.timestamp)
            let a = m.userAcceleration, gr = m.gravity, r = m.rotationRate
            self.append("iphone_acc", [t, (a.x + gr.x) * g, (a.y + gr.y) * g, (a.z + gr.z) * g])
            self.append("iphone_gyro", [t, r.x, r.y, r.z])
        }
        if head.isDeviceMotionAvailable {
            head.startDeviceMotionUpdates(to: queue) { [weak self] m, _ in
                guard let self, let m else { return }
                var t = self.stamp(m.timestamp)
                // 에어팟 시각이 폰 시계와 크게 다르면(일부 iOS) 받은 시각을 쓴다
                let now = ServerClient.localNow() + (self.server?.offset ?? 0)
                if abs(t - now) > 2 { t = now }
                let a = m.userAcceleration, gr = m.gravity, r = m.rotationRate
                self.append("airpods_acc", [t, a.x * g, a.y * g, a.z * g])
                self.append("airpods_gyro", [t, r.x, r.y, r.z])
                // 중력 방향: airpods_acc + airpods_grav = 몸 전체 가속도 (공중에선 약 0g)
                self.append("airpods_grav", [t, gr.x * g, gr.y * g, gr.z * g])
                if !self.headConnected { DispatchQueue.main.async { self.headConnected = true } }
            }
        }
        rateTimer?.invalidate()
        rateTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.updateRates() }
    }

    private func stamp(_ uptime: TimeInterval) -> Double {
        bootEpoch + uptime + (server?.offset ?? 0)
    }

    private func append(_ source: String, _ row: [Double]) {
        lock.lock()
        counts[source, default: 0] += 1
        if runId != nil { pending[source, default: []].append(row) }
        lock.unlock()
    }

    private func updateRates() {
        lock.lock()
        let now = counts
        lock.unlock()
        var r: [String: Double] = [:]
        for (k, v) in now { r[k] = Double(v - (lastCounts[k] ?? 0)) }
        lastCounts = now
        rates = r
    }

    // MARK: 기록
    func begin(runId: String, server: ServerClient) {
        self.server = server
        lock.lock()
        self.runId = runId
        pending = [:]
        lock.unlock()
        openFiles(runId)
        sendTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 500_000_000)
                await self?.flush()
            }
        }
    }

    func end() async {
        sendTask?.cancel()
        await flush()
        lock.lock()
        runId = nil
        lock.unlock()
        for (_, f) in files { try? f.close() }
        files = [:]
    }

    func stopSensors() {
        phone.stopDeviceMotionUpdates()
        head.stopDeviceMotionUpdates()
        rateTimer?.invalidate()
    }

    private func flush() async {
        guard let server, let runId else { return }
        lock.lock()
        let batch = pending
        pending = [:]
        lock.unlock()
        let nonEmpty = batch.filter { !$0.value.isEmpty }
        if nonEmpty.isEmpty { return }
        save(nonEmpty)
        let body: [[String: Any]] = nonEmpty.map { ["source": $0.key, "columns": Recorder.columns[$0.key] ?? [], "rows": $0.value] }
        do {
            try await server.post("api/speed/runs/\(runId)/samples", body)
            let n = nonEmpty.values.reduce(0) { $0 + $1.count }
            await MainActor.run { self.sentCount += n; self.sendError = nil }
        } catch {
            // 못 보낸 샘플은 앞에 다시 넣는다. 너무 쌓이면(약 2분) 오래된 것부터 버린다 (폰 CSV에는 남아 있음)
            lock.lock()
            for (k, rows) in nonEmpty {
                var merged = rows + (pending[k] ?? [])
                if merged.count > 12000 { merged.removeFirst(merged.count - 12000) }
                pending[k] = merged
            }
            lock.unlock()
            await MainActor.run { self.sendError = "노트북으로 못 보내는 중 (다시 시도)" }
        }
    }

    // MARK: 폰에 백업
    private func openFiles(_ runId: String) {
        let dir = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent(runId)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        for (src, cols) in Recorder.columns {
            let url = dir.appendingPathComponent("\(src).csv")
            if !FileManager.default.fileExists(atPath: url.path) {
                FileManager.default.createFile(atPath: url.path, contents: (["t"] + cols).joined(separator: ",").appending("\n").data(using: .utf8))
            }
            files[src] = try? FileHandle(forWritingTo: url)
            files[src]?.seekToEndOfFile()
        }
    }

    private func save(_ batch: [String: [[Double]]]) {
        for (src, rows) in batch {
            guard let f = files[src] else { continue }
            let text = rows.map { r in r.map { String(format: "%.5f", $0) }.joined(separator: ",") }.joined(separator: "\n") + "\n"
            if let d = text.data(using: .utf8) { f.write(d) }
        }
    }
}
