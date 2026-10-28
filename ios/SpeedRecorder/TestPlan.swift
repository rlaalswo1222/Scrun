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

    private var stageStart = Date()
    private var timer: Timer?
    private var warned = false
    private let speech = AVSpeechSynthesizer()
    private var server: ServerClient?
    private var recorder: Recorder?

    var current: Stage? { stages.indices.contains(index) ? stages[index] : nil }
    var next: Stage? { stages.indices.contains(index + 1) ? stages[index + 1] : nil }

    func start(stages: [Stage], heightCm: Double, server: ServerClient, recorder: Recorder) async {
        self.stages = stages
        self.server = server
        self.recorder = recorder
        status = "노트북 연결 중…"
        guard await server.syncClock() else {
            status = "노트북에 연결하지 못했어요. 주소와 핫스팟을 확인하세요."
            return
        }
        do {
            let res = try await server.post("api/speed/runs", ["height_cm": heightCm, "stages": stages.map(\.json),
                                                             "device": "iPhone + AirPods"])
            guard let id = res["id"] as? String else { throw URLError(.cannotParseResponse) }
            runId = id
        } catch {
            status = "기록을 만들지 못했어요: \(error.localizedDescription)"
            return
        }
        try? AVAudioSession.sharedInstance().setCategory(.playback, options: [.duckOthers])
        try? AVAudioSession.sharedInstance().setActive(true)
        recorder.begin(runId: runId!, server: server)
        running = true
        finished = false
        status = ""
        go(to: 0)
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
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
        guard let server, let runId else { return }
        if i >= stages.count {
            Task { await finish() }
            return
        }
        index = i
        stageStart = Date()
        warned = false
        let st = stages[i]
        remaining = st.duration
        let t = server.serverNow()
        Task { try? await server.post("api/speed/runs/\(runId)/stage",
                                      ["index": i, "label": st.label, "speed_kmh": st.speed.map { $0 as Any } ?? NSNull(), "set": st.set, "t": t]) }
        if let v = st.speed {
            say(st.set == "warmup" ? "\(fmt(v)) 킬로로 걸으세요" : "지금 \(fmt(v)) 킬로로 바꾸세요")
        } else {
            say(st.label)
        }
    }

    func finish() async {
        guard running, let server, let runId else { return }
        running = false
        timer?.invalidate()
        let t = server.serverNow()
        await recorder?.end()
        try? await server.post("api/speed/runs/\(runId)/finish", ["t": t])
        finished = true
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
