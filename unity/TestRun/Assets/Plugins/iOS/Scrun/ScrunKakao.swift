import Foundation

// 카카오 로그인(C# IKakaoLogin). 카카오 iOS SDK는 계획 0018의 6번에서 빌드 후처리로 넣는다.
// 성공하면 카카오 access 토큰, 사용자가 취소하면 nil.
enum ScrunKakao {
    static func login(_ done: @escaping (Result<String?, ScrunError>) -> Void) {
        done(.failure(ScrunError("카카오 SDK가 아직 이 빌드에 없습니다(계획 0018의 6번).")))
    }
}

