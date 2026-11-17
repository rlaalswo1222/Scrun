import AVFoundation
import UIKit

// 계기판 촬영 화면(Figma E-03, 계획 0033, ADR-0011). C# ICamera(IosNative.cs)와 짝.
// Unity 위에 이 화면을 통째로 띄운다: 미리보기 + 겹친 UI(X·남은 시간·점선 가이드·안내·셔터) + 노출 제어.
// 셔터 한 번에 노출만 다른 사진을 연달아 찍고(브래킷, 기본 0·−1.5·−3 EV) 앱 임시 폴더에 JPEG로 쓴 뒤
// 화면을 닫고 경로만 C#에 돌려준다(사진 바이트는 통로에 안 태움). 서버 OCR이 가장 잘 읽힌 장을 고른다.
//   답: {"outcome": "taken"|"cancelled"|"timedOut"|"noPermission"|"unavailable", "photos": [{"path", "bias"}]}
// 노출 규칙(가설, 첫 실기기 시험에서 값 정함, console_ocr.md):
//   - 초점·노출 기준점 = 가이드 프레임 가운데(계기판)
//   - 자동 노출이 셔터를 1/60초보다 길게 못 쓰게(LED 깜빡임·손떨림)
//   - 브래킷의 어두운 장: 7세그먼트 LED가 하얗게 타지 않게. 자동 장: LCD·터치스크린용
enum ScrunCamera {
    typealias Done = ([String: Any]) -> Void

    static let folder = FileManager.default.temporaryDirectory.appendingPathComponent("scrun-camera", isDirectory: true)
    private static var current: ScrunCameraController?
    private static var starting = false // 권한을 묻는 중(화면을 띄우기 전). 이 사이에 또 불려도 오류로(리뷰 3)

    struct Options {
        var timeLimitSec = 45
        var guideLabel = "계기판이 이 안에 들어오게"
        var hint = "숫자가 선명하게 보이도록 맞춰 주세요"
        var biases: [Float] = [0, -1.5, -3]

        init(_ a: [String: Any]) {
            if let v = a["timeLimitSec"] as? Int { timeLimitSec = max(0, v) }
            if let v = a["guideLabel"] as? String { guideLabel = v }
            if let v = a["hint"] as? String { hint = v }
            if let v = a["biases"] as? [NSNumber], !v.isEmpty { biases = v.map { $0.floatValue } }
        }
    }

