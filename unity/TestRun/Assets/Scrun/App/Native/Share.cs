using UnityEngine;

namespace Scrun.App
{
    // 글 복사·공유(G-05 내 친구 코드, 계획 0044).
    //   Native.Share.Copy(code);                       // 클립보드
    //   bool sent = await Native.Share.ShareTextAsync(msg); // iOS 공유 시트(UIActivityViewController)
    // 아이폰 공유는 Swift(ScrunBridge.swift "app.share"), 에디터는 복사만 하고 false(공유 시트가 없음).
    public interface IShare
    {
        void Copy(string text);

        // 공유 시트를 띄우고 닫힐 때 답. 앱을 골라 보냈으면 true, 취소면 false
        Awaitable<bool> ShareTextAsync(string text);
    }

    // 에디터·가짜: 복사는 진짜 클립보드, 공유는 복사로 대신하고 로그
    internal sealed class FakeShare : IShare
    {
        public static readonly FakeShare Instance = new FakeShare();

        public string LastShared { get; private set; }

        public void Copy(string text) => GUIUtility.systemCopyBuffer = text ?? "";

        public Awaitable<bool> ShareTextAsync(string text)
        {
            LastShared = text;
            Copy(text);
            Debug.Log("[Share] 에디터에는 공유 시트가 없어 클립보드에 복사함: " + text);
            var done = new AwaitableCompletionSource<bool>();
            done.SetResult(false);
            return done.Awaitable;
        }
    }
}
