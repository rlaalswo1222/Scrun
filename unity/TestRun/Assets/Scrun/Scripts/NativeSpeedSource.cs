using System;
using System.Globalization;
using System.IO;
using Scrun.App;
using UnityEngine;

namespace Scrun
{
    // Native.Speed(에디터는 가짜 센서, 아이폰은 Swift 속도 플러그인)를 RunnerController에 꽂는 어댑터.
    // 속도는 AvatarSpeed를 거친다(계획 0034): 걸음이 1.2초 없으면 바로 부드럽게 멈추고, 다시 걸으면 바로 출발.
    // 에디터에서는 Fake Native 창이나 키보드 ↑/↓·Space로 속도를 바꾼다
    // (같은 씬에 FakeSpeedSource를 같이 두지 않는다: 키가 둘 다 먹음).
    // 센서가 꺼져 있으면 켜고, 꺼질 때는 자기가 켠 경우에만 끈다(센서 준비 화면이 켠 센서를 빌려 쓰는 경우).
    public class NativeSpeedSource : MonoBehaviour, ISpeedSource
    {
        ISpeedSensor sensor;
        bool startedHere;
        readonly AvatarSpeed avatar = new AvatarSpeed();
        int frame = -1;

        public float SpeedKmh
        {
            get
            {
                Tick();
                return sensor == null ? 0f : avatar.SpeedKmh;
            }
        }

        // 읽는 쪽이 없어도(달리기 전 GatedSpeed가 안 읽음) 매 프레임 한 번 센서를 따라가 둔다
        void Update() => Tick();

        void Tick()
        {
            if (sensor == null || frame == Time.frameCount) return;
            frame = Time.frameCount;
            avatar.Update(sensor.Current, sensor.Phone == SensorFlow.Ok);
        }

        void OnEnable()
        {
            avatar.Reset();
            avatar.DelayMeasured += LogDelay;
            // 진짜 구현이 아직 없는 빌드에선 Native가 예외를 던진다 → 한 번만 알리고 속도 0(매 프레임 예외 안 남)
            try { sensor = Native.Speed; }
            catch (Exception e)
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
            avatar.DelayMeasured -= LogDelay;
            if (startedHere && sensor != null) sensor.Stop();
            startedHere = false;
        }

        // 반영 지연(BL-13 ③). 개발 빌드는 Documents/run-log/avatar-delays.csv에 쌓음(맥에서 devicectl로 받음)
        static void LogDelay(AvatarDelay d)
        {
            string kind = d.Kind == AvatarDelayKind.Stop ? "멈춤" : "출발";
            Debug.Log($"[아바타 지연] {kind} {d.Sec:0.00}초 (센서 {d.At:0.0}초)");
            if (Application.isEditor || !Debug.isDebugBuild) return;
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "run-log");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "avatar-delays.csv");
                if (!File.Exists(file)) File.WriteAllText(file, "time,kind,sec,sensor_t\n");
                // 지역 설정이 쉼표 소수점이어도 CSV가 깨지지 않게
                File.AppendAllText(file, string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-ddTHH:mm:ss},{1},{2:0.000},{3:0.00}\n",
                    DateTime.Now, d.Kind == AvatarDelayKind.Stop ? "stop" : "start", d.Sec, d.At));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[아바타 지연] 기록 실패: " + e.Message);
            }
        }
    }
}
