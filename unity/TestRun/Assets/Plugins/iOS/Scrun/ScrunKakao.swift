import Foundation
import UIKit
#if canImport(KakaoSDKUser)
import KakaoSDKAuth
import KakaoSDKCommon
import KakaoSDKUser
#endif

// 카카오 로그인(C# IKakaoLogin). 성공하면 카카오 access 토큰, 사용자가 취소하면 nil.
// 카카오 iOS SDK(SPM)와 앱 키(Info.plist KAKAO_APP_KEY)는 빌드 후처리(IosPostprocess)가 넣는다.
// 로컬 설정에 앱 키가 없으면 SDK 없이 빌드되고, 로그인은 "SDK 없음" 오류를 준다(계획 0018의 6번).
//
// 순서: 카카오톡이 깔려 있으면(kakaokompassauth 스킴) 카카오톡으로 로그인, 없거나 카카오톡을 열지 못하면(NotSupported)
// 카카오계정(웹) 로그인. 카카오톡 로그인의 다른 실패(네트워크 등)는 오류로 돌려준다(화면이 "다시 시도"를 띄움).
// 카카오톡에서 로그인 없이 돌아오면 SDK가 아무 답도 주지 않으므로, 앱으로 돌아온 뒤 2초 안에
// 카카오 URL이 오지 않으면 취소로 본다(로그인 버튼이 잠긴 채 남지 않게).
enum ScrunKakao {
    typealias Done = (Result<String?, ScrunError>) -> Void

    #if canImport(KakaoSDKUser)
    private static var ready = false
    private static var pending: Done?
    private static var attempt = 0 // 로그인 시도 번호(지난 시도의 타이머가 다음 시도를 끝내지 않게)
    private static var wentToBackground = false
    private static var urlArrived = false
    private static var observers: [NSObjectProtocol] = []
    #endif

    // 앱 시작 때 한 번(ScrunBridge). 카카오톡에서 돌아오는 URL을 받아 SDK에 넘긴다.
    static func setup() {
        #if canImport(KakaoSDKUser)
        guard !ready,
              let key = Bundle.main.object(forInfoDictionaryKey: "KAKAO_APP_KEY") as? String,
              !key.isEmpty else { return }
        KakaoSDK.initSDK(appKey: key)
        // Unity가 앱으로 들어온 URL을 이 이름의 알림으로 뿌린다(Classes/PluginBase/AppDelegateListener, userInfo["url"])
        NotificationCenter.default.addObserver(forName: Notification.Name("kUnityOnOpenURL"),
                                               object: nil, queue: .main) { n in
            guard let url = n.userInfo?["url"] as? URL, AuthApi.isKakaoTalkLoginUrl(url) else { return }
            urlArrived = true
            Task { @MainActor in _ = AuthController.handleOpenUrl(url: url) }
        }
        ready = true
        #endif
    }

    static func login(_ done: @escaping Done) {
        #if canImport(KakaoSDKUser)
        guard ready else {
            done(.failure(ScrunError("카카오 앱 키가 이 빌드에 없습니다(ScrunLocal.json의 kakaoNativeAppKey).")))
            return
        }
        guard pending == nil else {
            done(.failure(ScrunError("카카오 로그인이 이미 진행 중입니다.")))
            return
        }
        pending = done
        attempt += 1
        // 카카오톡이 없으면 바로 카카오계정(웹). SDK 기본(유니버설 링크)은 카카오톡이 없을 때 Safari로 열려
        // "열기 성공"이 되므로 NotSupported로 알 수 없다(11/10 시뮬레이터). 스킴은 Info.plist 허용 목록에 있음
        guard let talk = URL(string: "kakaokompassauth://authorize"), UIApplication.shared.canOpenURL(talk) else {
            loginWithAccount()
            return
        }
        watchReturnFromTalk()
        UserApi.shared.loginWithKakaoTalk { token, error in
            if let e = error as? SdkError, e.isClientFailed, e.getClientError().reason == .NotSupported {
                // 카카오톡을 열지 못함(설치됐지만 실행 실패 등) → 카카오계정(웹)
                loginWithAccount()
                return
            }
            finish(token?.accessToken, error)
        }
        #else
        done(.failure(ScrunError("카카오 SDK가 이 빌드에 없습니다(ScrunLocal.json의 kakaoNativeAppKey를 넣고 다시 빌드).")))
        #endif
    }

    #if canImport(KakaoSDKUser)
    // 웹 로그인 창은 앱 안에 떠서 "카카오톡에서 돌아옴" 감시는 끈다
    private static func loginWithAccount() {
        stopWatching()
        UserApi.shared.loginWithKakaoAccount { token, error in finish(token?.accessToken, error) }
    }

    private static func finish(_ accessToken: String?, _ error: Error?) {
        stopWatching()
        guard let done = pending else { return } // 이미 취소로 끝냄(늦게 온 답은 버림)
        pending = nil
        if let token = accessToken {
            done(.success(token))
        } else if let error = error {
            if isCancel(error) {
                done(.success(nil))
            } else {
                done(.failure(ScrunError("카카오 로그인 실패: \(error.localizedDescription)")))
            }
        } else {
            done(.failure(ScrunError("카카오 로그인이 토큰을 주지 않았습니다.")))
        }
    }

    private static func isCancel(_ error: Error) -> Bool {
        guard let e = error as? SdkError else { return false }
        if e.isClientFailed { return e.getClientError().reason == .Cancelled }
        if e.isAuthFailed { return e.getAuthError().reason == .AccessDenied } // 동의 화면에서 취소
        return false
    }

    // 카카오톡으로 넘어갔다(배경으로 감) 돌아왔는데 URL이 안 오면 취소
    private static func watchReturnFromTalk() {
        stopWatching()
        wentToBackground = false
        urlArrived = false
        let c = NotificationCenter.default
        observers.append(c.addObserver(forName: UIApplication.didEnterBackgroundNotification, object: nil, queue: .main) { _ in
            wentToBackground = true
        })
        let mine = attempt
        observers.append(c.addObserver(forName: UIApplication.didBecomeActiveNotification, object: nil, queue: .main) { _ in
            guard wentToBackground else { return }
            DispatchQueue.main.asyncAfter(deadline: .now() + 2.0) {
                if attempt == mine, pending != nil, !urlArrived { finish(nil, SdkError(reason: .Cancelled)) }
            }
        })
    }

    private static func stopWatching() {
        for o in observers { NotificationCenter.default.removeObserver(o) }
        observers.removeAll()
    }
    #endif
}
