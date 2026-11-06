import AVFoundation
import Foundation

/// 테스트 구간 하나 (러닝머신 속도와 시간)
struct Stage: Codable, Identifiable {
    var id = UUID()
    var label: String
    var speed: Double?        // km/h (점프 등은 nil)
    var set: String           // calib(보정) | test(평가) | warmup | sync
    var duration: Double      // 초

    var json: [String: Any] {
        ["label": label, "speed_kmh": speed.map { $0 as Any } ?? NSNull(), "set": set, "duration_s": duration]
    }

    static func plan(calib: [Double], test: [Double], seconds: Double, walk: Double = 5) -> [Stage] {
        var s = [Stage(label: "제자리 점프 3번", speed: nil, set: "sync", duration: 15),
                 Stage(label: "걷기", speed: walk, set: "warmup", duration: 60)]
        s += calib.map { Stage(label: "보정 \(fmt($0))", speed: $0, set: "calib", duration: seconds) }
        s += test.map { Stage(label: "평가 \(fmt($0))", speed: $0, set: "test", duration: seconds) }
        s.append(Stage(label: "멈추고 점프 3번", speed: nil, set: "sync", duration: 15))
        return s
    }

    /// 러닝머신 검증 + 에어팟 (계획 0008 러닝머신 단계). 점프·걷기 없이 SpeedLab 검증과 같은 순서.
    /// 구간 시간에 속도 맞추는 시간이 포함됨 (분석에서 앞부분을 버림). 앞 4개는 노트북 실시간 분석용 보정
    static func treadmillPlan(seconds: Double = 30) -> [Stage] {
        let speeds: [Double] = [7, 8, 9, 10, 9, 8, 7, 10, 9, 8, 7, 8, 9, 10]
        return speeds.enumerated().map { (i, v) in
            Stage(label: "검증 \(fmt(v))", speed: v, set: i < 4 ? "calib" : "test", duration: seconds)
        }
    }

    /// 보정 1분 + 자유 달리기 (계획 0010). 올라가는 순서로 보정(내린 직후엔 충격이 20초 넘게 남아서),
    /// 첫 구간은 러닝머신을 켜고 올라타는 시간까지 25초, 나머지 15초(앞 5초는 추정기가 버림). 자유 달리기 중 실제 속도는 ±버튼
    static func freePlan(minutes: Double = 20) -> [Stage] {
        [Stage(label: "보정 7", speed: 7, set: "calib", duration: 25),
         Stage(label: "보정 8", speed: 8, set: "calib", duration: 15),
         Stage(label: "보정 9", speed: 9, set: "calib", duration: 15),
         Stage(label: "보정 10", speed: 10, set: "calib", duration: 15),
         Stage(label: "자유 달리기", speed: nil, set: "free", duration: minutes * 60)]
    }

    /// 저장된 보정으로 보정 없이 바로 자유 달리기 (다른 날 오차 보기)
    static func freeOnlyPlan(minutes: Double = 20) -> [Stage] {
        [Stage(label: "자유 달리기", speed: nil, set: "free", duration: minutes * 60)]
    }

    /// 제자리 뛰기로 공중 시간 확인 (계획 0008). 메트로놈으로 걸음 수를 고정하고 뛰는 높이만 바꾼다
    static func hopPlan(seconds: Double = 30) -> [Stage] {
        [Stage(label: "가만히 서기", speed: nil, set: "still", duration: 10),
         Stage(label: "제자리 점프 3번", speed: nil, set: "sync", duration: 10),
         Stage(label: "낮게 뛰기", speed: nil, set: "hop", duration: seconds),
         Stage(label: "보통으로 뛰기", speed: nil, set: "hop", duration: seconds),
         Stage(label: "높게 뛰기", speed: nil, set: "hop", duration: seconds),
         Stage(label: "멈추고 점프 3번", speed: nil, set: "sync", duration: 10)]
    }
}

func fmt(_ v: Double) -> String {
    v.rounded() == v ? String(Int(v)) : String(format: "%.1f", v)
}

func parseSpeeds(_ text: String) -> [Double] {
    text.split(whereSeparator: { ", ".contains($0) }).compactMap { Double($0) }
}

/// 구간을 차례로 진행하며 음성으로 안내하고, 구간이 바뀔 때마다 노트북에 알린다.
@MainActor
final class TestRunner: ObservableObject {
    @Published var stages: [Stage] = []
    @Published var index = -1
    @Published var remaining: Double = 0
    @Published var running = false
    @Published var finished = false
    @Published var runId: String?
    @Published var status = ""
    @Published var online = false       // 노트북에도 보내는 중인지 (아니면 폰에만 저장)
    @Published var trueKmh: Double = 8  // 자유 달리기 중 ±버튼으로 맞추는 실제 속도

