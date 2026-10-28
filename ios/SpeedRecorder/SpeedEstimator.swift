import Foundation

/// 실시간 속도: 계기판 아이폰(충격·걸음 수) + 에어팟(머리 흔들림) (계획 0010)
/// speedlab/fusion_live.py를 그대로 옮긴 것. 숫자·규칙을 바꾸면 둘 다 고친다.
/// 1. 걷기·달리기: 에어팟 "0.5 g 아래 시간"(걷기는 공중이 없어 거의 0), 에어팟이 끊기면 폰 걸음 수. holdS 동안 이어져야 바꿈
/// 2. 달리기: 에어팟 있으면 a + b·ln(충격) + airD·흔들림 폭, 끊기면 a' + b'·ln(충격). a·b는 이 기록의 보정 구간(달리기만)
/// 3. 걷기: 공통 걸음 수 공식
/// Recorder의 모션 큐(직렬)에서만 부른다.
final class SpeedEstimator {
    struct Snapshot {
        var t: Double
        var kmh: Double?        // 보여 줄 값 (최근 2초 중앙값)
        var kmhPhone: Double?   // 폰만 (달리기일 때)
        var state: Int          // 0 걸음 없음, 1 걷기, 2 달리기
        var byAirPods: Bool     // 판단에 에어팟을 썼는지
        var airOK: Bool
        var cad: Double?
        var imp: Double?
        var amp: Double?
        var lowgMs: Double?
        var nPoints: Int
        var nRunPoints: Int
    }

    struct Point: Codable {
        var v: Double
        var limp: Double
        var cad: Double
        var amp: Double?
        var running: Bool
    }

    static let fs = 100.0, winS = 6.0, calSkipS = 5.0
    static let lowG = 0.5, runLowgMs = 70.0, runCad = 150.0, qMin = 0.3
    static let airMaxAge = 0.5, airMinCover = 0.7, holdS = 2.5, smoothS = 2.0, updateS = 0.5
    static let airD = 2.598                    // km/h per g (10/29 B 달리기 11구간 전체 맞춤)
    static let bFull = 4.828, bPhone = 6.396   // 보정점이 하나뿐일 때 빌리는 ln(충격) 기울기 (B)
    static let walkA = -3.45, walkB = 0.0732   // 걷기 v = a + b·spm
    static let g = 9.80665
    // scipy butter(4, 8, 'high', fs=100) / butter(4, 5, 'low', fs=100), sos = [b0, b1, b2, a0, a1, a2]
    static let hpSos: [[Double]] = [[0.514246848996, -1.02849369799, 0.514246848996, 1.0, -1.21281209262, 0.384004162287],
                                    [1.0, -2.0, 1.0, 1.0, -1.4797988944, 0.688676953054]]
    static let lpSos: [[Double]] = [[0.000416599204407, 0.000833198408813, 0.000416599204407, 1.0, -1.47967421693, 0.555821543282],
                                    [1.0, 2.0, 1.0, 1.0, -1.70096433194, 0.788499739815]]

    /// 샘플 하나씩 넣는 sosfilt (Direct Form II transposed, scipy와 같음)
    final class Sos {
        let sos: [[Double]]
        var z: [[Double]]
        init(_ sos: [[Double]]) { self.sos = sos; z = Array(repeating: [0, 0], count: sos.count) }
        func step(_ input: Double) -> Double {
            var x = input
            for i in 0..<sos.count {
                let s = sos[i]
                let y = s[0] * x + z[i][0]
                z[i][0] = s[1] * x - s[4] * y + z[i][1]
                z[i][1] = s[2] * x - s[5] * y
                x = y
            }
            return x
        }
    }

