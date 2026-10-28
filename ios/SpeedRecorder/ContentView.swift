import SwiftUI
import WebKit

struct ContentView: View {
    @AppStorage("server") private var serverText = "http://192.168.0.6:8000"
    @AppStorage("height") private var height = 175.0
    @AppStorage("calib") private var calibText = "8, 9, 10, 11, 12"
    @AppStorage("test") private var testText = "9.5, 11.5, 7"
    @AppStorage("seconds") private var seconds = 90.0
    @StateObject private var recorder = Recorder()
    @StateObject private var runner = TestRunner()
    @State private var check = ""
    @State private var server: ServerClient?
    @State private var savedCal = SavedCalibration.load()

    var body: some View {
        Group {
            if runner.running {
                runningView
            } else if runner.finished, runner.online, let id = runner.runId, let server {
                VStack(spacing: 0) {
                    HStack {
                        Text("기록 끝").font(.title2.bold())
                        Spacer()
                        Button("새 기록") { runner.finished = false; runner.runId = nil }
                    }
                    .padding()
                    WebView(url: server.baseURL.appendingPathComponent("speed.html").appending(queryItems: [.init(name: "run", value: id)]))
                }
            } else {
                setupView
            }
        }
        .onAppear {
            UIApplication.shared.isIdleTimerDisabled = true
            recorder.startSensors()
        }
        .onChange(of: runner.finished) { _ in savedCal = SavedCalibration.load() }
    }

    private var plan: [Stage] {
        Stage.plan(calib: parseSpeeds(calibText), test: parseSpeeds(testText), seconds: seconds)
    }

    // MARK: 준비 화면
    private var setupView: some View {
        Form {
            Section("노트북") {
                TextField("주소", text: $serverText)
                    .keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
                Button("연결 확인") { Task { await checkServer() } }
                if !check.isEmpty { Text(check).font(.footnote).foregroundStyle(.secondary) }
            }
            Section("센서") {
                row("에어팟", recorder.headConnected ? "연결됨 · \(rate("airpods_acc"))" :
                        (recorder.headAvailable ? "귀에 끼면 연결돼요" : "이 아이폰에서 지원 안 함"),
                    ok: recorder.headConnected)
                row("아이폰", rate("iphone_acc"), ok: (recorder.rates["iphone_acc"] ?? 0) > 50)
            }
            Section("테스트") {
                HStack { Text("키 (cm)"); Spacer(); TextField("175", value: $height, format: .number).keyboardType(.decimalPad).multilineTextAlignment(.trailing) }
                HStack { Text("보정 속도"); Spacer(); TextField("8, 9, 10", text: $calibText).multilineTextAlignment(.trailing) }
                HStack { Text("평가 속도"); Spacer(); TextField("9.5, 11.5", text: $testText).multilineTextAlignment(.trailing) }
                Stepper("구간마다 \(Int(seconds))초", value: $seconds, in: 30...240, step: 15)
                let total = plan.reduce(0) { $0 + $1.duration }
                Text("구간 \(plan.count)개 · 약 \(Int(total / 60))분").font(.footnote).foregroundStyle(.secondary)
            }
            Section {
                Button {
                    Task { await start() }
                } label: {
                    Text("기록 시작").font(.title3.bold()).frame(maxWidth: .infinity)
                }
                .disabled(parseSpeeds(calibText).count < 3)
                if !runner.status.isEmpty { Text(runner.status).foregroundStyle(.red) }
            } footer: {
                Text("폰은 러닝머신 거치대에 세우고, 에어팟을 양쪽 다 끼세요. 안내 음성이 에어팟으로 나와요. 기록 중에도 폰에 CSV가 저장돼요.")
            }
            Section {
                Button("저장된 보정으로 바로 자유 달리기") { Task { await start(Stage.freeOnlyPlan(), calibration: savedCal) } }
                    .disabled(savedCal == nil)
            } footer: {
                Text(savedCal.map { "\($0.label). 보정 없이 시작해요 — 다른 날 오차 확인용. 시작하자마자 ±로 실제 속도를 맞춰 주세요." }
                     ?? "저장된 보정이 없어요. 아래 \"보정 1분 + 자유 달리기\"를 한 번 하면 저장돼요.")
            }
            Section {
                Button("보정 1분 + 자유 달리기 (폰 안에서 실시간 속도)") { Task { await start(Stage.freePlan()) } }
            } footer: {
                Text("폰은 계기판 위에 평평하게, 에어팟 양쪽. 7·8·9·10 km/h 보정(올라가는 순서, 첫 구간 25초, 나머지 15초) 뒤 원하는 속도로 달려요. 러닝머신을 바꿀 때마다 화면의 ±로 실제 속도를 맞춰 주세요. 노트북이 없어도 돼요(폰에 저장).")
            }
            Section {
                Button("러닝머신 검증 + 에어팟 (14구간, 약 7분)") { Task { await start(Stage.treadmillPlan()) } }
            } footer: {
                Text("폰은 계기판 위에 평평하게, 에어팟 양쪽. 점프·걷기 없이 7에서 10 km/h를 30초씩 안내해요(속도 맞추는 시간 포함).")
            }
            Section {
                Button("제자리 뛰기 기록 (약 2분)") { Task { await start(Stage.hopPlan()) } }
            } footer: {
                Text("공중 시간 확인용. 폰은 두 손으로 가슴에 붙이고 팔은 흔들지 마세요. 다른 기기 메트로놈(170)에 맞춰 낮게·보통·높게 뛰어요.")
            }
        }
    }