    private var stageStart = Date()
    private var timer: Timer?
    private var warned = false
    private let speech = AVSpeechSynthesizer()
    private var server: ServerClient?
    private var recorder: Recorder?
    private var marks = 0

    var current: Stage? { stages.indices.contains(index) ? stages[index] : nil }
    var next: Stage? { stages.indices.contains(index + 1) ? stages[index + 1] : nil }

    /// 노트북에 연결되면 거기에도 기록을 만들고, 안 되면 폰에만 저장한다
    func start(stages: [Stage], heightCm: Double, server: ServerClient?, recorder: Recorder,
               calibration: SavedCalibration? = nil) async {
        self.stages = stages
        self.recorder = recorder
        var id: String?
        if let server {
            status = "노트북 연결 확인 중…"
            if await server.syncClock(),
               let res = try? await server.post("api/speed/runs", ["height_cm": heightCm, "stages": stages.map(\.json),
                                                                  "device": "iPhone + AirPods"]) {
                id = res["id"] as? String
            }
        }
        online = id != nil
        self.server = online ? server : nil
        runId = id ?? TestRunner.localId()
        try? AVAudioSession.sharedInstance().setCategory(.playback, options: [.duckOthers])
        try? AVAudioSession.sharedInstance().setActive(true)
        recorder.begin(runId: runId!, server: self.server, calibration: calibration)
        running = true
        finished = false
        marks = 0
        status = online ? "" : "노트북 없이 기록 중 (폰에만 저장)"
        go(to: 0)
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
    }

    static func localId() -> String {
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd-HHmmss"
        return f.string(from: Date())
    }

    private func tick() {
        guard running, let cur = current else { return }
        remaining = max(0, cur.duration - Date().timeIntervalSince(stageStart))
        if remaining <= 5, !warned, let nx = next {
            warned = true
            say(nx.speed.map { "5초 뒤 \(fmt($0)) 킬로" } ?? "5초 뒤 \(nx.label)")
        }
        if remaining <= 0 { go(to: index + 1) }
    }

    func skip() { go(to: index + 1) }

    private func go(to i: Int) {
        guard let runId else { return }
        if i >= stages.count {
            Task { await finish() }
            return
        }
        let prevSpeed = current?.speed
        index = i
        stageStart = Date()
        warned = false
        let st = stages[i]
        remaining = st.duration
        recorder?.stageChanged(index: i, set: st.set, speed: st.speed)
        if let server {
            let t = server.serverNow()
            Task { try? await server.post("api/speed/runs/\(runId)/stage",
                                          ["index": i, "label": st.label, "speed_kmh": st.speed.map { $0 as Any } ?? NSNull(), "set": st.set, "t": t]) }
        }
        if let v = st.speed {
            say(st.set == "warmup" ? "\(fmt(v)) 킬로로 걸으세요" : "지금 \(fmt(v)) 킬로로 바꾸세요")
        } else {
            say(st.label)
        }
        if st.set == "free" { mark(prevSpeed ?? trueKmh) }
    }

    /// 자유 달리기: 러닝머신을 바꾼 뒤 실제 속도를 남긴다 (나중에 오차 계산용)
    func mark(_ kmh: Double) {
        trueKmh = min(max(kmh, 0), 25)
        recorder?.markTrue(trueKmh)
        marks += 1
        if let server, let runId {
            let t = server.serverNow(), i = stages.count + marks
            Task { try? await server.post("api/speed/runs/\(runId)/stage",
                                          ["index": i, "label": "자유 \(fmt(trueKmh))", "speed_kmh": trueKmh, "set": "free", "t": t]) }
        }
    }

    func finish() async {
        guard running, let runId else { return }
        running = false
        timer?.invalidate()
        recorder?.stageChanged(index: -1, set: "end", speed: nil)
        let t = server?.serverNow() ?? ServerClient.localNow()
        await recorder?.end()
        if let server { try? await server.post("api/speed/runs/\(runId)/finish", ["t": t]) }
        finished = true
        if !online { status = "폰에 저장됨: \(runId) (파일 앱 → 나의 iPhone → SpeedRecorder)" }
        say("기록이 끝났어요. 수고했어요")
    }

    private func say(_ text: String) {
        let u = AVSpeechUtterance(string: text)
        u.voice = AVSpeechSynthesisVoice(language: "ko-KR")
        u.rate = 0.5
        u.volume = 1
        speech.speak(u)
    }
}