    private let hp = [Sos(SpeedEstimator.hpSos), Sos(SpeedEstimator.hpSos), Sos(SpeedEstimator.hpSos)]
    private let lp = Sos(SpeedEstimator.lpSos)
    private var envT: [Double] = [], envV: [Double] = []
    private var airT: [Double] = [], airV: [Double] = []
    private var state = 0, cand = 0, candSince = 0.0
    private var hist: [(Double, Double)] = []
    private var stage: (kind: String, speed: Double?, t: Double)?
    private(set) var points: [Point] = []
    private(set) var newPoints = 0          // 이번 기록에서 새로 생긴 보정점 수 (있으면 끝날 때 저장)
    private var full: (a: Double, b: Double)?
    private var phone: (a: Double, b: Double)?
    private var lastUpdate = -1e9

    // MARK: 입력

    /// 아이폰 가속도(중력 포함, m/s²). 0.5초마다 새 추정을 돌려준다
    func addPhone(_ t: Double, _ x: Double, _ y: Double, _ z: Double) -> Snapshot? {
        let a = hp[0].step(x), b = hp[1].step(y), c = hp[2].step(z)
        envT.append(t)
        envV.append(lp.step((a * a + b * b + c * c).squareRoot()))
        trim(&envT, &envV, t)
        guard t - lastUpdate >= Self.updateS else { return nil }
        lastUpdate = t
        return update(t)
    }

    /// 에어팟 전체 가속도(중력 포함, m/s²)
    func addAirPods(_ t: Double, _ x: Double, _ y: Double, _ z: Double) {
        airT.append(t)
        airV.append((x * x + y * y + z * z).squareRoot() / Self.g)
        trim(&airT, &airV, t)
    }

    /// 구간이 바뀔 때: 끝난 구간이 보정이면 보정점 하나
    func beginStage(_ set: String, speed: Double?, t: Double) {
        if let s = stage, s.kind == "calib", let v = s.speed {
            addPoint(v, s.t + Self.calSkipS, t)
        }
        stage = (set, speed, t)
    }

    private func trim(_ ts: inout [Double], _ vs: inout [Double], _ t: Double) {
        if ts.count > 4000, let k = ts.firstIndex(where: { $0 >= t - 30 }), k > 0 {
            ts.removeFirst(k)
            vs.removeFirst(k)
        }
    }

    // MARK: 특징

    private func slice(_ ts: [Double], _ vs: [Double], _ t0: Double, _ t1: Double) -> (t: ArraySlice<Double>, v: ArraySlice<Double>) {
        let a = lowerBound(ts, t0), b = upperBound(ts, t1)
        return a < b ? (ts[a..<b], vs[a..<b]) : ([], [])
    }

    private func phoneFeats(_ t0: Double, _ t1: Double) -> (cad: Double, q: Double, imp: Double)? {
        let s = Array(slice(envT, envV, t0, t1).v)
        guard Double(s.count) >= Self.fs * 3 else { return nil }
        let (cad, q) = Self.cadence(s)
        return (cad, q, Self.impact(s))
    }

    private func airFeats(_ t0: Double, _ t1: Double, cad: Double?) -> (amp: Double, lowgMs: Double, cover: Double, last: Double)? {
        let w = slice(airT, airV, t0, t1)
        guard w.v.count >= 10, let last = w.t.last else { return nil }
        let m = Array(w.v)
        let stepMs = 60000.0 / ((cad ?? 0) > 0 ? cad! : 165)
        let low = Double(m.filter { $0 < Self.lowG }.count) / Double(m.count)
        return (Self.percentile(m, 95) - Self.percentile(m, 5), low * stepMs, Double(m.count) / ((t1 - t0) * 45.0), last)
    }

    // MARK: 보정

    private func addPoint(_ v: Double, _ t0: Double, _ t1: Double) {
        guard let pf = phoneFeats(t0, t1), pf.q >= Self.qMin, pf.imp > 0 else { return }
        let af = airFeats(t0, t1, cad: pf.cad)
        let airOK = (af?.cover ?? 0) >= Self.airMinCover
        let running = airOK ? af!.lowgMs >= Self.runLowgMs : pf.cad >= Self.runCad
        points.append(Point(v: v, limp: log(pf.imp), cad: pf.cad, amp: airOK ? af!.amp : nil, running: running))
        newPoints += 1
        refit()
    }

