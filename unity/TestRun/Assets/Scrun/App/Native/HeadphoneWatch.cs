using System;
using UnityEngine;

namespace Scrun.App
{
    // 에어팟 모션 연결은 누군가 Native.Headphones.Changed를 구독하는 동안에만 iOS가 지켜본다(모션 업데이트 = 배터리,
    // 구독자가 없으면 에어팟도 ConnectedNoMotion으로 보임, unity_map.md "앱 코드 묶음"). 상태는 Status로 읽고, 이건 지켜보게만 한다.
    // 맵 화면이 입장부터 나갈 때까지 들고 있다: D-07 센서 확인은 1.5초 뒤 저절로 넘어가므로 거기서 구독하면 늦음(계획 0035 리뷰)
    public sealed class HeadphoneWatch : IDisposable
    {
        bool watching;

        public HeadphoneWatch()
        {
            try
            {
                Native.Headphones.Changed += OnChanged;
                watching = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[이어폰] 상태를 지켜보지 못함(이어폰 없이 진행으로 보임): " + e.Message);
            }
        }

        public void Dispose()
        {
            if (!watching) return;
            watching = false;
            try { Native.Headphones.Changed -= OnChanged; }
            catch (Exception e) { Debug.LogWarning("[이어폰] 구독을 빼지 못함: " + e.Message); }
        }

        static void OnChanged(HeadphoneStatus _) { }
    }
}