    // 메인 스레드에서 부른다(ScrunBridge.scrun_call)
    static func shoot(_ a: [String: Any], _ done: @escaping Done) throws {
        // ICamera 약속: 이미 떠 있거나 띄우는 중이면 오류(NativeException)
        guard current == nil, !starting else { throw ScrunError("카메라 화면이 이미 떠 있습니다.") }
        starting = true
        let opts = Options(a)
        cleanOld()
        ScrunPermissions.shared.request(.camera) { status in
            ScrunBridge.onMain {
                starting = false
                guard status == .granted else { done(["outcome": "noPermission"]); return }
                guard AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back) != nil,
                      topViewController() != nil else {
                    done(["outcome": "unavailable"]) // 시뮬레이터에는 카메라가 없음
                    return
                }
                let vc = ScrunCameraController(opts) { result in
                    current = nil
                    done(result)
                }
                current = vc
                presentWhenReady(vc)
            }
        }
    }

    // 다른 화면이 뜨거나 닫히는 중이면(권한 팝업 직후 등) 끝날 때까지 기다렸다 띄운다(0.1초씩 최대 3초).
    // present는 실패해도 경고만 찍고 완료 콜백을 안 부름 → 바로 확인해서 안 떴으면 끝냄
    // (안 그러면 C#이 영원히 기다리고 다음 촬영은 "이미 떠 있습니다", 리뷰 2)
    private static func presentWhenReady(_ vc: ScrunCameraController, triesLeft: Int = 30) {
        guard let top = topViewController() else { vc.presentFailed(); return }
        let busy = top.transitionCoordinator != nil || top.presentedViewController != nil // 위에 남은 건 사라지는 중인 창
        if busy && triesLeft > 0 {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { presentWhenReady(vc, triesLeft: triesLeft - 1) }
            return
        }
        top.present(vc, animated: true) { vc.didPresent() }
        if top.presentedViewController !== vc {
            NSLog("[ScrunCamera] 촬영 화면을 띄우지 못함(다른 화면 전환 중)")
            vc.presentFailed()
        }
    }

    // 사진을 임시 폴더에 쓴다. 한 장에 2–3 MB라 메인 스레드 밖에서 부른다(리뷰 7). 쓴 것만 돌려줌
    static func write(_ photos: [(bias: Float, data: Data)]) -> [[String: Any]] {
        let fm = FileManager.default
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        let stamp = Int(Date().timeIntervalSince1970 * 1000)
        var out: [[String: Any]] = []
        for (i, p) in photos.enumerated() {
            let url = folder.appendingPathComponent("\(stamp)-\(i).jpg")
            do {
                try p.data.write(to: url, options: .atomic)
                out.append(["path": url.path, "bias": Double(p.bias)])
            } catch {
                NSLog("[ScrunCamera] 사진 저장 실패: \(error)")
            }
        }
        return out
    }

    // 이 폴더 안의 파일만 지운다(C#이 넘긴 경로를 그대로 믿지 않음)
    static func discard(_ a: [String: Any]) {
        guard let paths = a["paths"] as? [String] else { return }
        let root = folder.standardizedFileURL.path + "/"
        for p in paths {
            let url = URL(fileURLWithPath: p).standardizedFileURL
            guard url.path.hasPrefix(root) else { continue }
            try? FileManager.default.removeItem(at: url)
        }
    }

    // 앱이 중간에 꺼져 남은 사진(1시간 지난 것)을 치운다
    private static func cleanOld() {
        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.contentModificationDateKey]) else { return }
        let limit = Date().addingTimeInterval(-3600)
        for f in files {
            let t = (try? f.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast
            if t < limit { try? fm.removeItem(at: f) }
        }
    }

    private static func topViewController() -> UIViewController? {
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        let windows = scenes.flatMap { $0.windows }
        var vc = (windows.first { $0.isKeyWindow } ?? windows.first)?.rootViewController
        while let p = vc?.presentedViewController, !p.isBeingDismissed { vc = p } // 사라지는 중인 창 위엔 못 띄움
        return vc
    }
}

// 사진 콜백은 아무 스레드에서나 온다. UIViewController(메인 액터)와 떼어 두고, 파일까지 쓴 뒤 메인으로 넘긴다.
private final class ScrunPhotoCollector: NSObject, AVCapturePhotoCaptureDelegate {
    private var photos: [(seq: Int, index: Int, bias: Float, data: Data)] = []
    private let lock = NSLock()
    private let done: ([[String: Any]], Error?) -> Void
    private let fallbackBias: Float

    // done: 메인 스레드에서 쓴 사진 [{path, bias}](요청한 노출 순서)
    init(fallbackBias: Float, done: @escaping ([[String: Any]], Error?) -> Void) {
        self.fallbackBias = fallbackBias
        self.done = done
    }

    func photoOutput(_ output: AVCapturePhotoOutput, didFinishProcessingPhoto photo: AVCapturePhoto, error: Error?) {
        guard error == nil, let data = photo.fileDataRepresentation() else { return }
        let bias = (photo.bracketSettings as? AVCaptureAutoExposureBracketedStillImageSettings)?.exposureTargetBias ?? fallbackBias
        lock.lock()
        photos.append((photo.sequenceCount, photo.photoCount, bias, data))
        lock.unlock()
    }

    func photoOutput(_ output: AVCapturePhotoOutput, didFinishCaptureFor resolvedSettings: AVCaptureResolvedPhotoSettings, error: Error?) {
        lock.lock()
        // 같은 브래킷의 사진은 sequenceCount가 같음 → 안에서는 photoCount(1부터, 요청한 순서)로(리뷰 6)
        let got = photos.sorted { ($0.seq, $0.index) < ($1.seq, $1.index) }
        photos = []
        lock.unlock()
        DispatchQueue.global(qos: .userInitiated).async {
            let saved = ScrunCamera.write(got.map { (bias: $0.bias, data: $0.data) })
            DispatchQueue.main.async { self.done(saved, error) }
        }
    }
}

