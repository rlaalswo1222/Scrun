import UIKit

// 글 공유 시트(G-05 내 친구 코드 공유, 계획 0044). iOS 기본 UIActivityViewController를 맨 위 화면에 띄우고,
// 시트가 닫히면 보냈는지(앱을 골라 끝까지 감)를 한 번만 돌려준다. 아이패드는 시트가 팝오버라 띄울 자리(가운데 아래)를 정해 준다.
//
// 완료 콜백은 여러 번 올 수 있다: 공유 확장(메시지 작성 등)에서 취소하면 (그 확장, false)로 불리고 시트는 그대로 열려 있음.
// 시트가 정말 닫힐 때는 보냈으면 completed=true, 그냥 닫았으면 activityType=nil. 그때만 답한다(리뷰).
// 띄우기가 조용히 실패하면(맨 위 화면이 막 닫히는 중) 콜백이 영영 안 와서 버튼이 잠기므로, 1초 뒤 떠 있지 않으면 false로 답한다.
enum ScrunShare {
    private static var pending: ((Bool) -> Void)?
    private static var current: UIActivityViewController? // 지금 답을 기다리는 시트(답하면 놓음. 띄우기 실패 때도 남아 있어야 1초 뒤 확인 가능)

    static func share(_ text: String, done: @escaping (Bool) -> Void) throws {
        guard !text.isEmpty else { throw ScrunError("공유할 글이 비었습니다.") }
        guard pending == nil else { done(false); return } // 두 번 눌림: 앞 시트가 아직 열려 있음
        guard let top = ScrunBridge.topViewController() else { throw ScrunError("공유 시트를 띄울 화면이 없습니다.") }
        let vc = UIActivityViewController(activityItems: [text], applicationActivities: nil)
        pending = done
        current = vc
        vc.completionWithItemsHandler = { activity, completed, _, _ in
            if !completed && activity != nil { return } // 확장만 닫힘, 시트는 아직 열려 있음
            finish(completed)
        }
        if let pop = vc.popoverPresentationController {
            pop.sourceView = top.view
            pop.sourceRect = CGRect(x: top.view.bounds.midX, y: top.view.bounds.maxY - 80, width: 0, height: 0)
            pop.permittedArrowDirections = []
        }
        top.present(vc, animated: true)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) { [weak vc] in
            // 이 시트가 아직 답을 기다리는데 떠 있지 않음 = 띄우기 실패. 그 사이 끝나고 새 시트가 떴으면 건드리지 않음
            guard let vc = vc, current === vc, vc.presentingViewController == nil else { return }
            finish(false)
        }
    }

    private static func finish(_ sent: Bool) {
        guard let done = pending else { return }
        pending = nil
        current = nil
        done(sent)
    }
}
