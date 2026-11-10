import Foundation

// 기록 CSV를 앱과 같은 SpeedEstimator에 다시 넣어 본다(계획 0024 2번). 맥에서:
//   ios/SpeedReplay/run.sh data/runs/20261029-040156/phone [--out a.csv]   → 0.5초마다 결과(기본 $TMPDIR/replay_swift.csv)
//   ios/SpeedReplay/run.sh <폴더> --compare                                → 그 폴더 estimate.csv(그날 폰이 낸 값)와 비교
//   ios/SpeedReplay/run.sh <폴더> --golden speedlab/golden/replay_<기록>.csv → Python 기준 구현(replay_live.py)과 비교
//     (계획 0024 2번 기준: 0.5초 칸마다 속도 차 0.01 km/h 이하, 걸음 상태·누적 걸음 같음)
//   ios/SpeedReplay/run.sh <폴더> --stages <v,t_begin,t_end CSV> [--expect <v,kind,result,steps CSV>]
//     → 폰 가속도만 넣으며 구간 끝마다 앱 단계 판정(measureStage). expect(Python live_stage_check 결과)와 비교
// 보정(calib) 구간이 끝날 때마다 단계 판정(measureStage)도 찍는다.
// 입력은 SpeedRecorder가 남긴 iphone_acc·airpods_acc·airpods_grav·stage.csv. 시각 순서로 섞어 넣는다(같은 시각이면 폰 → 에어팟 → 구간, replay_live.py와 같음)
// (앱은 받은 순서대로 넣었으므로 에어팟이 늦게 도착한 만큼 아주 조금 다를 수 있다).

struct Row { let t: Double; let v: [Double] }

func readCSV(_ path: String) -> [Row] {
    guard let text = try? String(contentsOfFile: path, encoding: .utf8) else { return [] }
    var out: [Row] = []
    for line in text.split(separator: "\n").dropFirst() {
        let f = line.split(separator: ",").map { Double($0.trimmingCharacters(in: .whitespaces)) ?? .nan }
        if let t = f.first, !t.isNaN { out.append(Row(t: t, v: Array(f.dropFirst()))) }
    }
    return out
}

let setName: [Int: String] = [0: "sync", 1: "calib", 2: "test", 3: "free", 4: "hop", 5: "warmup", 6: "still", 9: "end"]

var args = Array(CommandLine.arguments.dropFirst())
let compare = args.contains("--compare")
var outPath = (NSTemporaryDirectory() as NSString).appendingPathComponent("replay_swift.csv")
if let i = args.firstIndex(of: "--out"), i + 1 < args.count { outPath = args[i + 1]; args.removeSubrange(i...(i + 1)) }
var golden: String?, stagesPath: String?, expectPath: String?
if let i = args.firstIndex(of: "--stages"), i + 1 < args.count { stagesPath = args[i + 1]; args.removeSubrange(i...(i + 1)) }
if let i = args.firstIndex(of: "--expect"), i + 1 < args.count { expectPath = args[i + 1]; args.removeSubrange(i...(i + 1)) }
if let i = args.firstIndex(of: "--golden"), i + 1 < args.count { golden = args[i + 1]; args.removeSubrange(i...(i + 1)) }
args.removeAll { $0.hasPrefix("--") }
guard let dir = args.first else {
    FileHandle.standardError.write("사용: speed-replay <기록 폴더> [--compare]\n".data(using: .utf8)!)
    exit(2)
}
func file(_ n: String) -> String { (dir as NSString).appendingPathComponent(n) }

// 단계 판정만 보기: live_stage_check.py replay()와 같은 순서(샘플 하나 넣고 → 잴 창 시작이면 누적 걸음 기억 → 끝난 구간 판정)
if let stagesPath {
    let segs = readCSV(stagesPath).map { (v: $0.t, begin: $0.v[0], end: $0.v[1]) }.sorted { $0.end < $1.end }
    let starts = segs.indices.map { (t: segs[$0].begin + SpeedEstimator.calSkipS, i: $0) }.sorted { $0.t < $1.t }
    var expect: [(kind: String, result: String, steps: Int)] = []
    if let expectPath, let text = try? String(contentsOfFile: expectPath, encoding: .utf8) {
        for line in text.split(separator: "\n").dropFirst() {
            let f = line.split(separator: ",", omittingEmptySubsequences: false).map(String.init)
            expect.append((f[1], f[2], Int(Double(f[3]) ?? -1)))
        }
    }
    let est = SpeedEstimator()
    var j = 0, k = 0, stepsAt: [Int: Int] = [:], same = 0
    for r in readCSV(file("iphone_acc.csv")) {
        _ = est.addPhone(r.t, r.v[0], r.v[1], r.v[2])
        while j < starts.count && r.t >= starts[j].t { stepsAt[starts[j].i] = est.steps; j += 1 }
        while k < segs.count && r.t >= segs[k].end {
            let s = segs[k]
            let (res, _) = est.measureStage(s.v, begin: s.begin, end: s.end)
            let steps = est.steps - (stepsAt[k] ?? est.steps)
            var line = String(format: "%5.1f km/h  %-10@ 걸음 %4d", s.v, res.rawValue, steps)
            if k < expect.count {
                let e = expect[k]
                let ok = e.result == res.rawValue && e.steps == steps
                if ok { same += 1 }
                line += "  | Python \(e.kind) \(e.result) \(e.steps)" + (ok ? "" : "  ← 다름")
            }
            print(line)
            k += 1
        }
    }
    if !expect.isEmpty { print("Python과 같음 \(same)/\(segs.count)") }
    exit(0)
}