// 카메라 세션(AVFoundation). 세션·장치 설정은 모두 자기 큐에서 한다(Apple 권장: startRunning은 오래 걸림).
// 화면(메인 액터)과 떼어 둬 스레드 규칙이 섞이지 않게 한다.
final class ScrunCaptureEngine {
    let session = AVCaptureSession()
    let output = AVCapturePhotoOutput()
    private let queue = DispatchQueue(label: "scrun.camera.session")
    private var device: AVCaptureDevice?
    private var runningObserver: NSObjectProtocol?

    init() {
        // 세션이 (다시) 돌기 시작하면 형식이 다시 적용돼 셔터 상한이 풀릴 수 있음 → 그때마다 다시(리뷰 8)
        runningObserver = NotificationCenter.default.addObserver(
            forName: .AVCaptureSessionDidStartRunning, object: session, queue: nil) { [weak self] _ in self?.reapplyCap() }
    }

    deinit {
        if let o = runningObserver { NotificationCenter.default.removeObserver(o) }
    }

    // 끝나면 메인 스레드에서 장치(실패면 nil)
    func start(_ done: @escaping (AVCaptureDevice?) -> Void) {
        queue.async {
            let s = self.session
            s.beginConfiguration()
            s.sessionPreset = .photo
            guard let dev = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back),
                  let input = try? AVCaptureDeviceInput(device: dev),
                  s.canAddInput(input), s.canAddOutput(self.output) else {
                s.commitConfiguration()
                DispatchQueue.main.async { done(nil) }
                return
            }
            s.addInput(input)
            s.addOutput(self.output)
            self.output.maxPhotoQualityPrioritization = .quality
            s.commitConfiguration()
            self.device = dev
            Self.capExposureDuration(dev)
            s.startRunning() // 돌기 시작하면 위 observer가 셔터 상한을 다시 건다
            DispatchQueue.main.async { done(dev) }
        }
    }

    // 초점·노출 기준점(장치 좌표 0–1)
    func meter(at point: CGPoint) {
        queue.async {
            guard let dev = self.device else { return }
            do {
                try dev.lockForConfiguration()
                defer { dev.unlockForConfiguration() }
                if dev.isFocusPointOfInterestSupported { dev.focusPointOfInterest = point }
                if dev.isFocusModeSupported(.continuousAutoFocus) { dev.focusMode = .continuousAutoFocus }
                if dev.isExposurePointOfInterestSupported { dev.exposurePointOfInterest = point }
                if dev.isExposureModeSupported(.continuousAutoExposure) { dev.exposureMode = .continuousAutoExposure }
            } catch {
                NSLog("[ScrunCamera] 기준점 설정 실패: \(error)")
            }
            Self.capExposureDuration(dev)
        }
    }

    func reapplyCap() {
        queue.async { if let dev = self.device { Self.capExposureDuration(dev) } }
    }

    func capture(_ settings: AVCapturePhotoSettings, _ delegate: AVCapturePhotoCaptureDelegate) {
        queue.async {
            // 셔터 상한이 실제로 걸렸는지 보려고(11/17 어두운 곳에서 사진이 1/15초로 찍힘 → 미리보기도 넘는지 확인)
            if let d = self.device {
                NSLog("[ScrunCamera] 셔터 직전 미리보기 노출 1/%.0f초 ISO %.0f, 상한 1/%.0f초",
                      1 / d.exposureDuration.seconds, d.iso, 1 / d.activeMaxExposureDuration.seconds)
            }
            self.output.capturePhoto(with: settings, delegate: delegate)
        }
    }

    func stop() {
        queue.async { if self.session.isRunning { self.session.stopRunning() } }
    }

    // 자동 노출이 셔터를 1/60초보다 길게 쓰지 못하게(가설: LED 깜빡임·손떨림). 그 대신 ISO가 오름
    private static func capExposureDuration(_ dev: AVCaptureDevice) {
        do {
            try dev.lockForConfiguration()
            defer { dev.unlockForConfiguration() }
            let f = dev.activeFormat
            var cap = CMTime(value: 1, timescale: 60)
            if CMTimeCompare(cap, f.maxExposureDuration) > 0 { cap = f.maxExposureDuration }
            if CMTimeCompare(cap, f.minExposureDuration) < 0 { cap = f.minExposureDuration }
            if dev.isExposureModeSupported(.continuousAutoExposure) { dev.exposureMode = .continuousAutoExposure }
            dev.activeMaxExposureDuration = cap
        } catch {
            NSLog("[ScrunCamera] 노출 설정 실패: \(error)")
        }
    }
}

