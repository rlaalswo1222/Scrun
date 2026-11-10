using Scrun.App;
using UnityEngine;

namespace Scrun
{
    // Native.Speed(에디터는 가짜 센서, 아이폰은 Swift 속도 플러그인)를 RunnerController에 꽂는 어댑터.
    // 걸음이 안 잡히면 0. 에디터에서는 Fake Native 창이나 키보드 ↑/↓·Space로 속도를 바꾼다
    // (같은 씬에 FakeSpeedSource를 같이 두지 않는다: 키가 둘 다 먹음).
    // 센서가 꺼져 있으면 켜고, 꺼질 때는 자기가 켠 경우에만 끈다(센서 준비 화면이 켠 센서를 빌려 쓰는 경우).
    public class NativeSpeedSource : MonoBehaviour, ISpeedSource
    {
        ISpeedSensor sensor;
        bool startedHere;

        public float SpeedKmh
        {
            get
            {
                if (sensor == null) return 0f;
                var r = sensor.Current;
                return r.HasSpeed ? r.SpeedKmh : 0f;
            }
        }

        void OnEnable()
        {
            // 진짜 구현이 아직 없는 빌드에선 Native가 예외를 던진다 → 한 번만 알리고 속도 0(매 프레임 예외 안 남)
            try { sensor = Native.Speed; }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
                sensor = null;
                return;
            }
            if (!sensor.IsRunning)
            {
                sensor.Start();
                startedHere = true;
            }
        }

        void OnDisable()
        {
            if (startedHere && sensor != null) sensor.Stop();
            startedHere = false;
        }
    }
}
