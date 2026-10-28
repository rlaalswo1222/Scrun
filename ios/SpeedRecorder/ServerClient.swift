import Foundation

/// 노트북 서버(server.py)와 통신. 시계 차이를 재서 모든 샘플을 노트북 시계로 맞춘다.
final class ServerClient {
    var baseURL: URL
    /// 노트북 시계 - 이 폰 시계 (초)
    private(set) var offset: Double = 0
    private var bestRTT = Double.infinity

    init(baseURL: URL) { self.baseURL = baseURL }

    static func localNow() -> Double { Date().timeIntervalSince1970 }
    func serverNow() -> Double { ServerClient.localNow() + offset }

    /// 다섯 번 왕복해서 가장 빠른 왕복의 가운데로 시계 차이를 잡는다
    @discardableResult
    func syncClock() async -> Bool {
        var ok = false
        for _ in 0..<5 {
            let a = ServerClient.localNow()
            guard let obj = try? await get("api/speed/clock"), let t = obj["t"] as? Double else { continue }
            let b = ServerClient.localNow()
            if b - a < bestRTT {
                bestRTT = b - a
                offset = t - (a + b) / 2
            }
            ok = true
        }
        return ok
    }

    var roundTripMs: Int { bestRTT.isFinite ? Int(bestRTT * 1000) : -1 }

    func get(_ path: String) async throws -> [String: Any] {
        var req = URLRequest(url: baseURL.appendingPathComponent(path))
        req.timeoutInterval = 3
        req.cachePolicy = .reloadIgnoringLocalCacheData
        let (data, resp) = try await URLSession.shared.data(for: req)
        try check(resp)
        return (try JSONSerialization.jsonObject(with: data) as? [String: Any]) ?? [:]
    }

    @discardableResult
    func post(_ path: String, _ body: Any, timeout: TimeInterval = 5) async throws -> [String: Any] {
        var req = URLRequest(url: baseURL.appendingPathComponent(path))
        req.httpMethod = "POST"
        req.timeoutInterval = timeout
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (data, resp) = try await URLSession.shared.data(for: req)
        try check(resp)
        return (try? JSONSerialization.jsonObject(with: data) as? [String: Any]) ?? [:]
    }

    private func check(_ resp: URLResponse) throws {
        if let http = resp as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
            throw URLError(.badServerResponse)
        }
    }
}
