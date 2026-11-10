#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Scrun.App
{
    // 아이폰 빌드에서만: Swift ScrunSpeed.swift가 @_cdecl로 내보낸 속도 함수(계획 0024 "Swift ↔ C# 약속").
    // 매 프레임 읽기는 숫자 배열(문자열·JSON 없음), 드문 보정 호출만 작은 JSON. 문자열은 UTF-8 바이트로 직접 넘긴다(ADR-0009).
    internal sealed class IosSpeedNative : ISpeedNativeApi
    {
        [DllImport("__Internal")] static extern void scrun_speed_start();
        [DllImport("__Internal")] static extern void scrun_speed_stop();
        [DllImport("__Internal")] static extern int scrun_speed_read([Out] double[] slots, int n);
        [DllImport("__Internal")] static extern void scrun_speed_stage_begin(double kmh);
        [DllImport("__Internal")] static extern IntPtr scrun_speed_stage_end();
        [DllImport("__Internal")] static extern void scrun_speed_stage_cancel();
        [DllImport("__Internal")] static extern int scrun_speed_load(byte[] modelJson);
        [DllImport("__Internal")] static extern void scrun_free(IntPtr p);

        public void Start() => scrun_speed_start();
        public void Stop() => scrun_speed_stop();
        public int Read(double[] slots) => scrun_speed_read(slots, slots.Length);
        public void StageBegin(double kmh) => scrun_speed_stage_begin(kmh);
        public void StageCancel() => scrun_speed_stage_cancel();
        public bool Load(string modelJson) => scrun_speed_load(modelJson == null ? null : Utf8(modelJson)) == 1;

        public string StageEnd()
        {
            var p = scrun_speed_stage_end();
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(p); }
            finally { scrun_free(p); }
        }

        static byte[] Utf8(string s)
        {
            var n = Encoding.UTF8.GetByteCount(s);
            var b = new byte[n + 1]; // 끝 0
            Encoding.UTF8.GetBytes(s, 0, s.Length, b, 0);
            return b;
        }
    }
}
#endif
