import Foundation
import UIKit

// C#(IosChannel.cs)과 Swift 사이의 통로. ADR-0009, 계획 0018의 5번.
//   C# → Swift: scrun_call(요청 번호, 이름, JSON) — 답은 나중에 콜백으로
//               scrun_query(이름, JSON) → JSON — 바로 답(상태 읽기·키체인), 받은 쪽이 scrun_free
//   Swift → C#: 콜백(요청 번호, JSON). 요청 번호 0은 이벤트 {"event": "...", ...}
// 답 모양: {"ok": true, "value": ...} 또는 {"ok": false, "error": "..."}
// 이름·값 문자열은 C# IosNative.cs와 짝이다. 바꾸면 양쪽을 같이 고친다.
// 센서(속도)는 이 통로를 쓰지 않는다(Swift가 속도까지 계산하고 Unity는 결과만 읽음, S2).

public typealias ScrunReplyCallback = @convention(c) (Int32, UnsafePointer<CChar>) -> Void

struct ScrunError: Error {
    let message: String
    init(_ message: String) { self.message = message }
}

enum ScrunBridge {
    // 모든 Swift 쪽 상태는 메인 스레드에서만 만진다(Unity도 아이폰에서는 메인 스레드에서 돈다).
    static var callback: ScrunReplyCallback?

    static func ok(_ reqId: Int32, _ value: Any?) {
        send(reqId, ["ok": true, "value": value ?? NSNull()])
    }

    static func fail(_ reqId: Int32, _ message: String) {
        send(reqId, ["ok": false, "error": message])
    }

    static func emit(_ event: String, _ fields: [String: Any]) {
        var o = fields
        o["event"] = event
        send(0, o)
    }

    private static func send(_ reqId: Int32, _ obj: [String: Any]) {
        guard let cb = callback else { return }
        encode(obj).withCString { cb(reqId, $0) }
    }

    static func encode(_ obj: [String: Any]) -> String {
        guard JSONSerialization.isValidJSONObject(obj),
              let data = try? JSONSerialization.data(withJSONObject: obj),
              let s = String(data: data, encoding: .utf8)
        else { return #"{"ok":false,"error":"Swift 답을 JSON으로 만들지 못했습니다."}"# }
        return s
    }

    static func args(_ json: UnsafePointer<CChar>?) -> [String: Any] {
        guard let json = json,
              let data = String(cString: json).data(using: .utf8),
              let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return [:] }
        return o
    }

    static func onMain(_ work: @escaping () -> Void) {
        if Thread.isMainThread { work() } else { DispatchQueue.main.async(execute: work) }
    }

    static func string(_ a: [String: Any], _ key: String) throws -> String {
        guard let v = a[key] as? String else { throw ScrunError("'\(key)' 값이 없습니다.") }
        return v
    }

    static func permissionKind(_ a: [String: Any]) throws -> ScrunPermissionKind {
        let s = try string(a, "kind")
        guard let k = ScrunPermissionKind(rawValue: s) else { throw ScrunError("모르는 권한: \(s)") }
        return k
    }
}

// ---- C#이 부르는 함수 ----

@_cdecl("scrun_set_callback")
public func scrun_set_callback(_ cb: ScrunReplyCallback?) {
    ScrunBridge.onMain {
        ScrunBridge.callback = cb
        ScrunPermissions.shared.start()
        ScrunHeadphones.shared.start()
    }
}

@_cdecl("scrun_call")
public func scrun_call(_ reqId: Int32, _ name: UnsafePointer<CChar>, _ json: UnsafePointer<CChar>) {
    // 포인터는 이 호출 동안만 유효하니 먼저 복사한다.
    let n = String(cString: name)
    let a = ScrunBridge.args(json)
    ScrunBridge.onMain {
        do {
            switch n {
            case "permissions.request":
                ScrunPermissions.shared.request(try ScrunBridge.permissionKind(a)) { status in
                    ScrunBridge.ok(reqId, status.rawValue)
                }
            case "kakao.login":
                ScrunKakao.login { result in
                    switch result {
                    case .success(let token): ScrunBridge.ok(reqId, token) // 취소면 nil → null
                    case .failure(let e): ScrunBridge.fail(reqId, e.message)
                    }
                }
            default:
                throw ScrunError("모르는 요청: \(n)")
            }
        } catch let e as ScrunError {
            ScrunBridge.fail(reqId, e.message)
        } catch {
            ScrunBridge.fail(reqId, "\(error)")
        }
    }
}

@_cdecl("scrun_query")
public func scrun_query(_ name: UnsafePointer<CChar>, _ json: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>? {
    let n = String(cString: name)
    let a = ScrunBridge.args(json)
    var reply: [String: Any]
    do {
        reply = ["ok": true, "value": try scrunQuery(n, a) ?? NSNull()]
    } catch let e as ScrunError {
        reply = ["ok": false, "error": e.message]
    } catch {
        reply = ["ok": false, "error": "\(error)"]
    }
    return strdup(ScrunBridge.encode(reply))
}

@_cdecl("scrun_free")
public func scrun_free(_ p: UnsafeMutablePointer<CChar>?) {
    free(p)
}

// 바로 답하는 것들. C#은 메인 스레드에서 부른다.
private func scrunQuery(_ name: String, _ a: [String: Any]) throws -> Any? {
    switch name {
    case "permissions.status":
        return ScrunPermissions.shared.status(try ScrunBridge.permissionKind(a)).rawValue
    case "app.openSettings":
        if let url = URL(string: UIApplication.openSettingsURLString) {
            UIApplication.shared.open(url)
        }
        return nil
    case "headphones.status":
        let h = ScrunHeadphones.shared.current()
        return ["status": h.status, "name": h.name as Any? ?? NSNull()] as [String: Any]
    case "headphones.watch":
        ScrunHeadphones.shared.setWatching(a["on"] as? Bool ?? false)
        return nil
    case "secure.get":
        return try ScrunKeychain.get(try ScrunBridge.string(a, "key"))
    case "secure.set":
        try ScrunKeychain.set(try ScrunBridge.string(a, "key"), try ScrunBridge.string(a, "value"))
        return nil
    case "secure.delete":
        try ScrunKeychain.delete(try ScrunBridge.string(a, "key"))
        return nil
    default:
        throw ScrunError("모르는 조회: \(name)")
    }
}