enum Event { case phone(Row), air(Double, Double, Double, Double), stage(Row) }
var events: [(t: Double, order: Int, e: Event)] = []
for r in readCSV(file("iphone_acc.csv")) { events.append((r.t, 0, .phone(r))) }
let acc = readCSV(file("airpods_acc.csv")), grav = readCSV(file("airpods_grav.csv"))
// airpods_acc(사용자 가속도) + airpods_grav(중력) = 앱이 넣은 전체 가속도. 두 파일은 같은 줄이 같은 샘플
for (a, g) in zip(acc, grav) where a.t == g.t {
    events.append((a.t, 1, .air(a.t, a.v[0] + g.v[0], a.v[1] + g.v[1], a.v[2] + g.v[2])))
}
for r in readCSV(file("stage.csv")) { events.append((r.t, 2, .stage(r))) }
events.sort { $0.t != $1.t ? $0.t < $1.t : $0.order < $1.order }

let est = SpeedEstimator()
var out: [[Double]] = []
var openCalib: (v: Double, t: Double)?
for ev in events {
    switch ev.e {
    case .phone(let r):
        if let s = est.addPhone(r.t, r.v[0], r.v[1], r.v[2]) {
            out.append([s.t, s.kmh ?? -1, s.kmhPhone ?? -1, Double(s.state), s.byAirPods ? 1 : 0, s.airOK ? 1 : 0,
                        s.cad ?? -1, s.imp ?? -1, s.amp ?? -1, s.lowgMs ?? -1, Double(s.nRunPoints), Double(s.steps)])
        }
    case .air(let t, let x, let y, let z):
        est.addAirPods(t, x, y, z)
    case .stage(let r):
        let speed = r.v[1] < 0 ? nil : r.v[1]
        if let c = openCalib {
            let (res, p) = est.measureStage(c.v, begin: c.t, end: r.t)
            print(String(format: "단계 %.0f km/h (%.1f초): ", c.v, r.t - c.t) + res.rawValue
                  + (p.map { String(format: " limp=%.4f cad=%.2f amp=%@ %@", $0.limp, $0.cad,
                                    $0.amp.map { String(format: "%.4f", $0) } ?? "nil", $0.running ? "달리기" : "걷기") } ?? ""))
        }
        openCalib = setName[Int(r.v[2])] == "calib" ? speed.map { ($0, r.t) } : nil
        est.beginStage(setName[Int(r.v[2])] ?? "?", speed: speed, t: r.t)
    }
}

let cols = ["t", "kmh", "kmh_phone", "state", "by_airpods", "air_ok", "cad", "imp", "amp", "lowg_ms", "n_run_points", "steps"]
var text = cols.joined(separator: ",") + "\n"
for r in out { text += r.map { String(format: "%.5f", $0) }.joined(separator: ",") + "\n" }
try! text.write(toFile: outPath, atomically: true, encoding: .utf8)
print("\(out.count)줄 → \(outPath)")
print("보정점: " + est.points.map { String(format: "v=%.0f limp=%.4f cad=%.2f amp=%@ %@", $0.v, $0.limp, $0.cad,
                                              $0.amp.map { String(format: "%.4f", $0) } ?? "nil", $0.running ? "달리기" : "걷기") }
                                     .joined(separator: " / "))

var byT: [String: [Double]] = [:]
for r in out { byT[String(format: "%.5f", r[0])] = Array(r.dropFirst()) }

// 다른 쪽 CSV(t, kmh, kmh_phone, state, ..., 값 없음 = -1)와 0.5초 칸마다 비교
func compareWith(_ path: String, _ label: String, columns: [(name: String, idx: Int)]) {
    let other = readCSV(path)
    guard !other.isEmpty else { print("\(label): \(path)를 못 읽음"); return }
    var matched = 0
    var lines: [String] = []
    var stats = columns.map { _ in (maxD: 0.0, at: 0.0, onlyOne: 0) }
    for r in other {
        guard let mine = byT[String(format: "%.5f", r.t)] else { continue }
        matched += 1
        for (k, c) in columns.enumerated() where c.idx < r.v.count {
            let a = r.v[c.idx], b = mine[c.idx]
            if (a < 0) != (b < 0) { stats[k].onlyOne += 1; continue }
            let d = abs(a - b)
            if d > stats[k].maxD { stats[k].maxD = d; stats[k].at = r.t - other[0].t }
        }
    }
    for (k, c) in columns.enumerated() {
        lines.append(String(format: "  %@ 최대 차 %.4f (%.1f초), 한쪽만 값 없음 %d칸", c.name, stats[k].maxD, stats[k].at, stats[k].onlyOne))
    }
    print("\(label): 그쪽 \(other.count)줄, 이쪽 \(out.count)줄, 시각이 맞는 줄 \(matched)")
    lines.forEach { print($0) }
}

let base: [(name: String, idx: Int)] = [("kmh", 0), ("kmh_phone", 1), ("state", 2), ("cad", 5), ("imp", 6), ("amp", 7), ("lowg_ms", 8)]
if compare { compareWith(file("estimate.csv"), "그날 폰(estimate.csv)", columns: base) }
if let golden { compareWith(golden, "Python 기준(\((golden as NSString).lastPathComponent))", columns: base + [("n_run_points", 9), ("steps", 10)]) }
