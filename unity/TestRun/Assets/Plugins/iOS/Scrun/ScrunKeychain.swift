import Foundation
import Security

// 로그인 토큰 같은 작은 비밀값 저장(C# ISecureStore). 키체인 일반 암호 항목, 이 기기에서만·첫 잠금 해제 뒤 읽힘
// (백업으로 다른 기기에 옮겨 가지 않음: 서버 refresh 토큰은 deviceId에 묶여 있다).
// 키체인은 앱을 지워도 남으므로, 새로 설치한 뒤 처음 쓸 때 예전 값을 지운다(지운 앱의 로그인이 되살아나지 않게).
enum ScrunKeychain {
    private static let service = (Bundle.main.bundleIdentifier ?? "com.scrun.app") + ".secure"
    private static let installedFlag = "scrun.keychain.installed"

    private static let wipeOnFreshInstall: Void = {
        let d = UserDefaults.standard
        if !d.bool(forKey: installedFlag) {
            SecItemDelete([kSecClass: kSecClassGenericPassword, kSecAttrService: service] as CFDictionary)
            d.set(true, forKey: installedFlag)
        }
    }()

    private static func base(_ key: String) -> [CFString: Any] {
        _ = wipeOnFreshInstall
        return [kSecClass: kSecClassGenericPassword, kSecAttrService: service, kSecAttrAccount: key]
    }

    static func get(_ key: String) throws -> String? {
        var q = base(key)
        q[kSecReturnData] = true
        q[kSecMatchLimit] = kSecMatchLimitOne
        var out: CFTypeRef?
        let s = SecItemCopyMatching(q as CFDictionary, &out)
        if s == errSecItemNotFound { return nil }
        guard s == errSecSuccess else { throw error("읽기", s) }
        guard let data = out as? Data, let str = String(data: data, encoding: .utf8) else {
            throw ScrunError("키체인 값을 글자로 읽지 못했습니다.")
        }
        return str
    }

    static func set(_ key: String, _ value: String) throws {
        let data = Data(value.utf8)
        let q = base(key)
        let attrs: [CFString: Any] = [
            kSecValueData: data,
            kSecAttrAccessible: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
        ]
        var s = SecItemUpdate(q as CFDictionary, attrs as CFDictionary)
        if s == errSecItemNotFound {
            var add = q
            for (k, v) in attrs { add[k] = v }
            s = SecItemAdd(add as CFDictionary, nil)
        }
        guard s == errSecSuccess else { throw error("저장", s) }
    }

    static func delete(_ key: String) throws {
        let s = SecItemDelete(base(key) as CFDictionary)
        guard s == errSecSuccess || s == errSecItemNotFound else { throw error("삭제", s) }
    }

    private static func error(_ what: String, _ s: OSStatus) -> ScrunError {
        let msg = SecCopyErrorMessageString(s, nil) as String? ?? ""
        return ScrunError("키체인 \(what) 실패(\(s)) \(msg)")
    }
}