    /// 저장해 둔 보정점으로 시작 (보정 없이 바로 달릴 때)
    func load(_ saved: [Point]) {
        points = saved
        refit()
    }

    private func refit() {
        let run = points.filter { $0.running }
        phone = Self.fit(run.map { ($0.limp, $0.v) }, bDefault: Self.bPhone)
        full = Self.fit(run.compactMap { p in p.amp.map { (p.limp, p.v - Self.airD * $0) } }, bDefault: Self.bFull)
    }

    /// y = a + b·x. 점 2개 이상이면 둘 다, 하나면 b를 빌림
    static func fit(_ xy: [(Double, Double)], bDefault: Double) -> (a: Double, b: Double)? {
        guard !xy.isEmpty else { return nil }
        let n = Double(xy.count)
        let mx = xy.map { $0.0 }.reduce(0, +) / n, my = xy.map { $0.1 }.reduce(0, +) / n
        let xs = xy.map { $0.0 }
        if xy.count >= 2, (xs.max()! - xs.min()!) > 0.05 {
            let sxy = xy.map { ($0.0 - mx) * ($0.1 - my) }.reduce(0, +)
            let sxx = xy.map { ($0.0 - mx) * ($0.0 - mx) }.reduce(0, +)
            let b = sxy / sxx
            return (my - b * mx, b)
        }
        return (my - bDefault * mx, bDefault)
    }

    // MARK: 실시간

    private func update(_ t1: Double) -> Snapshot {
        let t0 = t1 - Self.winS
        let pf = phoneFeats(t0, t1)
        let af = airFeats(t0, t1, cad: pf?.cad)
        let airOK = af != nil && af!.cover >= Self.airMinCover && t1 - af!.last <= Self.airMaxAge
        var c = 0
        var byAir = false
        if let pf, pf.q >= Self.qMin {
            if airOK {
                c = af!.lowgMs >= Self.runLowgMs ? 2 : 1
                byAir = true
            } else {
                c = pf.cad >= Self.runCad ? 2 : 1
            }
        }
        if c != cand { cand = c; candSince = t1 }
        if c != state && t1 - candSince >= Self.holdS { state = c }

        var kmh: Double?
        var kmhPhone: Double?
        if state == 2, let pf, pf.imp > 0 {
            let li = log(pf.imp)
            if let p = phone { kmhPhone = p.a + p.b * li }
            if airOK, let f = full { kmh = f.a + f.b * li + Self.airD * af!.amp } else { kmh = kmhPhone }
        } else if state == 1, let pf {
            kmh = Self.walkA + Self.walkB * pf.cad
        } else if state == 0 {
            kmh = 0
        }
        if let k = kmh { hist.append((t1, min(max(k, 0), 25))) }
        hist.removeAll { $0.0 < t1 - Self.smoothS }
        let shown = kmh == nil ? nil : Self.median(hist.map { $0.1 })
        return Snapshot(t: t1, kmh: shown, kmhPhone: kmhPhone, state: state, byAirPods: byAir, airOK: airOK,
                        cad: pf?.cad, imp: pf?.imp, amp: af?.amp, lowgMs: af?.lowgMs,
                        nPoints: points.count, nRunPoints: points.filter { $0.running }.count)
    }

    // MARK: StepSpeed.cs 계산 (speedlab/exp/soundtrackdb.py와 같음, 포락선 100 Hz)

