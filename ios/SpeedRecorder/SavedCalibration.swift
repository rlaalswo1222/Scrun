import Foundation

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
