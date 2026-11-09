using System;
using UnityEngine;

namespace Scrun.App
{
    // 걸음 상태. Swift SpeedEstimator의 state 0·1·2와 같다.
    public enum Gait
    {
        None,    // 걸음 없음(벨트 옆 발판에 서 있음, 폰 데이터 없음)
        Walking,
        Running,
    }

    // 센서 데이터가 들어오는지.
    public enum SensorFlow
    {
        Off,     // 꺼짐(센서를 안 켰음, 에어팟은 연결·권한이 없어 안 씀)
        Waiting, // 켰는데 아직 안 옴
        Ok,      // 들어옴
        Lost,    // 2초 넘게 안 옴(처음부터 안 오거나 도중에 끊김)
    }

    // 한 프레임에 한 번 읽는 속도 추정 결과. 숫자는 Swift가 계산하고 C#은 읽기만 한다(계획 0020).
    public struct SpeedReading
    {
        public double Time;       // 센서를 켠 뒤 초
        public bool HasSpeed;     // 걸음이 잡혀 속도가 있음(Gait가 None이면 false)
        public float SpeedKmh;    // HasSpeed일 때만 의미 있음
        public Gait Gait;
        public float CadenceSpm;  // 분당 걸음 수, 걸음 없으면 0
        public int Steps;         // 센서를 켠 뒤 걸음 수. 감지 지연 없이 바로 늘어남(다리 애니메이션용, BL-13)
        public bool UsingAirPods; // 이번 값에 에어팟 데이터를 섞었는지
    }

    // 러닝머신 속도 센서. 화면은 Native.Speed로 쓴다.
    //   Native.Speed.Start();                       // 센서 준비 화면에 들어올 때
    //   Native.Speed.GaitChanged += g => ...;       // 걸음이 잡히면 카운트다운
    //   var r = Native.Speed.Current;               // 매 프레임 읽기
    //   Native.Speed.Stop();                        // 달리기 끝·취소(이벤트 없이 꺼짐으로)
    // 이벤트와 값은 메인 스레드에서, 다른 Update보다 먼저 한 프레임에 한 번 갱신된다.
    // 센서는 앱에 하나라 켠 쪽이 끈다. 이미 켜져 있으면 Start는 아무것도 안 한다.
    public interface ISpeedSensor
    {
        bool IsRunning { get; }
        void Start();
        void Stop();

        SpeedReading Current { get; }
        SensorFlow Phone { get; }   // 폰 가속도
        SensorFlow AirPods { get; } // 헤드폰 모션(연결·권한은 IHeadphones·IPermissions에서 따로 봄)
        bool HasCalibration { get; } // 보정점이 있음. 없으면 보정 화면으로

        event Action<Gait> GaitChanged;
        event Action FlowChanged; // Phone·AirPods 중 하나가 바뀜
    }

    // 켜기·끄기, 프레임마다 읽기, 바뀐 것만 이벤트로 알리기는 진짜(아이폰)와 가짜(에디터)가 같으므로 여기 둔다.
    // 구현은 Sample()에서 지금 값만 채운다.
    public abstract class SpeedSensorBase : ISpeedSensor
    {
        public bool IsRunning { get; private set; }
        public SpeedReading Current { get; private set; }
        public SensorFlow Phone { get; private set; }
        public SensorFlow AirPods { get; private set; }
        public abstract bool HasCalibration { get; }

        public event Action<Gait> GaitChanged;
        public event Action FlowChanged;

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            OnStart();
            SpeedSensorPump.Attach(this);
            Tick();
        }

        // 일부러 끄는 것이라 이벤트는 보내지 않는다("걸음 없음"·"끊김"과 헷갈리지 않게). 값만 꺼짐으로 돌아간다.
        // 센서는 하나를 같이 쓰므로, 켠 쪽이 끈다(이미 켜져 있던 걸 빌려 쓴 쪽은 끄지 않음).
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            OnStop();
            Current = default;
            Phone = SensorFlow.Off;
            AirPods = SensorFlow.Off;
        }

        // 프레임마다(SpeedSensorPump) 부른다. 테스트는 직접 부른다.
        internal void Tick()
        {
            if (!IsRunning) return;
            Sample(out var reading, out var phone, out var airPods);
            var oldGait = Current.Gait;
            bool flowChanged = phone != Phone || airPods != AirPods;
            Current = reading;
            Phone = phone;
            AirPods = airPods;
            if (flowChanged) FlowChanged?.Invoke();
            if (reading.Gait != oldGait) GaitChanged?.Invoke(reading.Gait);
        }

        // Play를 새로 시작할 때(도메인 리로드가 꺼져 있어 정적 객체가 남음) 지난 Play의 상태와 구독자를 지운다.
        protected void ResetForPlay()
        {
            GaitChanged = null;
            FlowChanged = null;
            IsRunning = false;
            Current = default;
            Phone = SensorFlow.Off;
            AirPods = SensorFlow.Off;
            OnResetForPlay();
        }

        protected virtual void OnResetForPlay() { }

        protected abstract void OnStart();
        protected abstract void OnStop();
        protected abstract void Sample(out SpeedReading reading, out SensorFlow phone, out SensorFlow airPods);
    }

    // 센서를 켠 동안 매 프레임 Tick을 부르는 숨은 오브젝트. 다른 스크립트의 Update보다 먼저 돌아서
    // 한 프레임 안에서는 모든 화면이 같은 값을 본다.
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("")]
    sealed class SpeedSensorPump : MonoBehaviour
    {
        static SpeedSensorPump instance;
        static SpeedSensorBase target;

        internal static void Attach(SpeedSensorBase sensor)
        {
            target = sensor;
            if (!Application.isPlaying || instance) return; // 에디트 모드(테스트)는 Tick을 직접 부름
            var go = new GameObject("[SpeedSensorPump]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go); // Play가 끝나면 같이 사라진다
            instance = go.AddComponent<SpeedSensorPump>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            instance = null;
            target = null;
        }

        void Update() => target?.Tick();
    }
}