final class ScrunCameraController: UIViewController {
    private let opts: ScrunCamera.Options
    private let finish: ScrunCamera.Done
    private var finished = false   // 닫기 시작함(더 이상 찍거나 닫지 않음)
    private var reported = false   // C#에 답함(꼭 한 번)
    private var presented = false  // present 애니메이션이 끝남. 그 전엔 dismiss가 무시됨(리뷰 1)
    private var pendingResult: [String: Any]? // 다 뜨기 전에 끝난 결과 → 뜨면 닫음
    private var capturing = false
    private var interrupted = false // 전화·다른 앱이 카메라를 씀(리뷰 4)
    private var interruptTimeout: DispatchWorkItem?
    private var captureWatchdog: DispatchWorkItem?
    private static let captureLimitSec = 15.0 // 브래킷 3장은 보통 1초 안(11/13·11/17 아이폰 13 mini)
    // 중단이 이만큼 이어지면 unavailable로 닫음(가설: 전화를 받으면 보통 이보다 김 → 끝나고 다시 찍게)
    private static let interruptLimitSec = 10.0

    private let engine = ScrunCaptureEngine()
    private var device: AVCaptureDevice? // 세션이 뜬 뒤(메인 스레드에서 읽기만)
    private var collector: ScrunPhotoCollector? // 찍는 동안 붙잡아 둠(AVFoundation은 delegate를 약하게 잡음)

    private let previewBox = UIView()
    private var previewLayer: AVCaptureVideoPreviewLayer!
    private let guideLayer = CAShapeLayer()
    private let guideTag = PaddedLabel()
    private let closeButton = UIButton(type: .system)
    private let timerLabel = PaddedLabel()
    private let hintLabel = UILabel()
    private let shutter = UIButton(type: .custom)
    private let spinner = UIActivityIndicatorView(style: .large)

    private var timer: Timer?
    private var remaining = 0
    private var guideRect = CGRect.zero
    private var meteredRect = CGRect.zero

    // UI Kit 값(App.uss): ink #172036, sky/700 #2F6FE4. Figma E-03과 나란히 놓고 상준이 다듬음
    private static let ink = UIColor(red: 0x17 / 255.0, green: 0x20 / 255.0, blue: 0x36 / 255.0, alpha: 1)
    private static let sky700 = UIColor(red: 0x2F / 255.0, green: 0x6F / 255.0, blue: 0xE4 / 255.0, alpha: 1)

    init(_ opts: ScrunCamera.Options, finish: @escaping ScrunCamera.Done) {
        self.opts = opts
        self.finish = finish
        super.init(nibName: nil, bundle: nil)
        modalPresentationStyle = .fullScreen
    }

    required init?(coder: NSCoder) { fatalError("코드로만 만든다") }