    // MARK: 기록 중 화면: 위는 진행 상황, 아래는 노트북 분석 화면(실시간)
    private var runningView: some View {
        VStack(spacing: 8) {
            estimateView
            if let cur = runner.current {
                Text(cur.speed.map { "안내 \(fmt($0)) km/h" } ?? cur.label)
                    .font(.system(size: 30, weight: .bold, design: .rounded))
                    .minimumScaleFactor(0.5)
                Text("\(cur.label) · \(Int(runner.remaining.rounded(.up)))초 남음")
                    .font(.title3.monospacedDigit())
                if let nx = runner.next {
                    Text("다음: \(nx.speed.map { "\(fmt($0)) km/h" } ?? nx.label)").foregroundStyle(.secondary)
                }
            }
            HStack(spacing: 14) {
                Label(recorder.headConnected ? rate("airpods_acc") : "에어팟 끊김", systemImage: "airpods")
                    .foregroundStyle(recorder.headConnected ? Color.green : Color.red)
                Label(rate("iphone_acc"), systemImage: "iphone")
                if let e = recorder.sendError { Text(e).foregroundStyle(.orange) }
            }
            .font(.footnote.monospacedDigit())
            if runner.current?.set == "free" {
                HStack(spacing: 10) {
                    Button("−1") { runner.mark(runner.trueKmh - 1) }
                    Button("−0.5") { runner.mark(runner.trueKmh - 0.5) }
                    Text("실제 \(fmt(runner.trueKmh))").font(.title2.bold().monospacedDigit()).frame(minWidth: 110)
                    Button("+0.5") { runner.mark(runner.trueKmh + 0.5) }
                    Button("+1") { runner.mark(runner.trueKmh + 1) }
                }
                .buttonStyle(.borderedProminent)
            }
            HStack {
                Button("다음 구간") { runner.skip() }.buttonStyle(.bordered)
                Spacer()
                Button("기록 끝내기", role: .destructive) { Task { await runner.finish() } }.buttonStyle(.bordered)
            }
            .padding(.horizontal)
            if runner.online, let id = runner.runId, let server {
                WebView(url: server.baseURL.appendingPathComponent("speed.html")
                    .appending(queryItems: [.init(name: "run", value: id), .init(name: "compact", value: "1")]))
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                    .padding(.horizontal, 6)
            }
        }
        .padding(.top, 10)
    }

    /// 폰 안에서 계산한 실시간 속도 (SpeedEstimator)
    private var estimateView: some View {
        VStack(spacing: 2) {
            if let e = recorder.estimate {
                Text(e.kmh.map { String(format: "%.1f", $0) } ?? "--")
                    .font(.system(size: 80, weight: .heavy, design: .rounded).monospacedDigit())
                    .foregroundStyle(e.state == 2 ? Color.orange : Color.primary)
                let state = ["걸음 없음", "걷기", "달리기"][e.state]
                Text("km/h 추정 · \(state) (\(e.byAirPods ? "에어팟" : "폰") 판단)").font(.headline)
                Text("폰만 \(e.kmhPhone.map { String(format: "%.1f", $0) } ?? "--") · 에어팟 \(e.airOK ? "사용" : "끊김") · 보정점 \(e.nRunPoints)/\(e.nPoints)")
                    .font(.footnote.monospacedDigit()).foregroundStyle(.secondary)
            } else {
                Text("추정 준비 중").font(.headline).foregroundStyle(.secondary)
            }
        }
    }

    private func row(_ name: String, _ value: String, ok: Bool) -> some View {
        HStack { Text(name); Spacer(); Text(value).foregroundStyle(ok ? Color.green : Color.secondary) }
    }

    private func rate(_ src: String) -> String {
        "\(Int(recorder.rates[src] ?? 0)) Hz"
    }

    private func makeServer() -> ServerClient? {
        var text = serverText.trimmingCharacters(in: .whitespaces)
        if !text.hasPrefix("http") { text = "http://" + text }
        guard let url = URL(string: text) else { return nil }
        if let server, server.baseURL == url { return server }
        let s = ServerClient(baseURL: url)
        server = s
        return s
    }

    private func checkServer() async {
        guard let s = makeServer() else { check = "주소가 이상해요"; return }
        check = "확인 중…"
        check = await s.syncClock() ? "연결됨 · 왕복 \(s.roundTripMs)ms" : "연결 안 됨 · 노트북 서버와 같은 와이파이(핫스팟)인지 확인"
    }

    /// 노트북 주소가 맞고 연결되면 노트북에도 보내고, 아니면 폰에만 저장
    private func start(_ stages: [Stage]? = nil, calibration: SavedCalibration? = nil) async {
        await runner.start(stages: stages ?? plan, heightCm: height, server: makeServer(), recorder: recorder,
                           calibration: calibration)
    }
}

/// 노트북 분석 화면(speed.html)을 앱 안에 띄운다
struct WebView: UIViewRepresentable {
    let url: URL

    func makeUIView(context: Context) -> WKWebView {
        let v = WKWebView()
        v.isOpaque = false
        v.backgroundColor = .black
        v.load(URLRequest(url: url))
        return v
    }

    func updateUIView(_ v: WKWebView, context: Context) {
        if v.url?.absoluteString != url.absoluteString { v.load(URLRequest(url: url)) }
    }
}
