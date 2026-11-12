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
        guard current == nil else { throw ScrunError("카메라 화면이 이미 떠 있습니다.") }
        let opts = Options(a)
        cleanOld()
        ScrunPermissions.shared.request(.camera) { status in
            ScrunBridge.onMain {
                guard status == .granted else { done(["outcome": "noPermission"]); return }
                guard current == nil else { done(["outcome": "unavailable"]); return } // 권한 팝업 사이에 또 불림
                guard AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back) != nil,
                      let top = topViewController() else {
                    done(["outcome": "unavailable"]) // 시뮬레이터에는 카메라가 없음
                    return
                }
                let vc = ScrunCameraController(opts) { result in
                    current = nil
                    done(result)
                }
                current = vc
                top.present(vc, animated: true)
            }
        }
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
        while let p = vc?.presentedViewController { vc = p }
        return vc
    }
}

// 사진 콜백은 아무 스레드에서나 온다. UIViewController(메인 액터)와 떼어 두고 메인으로 넘긴다.
private final class ScrunPhotoCollector: NSObject, AVCapturePhotoCaptureDelegate {
    private var photos: [(seq: Int, bias: Float, data: Data)] = []
    private let lock = NSLock()
    private let done: ([(seq: Int, bias: Float, data: Data)], Error?) -> Void
    private let fallbackBias: Float

    init(fallbackBias: Float, done: @escaping ([(seq: Int, bias: Float, data: Data)], Error?) -> Void) {
        self.fallbackBias = fallbackBias
        self.done = done
    }

    func photoOutput(_ output: AVCapturePhotoOutput, didFinishProcessingPhoto photo: AVCapturePhoto, error: Error?) {
        guard error == nil, let data = photo.fileDataRepresentation() else { return }
        let bias = (photo.bracketSettings as? AVCaptureAutoExposureBracketedStillImageSettings)?.exposureTargetBias ?? fallbackBias
        lock.lock()
        photos.append((photo.sequenceCount, bias, data))
        lock.unlock()
    }

    func photoOutput(_ output: AVCapturePhotoOutput, didFinishCaptureFor resolvedSettings: AVCaptureResolvedPhotoSettings, error: Error?) {
        lock.lock()
        let got = photos.sorted { $0.seq < $1.seq }
        lock.unlock()
        DispatchQueue.main.async { self.done(got, error) }
    }
}

// 카메라 세션(AVFoundation). 세션·장치 설정은 모두 자기 큐에서 한다(Apple 권장: startRunning은 오래 걸림).
// 화면(메인 액터)과 떼어 둬 스레드 규칙이 섞이지 않게 한다.
final class ScrunCaptureEngine {
    let session = AVCaptureSession()
    let output = AVCapturePhotoOutput()
    private let queue = DispatchQueue(label: "scrun.camera.session")
    private var device: AVCaptureDevice?

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
            s.startRunning()
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
        }
    }

    func capture(_ settings: AVCapturePhotoSettings, _ delegate: AVCapturePhotoCaptureDelegate) {
        queue.async { self.output.capturePhoto(with: settings, delegate: delegate) }
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
    private var finished = false
    private var capturing = false

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
        NotificationCenter.default.addObserver(self, selector: #selector(sessionError),
                                               name: .AVCaptureSessionRuntimeError, object: engine.session)
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
        guard !finished, !capturing else { return }
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

    // ---- 카메라 ----

    // 초점·노출 기준점을 가이드 프레임 가운데로
    private func meterAtGuide() {
        guard device != nil, guideRect.width > 0, previewLayer.connection != nil else { return }
        meteredRect = guideRect
        engine.meter(at: previewLayer.captureDevicePointConverted(fromLayerPoint: CGPoint(x: guideRect.midX, y: guideRect.midY)))
    }

    @objc private func shutterTapped() {
        guard !capturing, !finished, let dev = device else { return }
        capturing = true
        shutter.isEnabled = false
        closeButton.isEnabled = false
        spinner.startAnimating()

        let out = engine.output
        // JPEG: 서버 OCR이 바로 읽게(HEIC 아님). 거의 모든 기기가 지원하지만 없으면 기본 형식
        let format: [String: Any]? = out.availablePhotoCodecTypes.contains(.jpeg) ? [AVVideoCodecKey: AVVideoCodecType.jpeg] : nil
        let biases = opts.biases.map { min(max($0, dev.minExposureTargetBias), dev.maxExposureTargetBias) }
        let maxCount = out.maxBracketedCapturePhotoCount
        let settings: AVCapturePhotoSettings
        if biases.count > 1 && maxCount > 1 {
            let brackets = biases.prefix(maxCount).map {
                AVCaptureAutoExposureBracketedStillImageSettings.autoExposureSettings(exposureTargetBias: $0)
            }
            settings = AVCapturePhotoBracketSettings(rawPixelFormatType: 0, processedFormat: format, bracketedSettings: Array(brackets))
        } else {
            // 브래킷을 못 하는 기기·한 장만 원할 때: 자동 노출 한 장
            settings = format.map { AVCapturePhotoSettings(format: $0) } ?? AVCapturePhotoSettings()
            settings.photoQualityPrioritization = .quality
        }
        Self.portrait(out.connection(with: .video))

        let c = ScrunPhotoCollector(fallbackBias: 0) { [weak self] photos, error in
            self?.collector = nil
            self?.saved(photos, error)
        }
        collector = c
        engine.capture(settings, c)
    }

    private func saved(_ photos: [(seq: Int, bias: Float, data: Data)], _ error: Error?) {
        guard !finished else { return }
        if let error = error { NSLog("[ScrunCamera] 촬영 오류: \(error)") }
        let fm = FileManager.default
        try? fm.createDirectory(at: ScrunCamera.folder, withIntermediateDirectories: true)
        let stamp = Int(Date().timeIntervalSince1970 * 1000)
        var out: [[String: Any]] = []
        for (i, p) in photos.enumerated() {
            let url = ScrunCamera.folder.appendingPathComponent("\(stamp)-\(i).jpg")
            do {
                try p.data.write(to: url, options: .atomic)
                out.append(["path": url.path, "bias": Double(p.bias)])
            } catch {
                NSLog("[ScrunCamera] 사진 저장 실패: \(error)")
            }
        }
        guard !out.isEmpty else {
            // 한 장도 못 얻음: 화면에 남아 다시 찍게 한다
            capturing = false
            shutter.isEnabled = true
            closeButton.isEnabled = true
            spinner.stopAnimating()
            return
        }
        close(["outcome": "taken", "photos": out])
    }

    private func close(_ result: [String: Any]) {
        guard !finished else { return }
        finished = true
        timer?.invalidate()
        timer = nil
        NotificationCenter.default.removeObserver(self)
        engine.stop()
        dismiss(animated: true) { self.finish(result) }
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