    /// 자기상관으로 걸음 수(spm)와 신뢰(0에서 1)
    static func cadence(_ x: [Double]) -> (Double, Double) {
        let n = x.count
        let mean = x.reduce(0, +) / Double(n)
        let s = x.map { $0 - mean }
        let lo = Int((fs * 60 / 220).rounded(.down))
        var hi = Int((fs * 60 / 70).rounded(.up))
        hi = min(hi + 2, n / 2)
        let r0 = s.map { $0 * $0 }.reduce(0, +) / Double(n)
        guard r0 > 1e-12, hi > lo else { return (0, 0) }
        var r = [Double](repeating: 0, count: hi + 2)
        for k in (lo - 1)..<min(hi + 2, n) {
            var acc = 0.0
            for i in 0..<(n - k) { acc += s[i] * s[i + k] }
            r[k] = acc / Double(n - k) / r0
        }
        var best = lo + argmax(r, lo, hi)
        for mm in [3, 2] {
            let c = Double(best) / Double(mm)
            if c < Double(lo) { continue }
            let a = max(lo, Int(c) - 2), b = min(hi, Int(c) + 3)
            let kh = a + argmax(r, a, b)
            if r[kh] > 0.1 { best = kh; break }
        }
        var d = 0.0
        if lo < best && best < hi {
            let y0 = r[best - 1], y1 = r[best], y2 = r[best + 1]
            let den = y0 - 2 * y1 + y2
            if den != 0 { d = max(-0.5, min(0.5, 0.5 * (y0 - y2) / den)) }
        }
        return (60.0 / ((Double(best) + d) / fs), max(0, r[best]))
    }

    /// 충격 세기: 상위 25% 안의 봉우리(앞뒤 0.12초 안 최댓값) 중앙값
    static func impact(_ s: [Double]) -> Double {
        let n = s.count
        guard n > 25 else { return 0 }
        let thr = s.sorted()[Int(0.75 * Double(n - 1))]
        var pk: [Double] = []
        for i in 12..<(n - 12) where s[i] >= thr {
            if 12 == argmax(Array(s[(i - 12)...(i + 12)]), 0, 24) { pk.append(s[i]) }
        }
        return median(pk) ?? 0
    }

    /// a...b 안에서 가장 큰 값의 첫 위치 (a 기준 상대 위치)
    static func argmax(_ r: [Double], _ a: Int, _ b: Int) -> Int {
        var best = a
        for k in a...b where r[k] > r[best] { best = k }
        return best - a
    }

    static func median(_ a: [Double]) -> Double? {
        guard !a.isEmpty else { return nil }
        let s = a.sorted(), n = s.count
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2
    }

    /// numpy percentile 기본(선형 보간)
    static func percentile(_ a: [Double], _ p: Double) -> Double {
        let s = a.sorted()
        let pos = p / 100 * Double(s.count - 1)
        let i = Int(pos.rounded(.down)), f = pos - Double(i)
        return i + 1 < s.count ? s[i] * (1 - f) + s[i + 1] * f : s[i]
    }
}

private func lowerBound(_ a: [Double], _ x: Double) -> Int {
    var lo = 0, hi = a.count
    while lo < hi { let m = (lo + hi) / 2; if a[m] < x { lo = m + 1 } else { hi = m } }
    return lo
}

private func upperBound(_ a: [Double], _ x: Double) -> Int {
    var lo = 0, hi = a.count
    while lo < hi { let m = (lo + hi) / 2; if a[m] <= x { lo = m + 1 } else { hi = m } }
    return lo
}

/// 폰에 저장한 마지막 보정 (문서 폴더 calibration.json). 다른 날 보정 없이 달려 볼 때 쓴다 (계획 0010)
struct SavedCalibration: Codable {
    var saved: Double            // 저장 시각 (epoch 초)
    var runId: String            // 보정한 기록
    var points: [SpeedEstimator.Point]

    static var url: URL {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("calibration.json")
    }

    static func load() -> SavedCalibration? {
        guard let d = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(SavedCalibration.self, from: d)
    }

    /// 문서 폴더에 저장하고, 기록 폴더에도 사본
    func save(copyTo extra: URL? = nil) {
        guard let d = try? JSONEncoder().encode(self) else { return }
        try? d.write(to: SavedCalibration.url)
        if let extra { try? d.write(to: extra) }
    }

    var label: String {
        let f = DateFormatter()
        f.dateFormat = "M/d HH:mm"
        return "\(f.string(from: Date(timeIntervalSince1970: saved))) 보정 · 달리기 점 \(points.filter { $0.running }.count)개"
    }
}
