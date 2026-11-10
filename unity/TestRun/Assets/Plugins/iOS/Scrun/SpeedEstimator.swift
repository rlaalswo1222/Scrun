import Foundation

/// 실시간 속도: 계기판 아이폰(충격·걸음 수) + 에어팟(머리 흔들림) (계획 0010)
/// speedlab/fusion_live.py를 그대로 옮긴 것(맞는지는 ios/SpeedReplay로 speedlab/golden과 비교). 숫자·규칙을 바꾸면 둘 다 고친다.
/// 1. 걷기·달리기: 에어팟 "0.5 g 아래 시간"(걷기는 공중이 없어 거의 0), 에어팟이 끊기면 폰 걸음 수. holdS 동안 이어져야 바꿈
/// 2. 달리기: 에어팟 있으면 a + b·ln(충격) + airD·흔들림 폭, 끊기면 a' + b'·ln(충격). a·b는 이 기록의 보정 구간(달리기만)
/// 3. 걷기: 걸음 수 공식. 기울기는 공통, 절편은 이 사람의 걷기 보정점이 있으면 그걸로(walkOwn, 지금은 꺼 둠), 없으면 공통
/// 4. 보정 단계 하나 판정(계획 0023·0024): measureStage. 점은 쌓지 않고 돌려준다(Unity 앱은 C#이 모아 확정한 뒤 load)
/// 5. 누적 걸음: 포락선 봉우리를 그때그때 셈(다리 애니메이션용, 감지 지연 없이). 문턱은 0.5초마다 최근 3초로 다시 정함
/// 쓰는 곳: Unity 앱(ScrunSpeed.swift), 실험 앱 SpeedRecorder(ios/SpeedRecorder, 이 파일을 복사하지 않고 참조),
/// 재생 도구(ios/SpeedReplay). 한 객체는 한 직렬 큐에서만 부른다.
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
        var steps: Int          // 누적 걸음
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
    // 이 사람 걷기 절편을 쓸지(계획 0023 설계). 10/29 두 사람에서 공통 절편보다 낫지 않아 끔(fusion_live.py WALK_OWN)
    static let walkOwn = false
    // 보정 단계 판정(계획 0024). 숫자는 가설, fusion_live.py와 같음(근거는 그 파일과 speedlab/exp/live_stage_check.py)
    static let stageMinS = 3.0      // 앞을 버린 뒤 잴 시간이 이보다 짧으면 TooShort
    static let lostGapS = 2.0       // 폰 샘플이 이만큼 끊기면 SensorLost
    static let stillEnv = 0.12      // 포락선 75% 값이 이보다 작으면 걸음 없음(NoSteps). 아이폰 세기는 책상 확인 때 다시
    static let cadEdge = 215.0      // 걸음 수가 찾는 범위 끝(220)에 붙으면 벨트 모터 진동 → NoSteps
    static let walkOnlyKmh = 4.0    // 이 속도 이하에서 달리기로 나오면 걸음 수를 두 배로 잡은 것 → Irregular
    // 누적 걸음(봉우리 세기)
    static let stepHalf = 12        // 봉우리: 앞뒤 0.12초 안 최댓값(impact와 같음)
    static let stepGapS = 60.0 / 220 // 220 spm보다 빠른 봉우리는 같은 걸음
    static let stepRel = 0.5        // 문턱 = 최근 3초 포락선 90% 값 × 이 비율(아래는 stillEnv)

    /// 보정 단계 하나를 잰 결과. 이름은 C# CalStageResult와 같다(NotFaster는 앞 점과 비교라 C#이 판정)
    enum StageResult: String {
        case ok = "Ok", noSteps = "NoSteps", irregular = "Irregular", sensorLost = "SensorLost", tooShort = "TooShort"
    }
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
    private var walkA: Double?          // 이 사람의 걷기 절편(걷기 보정점이 있을 때)
    private var lastUpdate = -1e9
    private(set) var steps = 0          // 누적 걸음
    private var stepThr: Double?        // 봉우리 문턱(0.5초마다)
    private var lastStepT = -1e9

    // MARK: 입력

    /// 아이폰 가속도(중력 포함, m/s²). 0.5초마다 새 추정을 돌려준다
    func addPhone(_ t: Double, _ x: Double, _ y: Double, _ z: Double) -> Snapshot? {
        let a = hp[0].step(x), b = hp[1].step(y), c = hp[2].step(z)
        envT.append(t)
        envV.append(lp.step((a * a + b * b + c * c).squareRoot()))
        trim(&envT, &envV, t)
        countStep()
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

    /// stepHalf 샘플 전 값이 앞뒤 stepHalf 안 최댓값(같으면 앞이 이김)이고 문턱을 넘으면 한 걸음(0.12초 늦게 셈)
    private func countStep() {
        let n = envV.count
        guard let thr = stepThr, n >= 2 * Self.stepHalf + 1 else { return }
        let c = n - 1 - Self.stepHalf
        let tc = envT[c], vc = envV[c]
        guard vc >= thr, tc - lastStepT >= Self.stepGapS else { return }
        for i in (c - Self.stepHalf)..<n where envV[i] > vc || (envV[i] == vc && i < c) { return }
        steps += 1
        lastStepT = tc
    }

    private func updateStepThr(_ t1: Double) {
        let s = Array(slice(envT, envV, t1 - 3.0, .infinity).v)
        if Double(s.count) >= Self.fs { stepThr = max(Self.stillEnv, Self.stepRel * Self.percentile(s, 90)) }
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

    /// SpeedRecorder 구간 방식(beginStage): 끝난 보정 구간에서 점 하나
    private func addPoint(_ v: Double, _ t0: Double, _ t1: Double) {
        guard case (.ok, let p?) = stagePoint(v, t0, t1) else { return }
        points.append(p)
        newPoints += 1
        refit()
    }

    /// 앱 보정 단계 하나(계획 0024). begin에 단계 시작(속도 안내), 앞 calSkipS 버리고 end까지
    func measureStage(_ v: Double, begin: Double, end: Double) -> (StageResult, Point?) {
        let t0 = begin + Self.calSkipS
        guard end - t0 >= Self.stageMinS else { return (.tooShort, nil) }
        let ts = slice(envT, envV, t0, end).t
        guard let first = ts.first, let last = ts.last, first - t0 <= Self.lostGapS, end - last <= Self.lostGapS
        else { return (.sensorLost, nil) }
        var prev = first
        for t in ts {
            if t - prev > Self.lostGapS { return (.sensorLost, nil) }
            prev = t
        }
        return stagePoint(v, t0, end)
    }

    /// [t0, t1](이미 앞을 버린 창)의 보정점 판정
    private func stagePoint(_ v: Double, _ t0: Double, _ t1: Double) -> (StageResult, Point?) {
        let s = Array(slice(envT, envV, t0, t1).v)
        guard Double(s.count) >= Self.fs * Self.stageMinS else { return (.tooShort, nil) }
        if Self.envLevel(s) < Self.stillEnv { return (.noSteps, nil) }
        let (cad, q) = Self.cadence(s)
        let imp = Self.impact(s)
        if cad >= Self.cadEdge { return (.noSteps, nil) }
        if q < Self.qMin || imp <= 0 { return (.irregular, nil) }
        let af = airFeats(t0, t1, cad: cad)
        let airOK = (af?.cover ?? 0) >= Self.airMinCover
        let running = airOK ? af!.lowgMs >= Self.runLowgMs : cad >= Self.runCad
        if running && v <= Self.walkOnlyKmh { return (.irregular, nil) }
        return (.ok, Point(v: v, limp: log(imp), cad: cad, amp: airOK ? af!.amp : nil, running: running))
    }

    /// 걸음 없음 판정에 쓰는 포락선 세기: 75% 값(impact의 봉우리 문턱과 같은 자리)
    static func envLevel(_ s: [Double]) -> Double {
        s.sorted()[Int(0.75 * Double(s.count - 1))]
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
        let walk = points.filter { !$0.running }
        walkA = walk.isEmpty ? nil : walk.map { $0.v - Self.walkB * $0.cad }.reduce(0, +) / Double(walk.count)
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
        updateStepThr(t1)
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
            kmh = (Self.walkOwn ? walkA ?? Self.walkA : Self.walkA) + Self.walkB * pf.cad
        } else if state == 0 {
            kmh = 0
        }
        if let k = kmh { hist.append((t1, min(max(k, 0), 25))) }
        hist.removeAll { $0.0 < t1 - Self.smoothS }
        let shown = kmh == nil ? nil : Self.median(hist.map { $0.1 })
        return Snapshot(t: t1, kmh: shown, kmhPhone: kmhPhone, state: state, byAirPods: byAir, airOK: airOK,
                        cad: pf?.cad, imp: pf?.imp, amp: af?.amp, lowgMs: af?.lowgMs,
                        nPoints: points.count, nRunPoints: points.filter { $0.running }.count, steps: steps)
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
