import CoreMotion
import Foundation

/// 아이폰 가속도·자이로(100Hz)와 에어팟 머리 움직임(약 50Hz)을 모아 폰의 문서 폴더에 CSV로 남기고,
/// 노트북이 연결돼 있으면 0.5초마다 보낸다(못 보낸 샘플은 다음에 다시). 실시간 속도는 폰 안에서 계산(SpeedEstimator).
final class Recorder: NSObject, ObservableObject, CMHeadphoneMotionManagerDelegate {
    static let columns: [String: [String]] = [
        "iphone_acc": ["x", "y", "z"], "iphone_gyro": ["x", "y", "z"],
        "airpods_acc": ["x", "y", "z"], "airpods_gyro": ["x", "y", "z"], "airpods_grav": ["x", "y", "z"],
        // 폰 안 실시간 추정(0.5초마다, 없으면 -1), 구간 바뀜, 실제 속도 표시(±버튼)
        "estimate": ["kmh", "kmh_phone", "state", "by_airpods", "air_ok", "cad", "imp", "amp", "lowg_ms", "n_run_points"],
        "stage": ["index", "speed", "set"],
        "mark": ["true_kmh"],
    ]
    /// stage.csv의 set 열
    static let setCode: [String: Double] = ["sync": 0, "calib": 1, "test": 2, "free": 3, "hop": 4, "warmup": 5, "still": 6, "end": 9]

    @Published var estimate: SpeedEstimator.Snapshot?
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
    private var pending: [String: [[Double]]] = [:]   // 아직 파일에 안 쓴 새 샘플
    private var unsent: [String: [[Double]]] = [:]    // 파일엔 썼지만 노트북에 못 보낸 샘플
    private var estimator = SpeedEstimator()          // 모션 큐에서만 씀
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
            let x = (a.x + gr.x) * g, y = (a.y + gr.y) * g, z = (a.z + gr.z) * g
            self.append("iphone_acc", [t, x, y, z])
            self.append("iphone_gyro", [t, r.x, r.y, r.z])
            if let s = self.estimator.addPhone(t, x, y, z) {
                self.append("estimate", [t, s.kmh ?? -1, s.kmhPhone ?? -1, Double(s.state), s.byAirPods ? 1 : 0, s.airOK ? 1 : 0,
                                         s.cad ?? -1, s.imp ?? -1, s.amp ?? -1, s.lowgMs ?? -1, Double(s.nRunPoints)])
                DispatchQueue.main.async { self.estimate = s }
            }
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
                self.estimator.addAirPods(t, (a.x + gr.x) * g, (a.y + gr.y) * g, (a.z + gr.z) * g)
                if !self.headConnected { DispatchQueue.main.async { self.headConnected = true } }
            }
        }
        rateTimer?.invalidate()
        rateTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.updateRates() }
    }

    private func stamp(_ uptime: TimeInterval) -> Double {
        bootEpoch + uptime + (server?.offset ?? 0)
    }

    /// 샘플과 같은 시계(노트북 연결 시 노트북 시계)
    func now() -> Double { ServerClient.localNow() + (server?.offset ?? 0) }

    /// 구간이 바뀜: 추정기에 알리고(끝난 보정 구간 → 보정점) stage.csv에 남김
    func stageChanged(index: Int, set: String, speed: Double?) {
        let t = now()
        queue.addOperation { [weak self] in self?.estimator.beginStage(set, speed: speed, t: t) }
        append("stage", [t, Double(index), speed ?? -1, Recorder.setCode[set] ?? -1])
    }

    /// 자유 달리기 중 실제 속도(러닝머신 표시)를 ±버튼으로 남김
    func markTrue(_ kmh: Double) {
        append("mark", [now(), kmh])
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
    /// server가 nil이면 폰에만 저장
    func begin(runId: String, server: ServerClient?) {
        self.server = server
        lock.lock()
        self.runId = runId
        pending = [:]
        unsent = [:]
        lock.unlock()
        queue.addOperation { [weak self] in self?.estimator = SpeedEstimator() }
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
        lock.lock()
        let fresh = pending.filter { !$0.value.isEmpty }
        pending = [:]
        let rid = runId
        lock.unlock()
        guard rid != nil else { return }
        // 새 샘플은 파일에 한 번만 쓴다 (예전엔 못 보낸 샘플을 다시 보낼 때마다 또 써서 같은 줄이 여러 번 들어갔음)
        if !fresh.isEmpty { save(fresh) }
        guard let server, let runId = rid else { return }
        lock.lock()
        var batch = unsent
        unsent = [:]
        lock.unlock()
        for (k, rows) in fresh { batch[k, default: []] += rows }
        let nonEmpty = batch.filter { !$0.value.isEmpty }
        if nonEmpty.isEmpty { return }
        let body: [[String: Any]] = nonEmpty.map { ["source": $0.key, "columns": Recorder.columns[$0.key] ?? [], "rows": $0.value] }
        do {
            try await server.post("api/speed/runs/\(runId)/samples", body)
            let n = nonEmpty.values.reduce(0) { $0 + $1.count }
            await MainActor.run { self.sentCount += n; self.sendError = nil }
        } catch {
            // 못 보낸 샘플은 다음에 다시 보낸다. 너무 쌓이면(약 2분) 오래된 것부터 버린다 (폰 CSV에는 남아 있음)
            lock.lock()
            for (k, rows) in nonEmpty {
                var merged = rows + (unsent[k] ?? [])
                if merged.count > 12000 { merged.removeFirst(merged.count - 12000) }
                unsent[k] = merged
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
