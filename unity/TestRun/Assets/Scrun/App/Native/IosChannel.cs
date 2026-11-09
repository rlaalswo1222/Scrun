#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using AOT;

namespace Scrun.App
{
    // 아이폰 빌드에서만: Swift가 @_cdecl로 내보낸 함수(ScrunBridge.swift)를 부르는 진짜 통로.
    // 문자열은 UTF-8 바이트로 직접 넘긴다(IL2CPP 기본 문자열 변환에 기대지 않음).
    internal sealed class IosChannel : INativeChannel
    {
        delegate void ReplyCallback(int reqId, IntPtr json);

        [DllImport("__Internal")] static extern void scrun_set_callback(ReplyCallback cb);
        [DllImport("__Internal")] static extern void scrun_call(int reqId, byte[] name, byte[] json);
        [DllImport("__Internal")] static extern IntPtr scrun_query(byte[] name, byte[] json);
        [DllImport("__Internal")] static extern void scrun_free(IntPtr p);

        static NativeBridge bridge;
        static IosNative instance;
        static SynchronizationContext main; // Unity 메인 스레드(플레이어 루프에서 Post를 실행)
        static readonly ReplyCallback replyCallback = OnReply; // 대리자가 GC에 지워지지 않게 붙잡아 둠

        // Native.cs가 처음 쓸 때(메인 스레드) 만든다. 다 준비된 뒤에 instance를 넣어, 중간에 실패하면 다음에 다시 시도한다.
        public static IosNative Instance
        {
            get
            {
                if (instance != null) return instance;
                main = SynchronizationContext.Current
                       ?? throw new InvalidOperationException("Native는 Unity 메인 스레드에서 처음 불러야 합니다.");
                var b = new NativeBridge(new IosChannel());
                var n = new IosNative(b);
                bridge = b;
                scrun_set_callback(replyCallback);
                instance = n;
                return instance;
            }
        }

        public void Call(int reqId, string name, string json) => scrun_call(reqId, Utf8(name), Utf8(json));

        public string Query(string name, string json)
        {
            var p = scrun_query(Utf8(name), Utf8(json));
            if (p == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(p);
            }
            finally
            {
                scrun_free(p);
            }
        }

        // Swift가 부른다(아무 스레드). 포인터는 이 호출 동안만 살아 있으니 바로 문자열로 복사해 큐에 넣고,
        // 메인 스레드에서 큐를 비우게 한다(Swift 안에서 바로 C# 화면 코드가 돌지 않게 늘 다음 차례로 미룸).
        [MonoPInvokeCallback(typeof(ReplyCallback))]
        static void OnReply(int reqId, IntPtr json)
        {
            bridge.Post(reqId, Marshal.PtrToStringUTF8(json));
            main.Post(_ => bridge.Drain(), null);
        }

        static byte[] Utf8(string s)
        {
            int n = Encoding.UTF8.GetByteCount(s ?? "");
            var b = new byte[n + 1]; // 끝의 0
            Encoding.UTF8.GetBytes(s ?? "", 0, (s ?? "").Length, b, 0);
            return b;
        }
    }
}
#endif