    override var supportedInterfaceOrientations: UIInterfaceOrientationMask { .portrait }
    override var preferredInterfaceOrientationForPresentation: UIInterfaceOrientation { .portrait }
    override var preferredStatusBarStyle: UIStatusBarStyle { .lightContent }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = Self.ink
        buildUI()
        engine.start { [weak self] dev in
            guard let self = self, !self.finished else { return }
            guard let dev = dev else { self.close(["outcome": "unavailable"]); return }
            self.device = dev
            Self.portrait(self.previewLayer.connection)
            self.meterAtGuide()
        }
        remaining = opts.timeLimitSec
        updateTimer()
        if opts.timeLimitSec > 0 {
            timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.tick() }
        }
        let nc = NotificationCenter.default
        nc.addObserver(self, selector: #selector(sessionError), name: .AVCaptureSessionRuntimeError, object: engine.session)
        nc.addObserver(self, selector: #selector(sessionInterrupted), name: .AVCaptureSessionWasInterrupted, object: engine.session)
        nc.addObserver(self, selector: #selector(interruptionEnded), name: .AVCaptureSessionInterruptionEnded, object: engine.session)
    }

    // ScrunCamera.shoot이 present 완료 콜백에서 부름
    func didPresent() {
        presented = true
        if let r = pendingResult {
            pendingResult = nil
            dismissAndReport(r)
        }
    }

    // present가 안 됨: 화면 없이 바로 unavailable(리뷰 2)
    func presentFailed() {
        guard !reported else { return }
        finished = true
        teardown()
        report(["outcome": "unavailable"])
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer.frame = previewBox.bounds
        // 가이드: 미리보기 가운데, 가로 84%, 계기판처럼 가로로 긴 칸(5:3)
        let w = previewBox.bounds.width * 0.84
        let h = w * 0.6
        guideRect = CGRect(x: (previewBox.bounds.width - w) / 2, y: (previewBox.bounds.height - h) / 2, width: w, height: h)
        guideLayer.frame = previewBox.bounds
        guideLayer.path = UIBezierPath(roundedRect: guideRect, cornerRadius: 12).cgPath
        guideTag.frame.origin = CGPoint(x: guideRect.minX, y: guideRect.minY - guideTag.intrinsicContentSize.height - 8)
        guideTag.frame.size = guideTag.intrinsicContentSize
        if guideRect != meteredRect { meterAtGuide() }
    }

    // ---- UI ----

    private func buildUI() {
        previewBox.backgroundColor = .black
        previewBox.layer.cornerRadius = 20
        previewBox.clipsToBounds = true
        previewLayer = AVCaptureVideoPreviewLayer(session: engine.session)
        previewLayer.videoGravity = .resizeAspectFill
        previewBox.layer.addSublayer(previewLayer)

        guideLayer.strokeColor = UIColor.white.cgColor
        guideLayer.fillColor = UIColor.clear.cgColor
        guideLayer.lineWidth = 2
        guideLayer.lineDashPattern = [8, 6]
        previewBox.layer.addSublayer(guideLayer)

        guideTag.text = opts.guideLabel
        guideTag.font = .systemFont(ofSize: 13, weight: .semibold)
        guideTag.textColor = Self.sky700
        guideTag.backgroundColor = UIColor.white.withAlphaComponent(0.92)
        guideTag.layer.cornerRadius = 12
        guideTag.clipsToBounds = true
        previewBox.addSubview(guideTag)

        closeButton.setTitle("✕", for: .normal)
        closeButton.titleLabel?.font = .systemFont(ofSize: 18, weight: .semibold)
        closeButton.tintColor = Self.ink
        closeButton.backgroundColor = .white
        closeButton.layer.cornerRadius = 20
        closeButton.accessibilityLabel = "닫기"
        closeButton.addTarget(self, action: #selector(closeTapped), for: .touchUpInside)

        timerLabel.font = .monospacedDigitSystemFont(ofSize: 14, weight: .semibold)
        timerLabel.textColor = .white
        timerLabel.backgroundColor = UIColor.white.withAlphaComponent(0.16)
        timerLabel.layer.cornerRadius = 14
        timerLabel.clipsToBounds = true
        timerLabel.isHidden = opts.timeLimitSec <= 0

        hintLabel.text = opts.hint
        hintLabel.font = .systemFont(ofSize: 15, weight: .medium)
        hintLabel.textColor = UIColor.white.withAlphaComponent(0.85)
        hintLabel.textAlignment = .center
        hintLabel.numberOfLines = 0

        shutter.backgroundColor = .white
        shutter.layer.cornerRadius = 32
        shutter.layer.borderColor = Self.ink.cgColor
        shutter.layer.borderWidth = 3
        let ring = UIView()
        ring.isUserInteractionEnabled = false
        ring.layer.borderColor = UIColor.white.cgColor
        ring.layer.borderWidth = 4
        ring.layer.cornerRadius = 40
        shutter.accessibilityLabel = "촬영"
        shutter.addTarget(self, action: #selector(shutterTapped), for: .touchUpInside)
        spinner.color = Self.ink
        spinner.hidesWhenStopped = true

        for v in [previewBox, closeButton, timerLabel, hintLabel, ring, shutter, spinner] as [UIView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            view.addSubview(v)
        }
        let safe = view.safeAreaLayoutGuide
        NSLayoutConstraint.activate([
            closeButton.leadingAnchor.constraint(equalTo: safe.leadingAnchor, constant: 16),
            closeButton.topAnchor.constraint(equalTo: safe.topAnchor, constant: 8),
            closeButton.widthAnchor.constraint(equalToConstant: 40),
            closeButton.heightAnchor.constraint(equalToConstant: 40),

            timerLabel.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            timerLabel.centerYAnchor.constraint(equalTo: closeButton.centerYAnchor),
            timerLabel.heightAnchor.constraint(equalToConstant: 28),

            previewBox.topAnchor.constraint(equalTo: closeButton.bottomAnchor, constant: 16),
            previewBox.leadingAnchor.constraint(equalTo: safe.leadingAnchor, constant: 16),
            previewBox.trailingAnchor.constraint(equalTo: safe.trailingAnchor, constant: -16),
            previewBox.bottomAnchor.constraint(equalTo: hintLabel.topAnchor, constant: -16),

            hintLabel.leadingAnchor.constraint(equalTo: safe.leadingAnchor, constant: 24),
            hintLabel.trailingAnchor.constraint(equalTo: safe.trailingAnchor, constant: -24),
            hintLabel.bottomAnchor.constraint(equalTo: ring.topAnchor, constant: -20),

            ring.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            ring.bottomAnchor.constraint(equalTo: safe.bottomAnchor, constant: -20),
            ring.widthAnchor.constraint(equalToConstant: 80),
            ring.heightAnchor.constraint(equalToConstant: 80),
            shutter.centerXAnchor.constraint(equalTo: ring.centerXAnchor),
            shutter.centerYAnchor.constraint(equalTo: ring.centerYAnchor),
            shutter.widthAnchor.constraint(equalToConstant: 64),
            shutter.heightAnchor.constraint(equalToConstant: 64),
            spinner.centerXAnchor.constraint(equalTo: shutter.centerXAnchor),
            spinner.centerYAnchor.constraint(equalTo: shutter.centerYAnchor),
        ])
    }

    private func tick() {
        guard !finished, !capturing, !interrupted else { return } // 중단 중엔 검은 화면이라 시간을 안 셈
        remaining -= 1
        updateTimer()
        if remaining <= 0 { close(["outcome": "timedOut"]) }
    }

    private func updateTimer() {
        let s = max(0, remaining)
        timerLabel.text = String(format: "남은 시간 %d:%02d", s / 60, s % 60)
    }

    @objc private func closeTapped() {
        guard !capturing else { return }
        close(["outcome": "cancelled"])
    }

    @objc private func sessionError(_ n: Notification) {
        NSLog("[ScrunCamera] 세션 오류: \(String(describing: n.userInfo?[AVCaptureSessionErrorKey]))")
        DispatchQueue.main.async { self.close(["outcome": "unavailable"]) }
    }

    @objc private func sessionInterrupted(_ n: Notification) {
        let reason = (n.userInfo?[AVCaptureSessionInterruptionReasonKey] as? Int).flatMap { AVCaptureSession.InterruptionReason(rawValue: $0) }
        DispatchQueue.main.async { self.setInterrupted(true, reason) }
    }

    @objc private func interruptionEnded(_ n: Notification) {
        DispatchQueue.main.async { self.setInterrupted(false, nil) }
    }

    // 중단 중엔 셔터를 막고 안내. 앱이 뒤로 간 것(돌아오면 저절로 풀림)이 아니면 오래 이어질 때 unavailable로 닫음
    private func setInterrupted(_ on: Bool, _ reason: AVCaptureSession.InterruptionReason?) {
        guard !finished else { return }
        interrupted = on
        interruptTimeout?.cancel()
        interruptTimeout = nil
        updateControls()
        if on {
            NSLog("[ScrunCamera] 카메라 중단: \(reason.map { String(describing: $0.rawValue) } ?? "?")")
            switch reason {
            case .videoDeviceNotAvailableDueToSystemPressure?: hintLabel.text = "폰이 뜨거워 카메라를 잠시 쓸 수 없어요"
            case .videoDeviceNotAvailableWithMultipleForegroundApps?: hintLabel.text = "화면 나누기를 끄면 다시 찍을 수 있어요"
            default: hintLabel.text = "다른 앱이 카메라를 쓰고 있어요. 잠시만 기다려 주세요"
            }
            // 다른 앱·전화가 카메라를 가져간 경우만 오래 이어지면 닫음. 뒤로 감·화면 나누기·발열은 풀리면 저절로 돌아오고,
            // 그동안 남은 시간은 멈추며 X로 언제든 닫을 수 있음(리뷰)
            if reason == .videoDeviceInUseByAnotherClient || reason == .audioDeviceInUseByAnotherClient || reason == nil {
                let w = DispatchWorkItem { [weak self] in self?.close(["outcome": "unavailable"]) }
                interruptTimeout = w
                DispatchQueue.main.asyncAfter(deadline: .now() + Self.interruptLimitSec, execute: w)
            }
        } else {
            NSLog("[ScrunCamera] 카메라 중단 끝")
            hintLabel.text = opts.hint
            meterAtGuide()
        }
    }

    private func updateControls() {
        shutter.isEnabled = !finished && !capturing && !interrupted
        closeButton.isEnabled = !capturing
    }

    // ---- 카메라 ----

    // 초점·노출 기준점을 가이드 프레임 가운데로
    private func meterAtGuide() {
        guard device != nil, guideRect.width > 0, previewLayer.connection != nil else { return }
        meteredRect = guideRect
        engine.meter(at: previewLayer.captureDevicePointConverted(fromLayerPoint: CGPoint(x: guideRect.midX, y: guideRect.midY)))
    }

    @objc private func shutterTapped() {
        guard !capturing, !finished, !interrupted, let dev = device else { return }
        capturing = true
        updateControls()
        spinner.startAnimating()

        let out = engine.output
        // JPEG: 서버 OCR이 바로 읽게(HEIC 아님). 거의 모든 기기가 지원하지만 없으면 기본 형식
        let format: [String: Any]? = out.availablePhotoCodecTypes.contains(.jpeg) ? [AVVideoCodecKey: AVVideoCodecType.jpeg] : nil
        let biases = opts.biases.map { min(max($0, dev.minExposureTargetBias), dev.maxExposureTargetBias) }
        let maxCount = out.maxBracketedCapturePhotoCount
        let settings: AVCapturePhotoSettings
        var reportedBias: Float = 0
        if maxCount >= 1 {
            // 한 장이어도 브래킷으로 찍어야 요청한 노출이 됨(그냥 찍으면 자동 노출, 리뷰 5)
            let brackets = biases.prefix(maxCount).map {
                AVCaptureAutoExposureBracketedStillImageSettings.autoExposureSettings(exposureTargetBias: $0)
            }
            settings = AVCapturePhotoBracketSettings(rawPixelFormatType: 0, processedFormat: format, bracketedSettings: Array(brackets))
        } else {
            // 브래킷을 못 하는 기기(지금 아이폰엔 없음): 자동 노출 한 장, 노출은 0으로 알림
            settings = format.map { AVCapturePhotoSettings(format: $0) } ?? AVCapturePhotoSettings()
            settings.photoQualityPrioritization = .quality
            reportedBias = 0
        }
        Self.portrait(out.connection(with: .video))

        let c = ScrunPhotoCollector(fallbackBias: reportedBias) { [weak self] photos, error in
            guard let self = self else {
                // 찍는 사이 화면이 닫히고 없어짐: 아무도 안 받는 사진은 지움(리뷰)
                ScrunCamera.discard(["paths": photos.compactMap { $0["path"] as? String }])
                return
            }
            self.captureWatchdog?.cancel()
            self.captureWatchdog = nil
            self.collector = nil
            self.saved(photos, error)
        }
        collector = c
        // 촬영 완료가 끝내 안 오면(세션이 멈춤 등) 셔터·X가 영원히 막히지 않게 풀어 줌(리뷰)
        let w = DispatchWorkItem { [weak self] in
            guard let self = self, self.capturing, !self.finished else { return }
            NSLog("[ScrunCamera] 촬영 완료가 안 와서 다시 찍게 함")
            self.captureWatchdog = nil
            self.capturing = false
            self.spinner.stopAnimating()
            self.updateControls()
        }
        captureWatchdog = w
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.captureLimitSec, execute: w)
        engine.capture(settings, c)
    }

    // photos: 이미 파일로 씀(ScrunPhotoCollector)
    private func saved(_ photos: [[String: Any]], _ error: Error?) {
        if let error = error { NSLog("[ScrunCamera] 촬영 오류: \(error)") }
        guard !finished else {
            // 찍는 동안 닫힘(세션 오류·긴 중단): 아무도 안 받는 사진은 지움
            ScrunCamera.discard(["paths": photos.compactMap { $0["path"] as? String }])
            return
        }
        capturing = false
        spinner.stopAnimating()
        guard !photos.isEmpty else {
            // 한 장도 못 얻음: 화면에 남아 다시 찍게 한다
            updateControls()
            return
        }
        close(["outcome": "taken", "photos": photos])
    }

    private func close(_ result: [String: Any]) {
        guard !finished else { return }
        finished = true
        teardown()
        updateControls()
        if presented { dismissAndReport(result) } else { pendingResult = result } // 뜨는 중이면 다 뜬 뒤 닫음(리뷰 1)
    }

    private func teardown() {
        timer?.invalidate()
        timer = nil
        interruptTimeout?.cancel()
        interruptTimeout = nil
        captureWatchdog?.cancel()
        captureWatchdog = nil
        NotificationCenter.default.removeObserver(self)
        engine.stop()
    }

    private func dismissAndReport(_ result: [String: Any]) {
        guard presentingViewController != nil else { report(result); return }
        dismiss(animated: true) { self.report(result) }
        ensureDismissed(result, triesLeft: 20)
    }

    // 닫기가 무시되면(다른 전환과 겹침) 0.5초마다 다시 닫아 본다. 정말 사라진 뒤에 답하고,
    // 10초가 지나도 남아 있으면 C#이 영원히 기다리지 않게 먼저 답한 뒤 계속 닫아 본다(리뷰)
    private func ensureDismissed(_ result: [String: Any], triesLeft: Int) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
            guard self.presentingViewController != nil else { self.report(result); return }
            if self.transitionCoordinator == nil && !self.isBeingDismissed {
                NSLog("[ScrunCamera] 닫기가 무시돼 다시 닫음")
                self.presentingViewController?.dismiss(animated: false) { self.report(result) }
            }
            if triesLeft == 0 { self.report(result) }
            if triesLeft > -100 { self.ensureDismissed(result, triesLeft: triesLeft - 1) } // 그 뒤로도 약 50초 더
        }
    }

    private func report(_ result: [String: Any]) {
        guard !reported else { return }
        reported = true
        finish(result)
    }

    // 세로 고정. iOS 17부터는 회전 각도, 그 전엔 방향
    private static func portrait(_ c: AVCaptureConnection?) {
        guard let c = c else { return }
        if #available(iOS 17.0, *) {
            if c.isVideoRotationAngleSupported(90) { c.videoRotationAngle = 90 }
        } else if c.isVideoOrientationSupported {
            c.videoOrientation = .portrait
        }
    }
}

// 안쪽 여백이 있는 라벨(남은 시간·가이드 태그)
private final class PaddedLabel: UILabel {
    var inset = UIEdgeInsets(top: 4, left: 12, bottom: 4, right: 12)
    override func drawText(in rect: CGRect) { super.drawText(in: rect.inset(by: inset)) }
    override var intrinsicContentSize: CGSize {
        let s = super.intrinsicContentSize
        return CGSize(width: s.width + inset.left + inset.right, height: s.height + inset.top + inset.bottom)
    }
}
