using UnityEngine;
using UnityEngine.InputSystem;

namespace Scrun
{
    // 키보드로 속도 흉내: ↑/↓ = ±0.5 km/h, Space = 정지
    public class FakeSpeedSource : MonoBehaviour, ISpeedSource
    {
        [SerializeField] float startKmh = 0f;
        [SerializeField] float stepKmh = 0.5f;
        [SerializeField] float maxKmh = 20f;

        public float SpeedKmh { get; private set; }

        void Awake() => SpeedKmh = startKmh;

        // 터치 버튼(폰 테스트용)에서 호출
        public void Add(float kmh) => SpeedKmh = Mathf.Clamp(SpeedKmh + kmh, 0f, maxKmh);
        public void Stop() => SpeedKmh = 0f;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.upArrowKey.wasPressedThisFrame) SpeedKmh += stepKmh;
            if (kb.downArrowKey.wasPressedThisFrame) SpeedKmh -= stepKmh;
            if (kb.spaceKey.wasPressedThisFrame) SpeedKmh = 0f;

            SpeedKmh = Mathf.Clamp(SpeedKmh, 0f, maxKmh);
        }
    }
}
