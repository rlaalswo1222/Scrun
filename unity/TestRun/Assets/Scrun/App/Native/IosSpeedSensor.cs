using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scrun.App
{
    // Swift 속도 플러그인의 C 함수 7개(계획 0024 "Swift ↔ C# 약속"). 진짜는 아이폰 빌드의 IosSpeedNative,
    // 에디터 테스트는 가짜로 바꿔 끼운다. 모두 메인 스레드에서 부른다.
    public interface ISpeedNativeApi
    {
        void Start();
        void Stop();
        int Read(double[] slots);      // 채운 칸 수
        void StageBegin(double kmh);
        string StageEnd();             // {"result":"Ok","point":{...}} / {"result":"Irregular","point":null}
        void StageCancel();
        bool Load(string modelJson);   // null이면 보정 지움
    }

    // 아이폰 진짜 속도 센서(계획 0024). 계산은 Swift가 하고 여기선 한 프레임에 한 번 숫자 칸을 읽는다(ADR-0009).
    // 켜기·끄기·이벤트·보정 순서는 SpeedSensorBase가 하고, 이 클래스는 칸을 SpeedReading으로 옮기기와 단계 결과 읽기만.
    public sealed class IosSpeedSensor : SpeedSensorBase
    {
        // scrun_speed_read 칸(순서 고정, Swift ScrunSpeed.swift와 짝)
        public const int SlotVersion = 1;
        public const int SlotCount = 11;
        const int IVersion = 0, ITime = 1, ISpeed = 2, IGait = 3, ICadence = 4, ISteps = 5, IAir = 6,
                  IPhoneFlow = 7, IAirFlow = 8, IPhoneRate = 9, IAirRate = 10;

        readonly ISpeedNativeApi api;
        readonly double[] slots = new double[SlotCount];
        bool layoutWarned;

        public IosSpeedSensor(ISpeedNativeApi api) => this.api = api ?? throw new ArgumentNullException(nameof(api));

        // 점검 화면용: 지난 1초에 들어온 샘플 수
        public int PhoneSamplesPerSec { get; private set; }
        public int AirPodsSamplesPerSec { get; private set; }

        protected override void OnStart() => api.Start();

        protected override void OnStop()
        {
            api.Stop();
            PhoneSamplesPerSec = AirPodsSamplesPerSec = 0;
        }

        protected override void Sample(out SpeedReading reading, out SensorFlow phone, out SensorFlow airPods)
        {
            int n = api.Read(slots);
            if (n < SlotCount || slots[IVersion] != SlotVersion)
            {
                // Swift와 C#의 칸 약속이 어긋남(한쪽만 고침). 값을 믿을 수 없으니 끊긴 것으로
                if (!layoutWarned)
                {
                    layoutWarned = true;
                    Debug.LogError($"[속도] Swift 칸이 약속과 다릅니다(칸 {n}, 버전 {(n > 0 ? slots[IVersion] : -1)}). 계획 0024 약속 표를 확인하세요.");
                }
                reading = default;
                phone = airPods = SensorFlow.Lost;
                return;
            }
            double kmh = slots[ISpeed];
            var gait = Enum.IsDefined(typeof(Gait), (int)slots[IGait]) ? (Gait)(int)slots[IGait] : Gait.None;
            reading = new SpeedReading
            {
                Time = slots[ITime],
                HasSpeed = !double.IsNaN(kmh) && gait != Gait.None,
                SpeedKmh = double.IsNaN(kmh) ? 0f : (float)kmh,
                Gait = gait,
                CadenceSpm = (float)slots[ICadence],
                Steps = (int)slots[ISteps],
                UsingAirPods = slots[IAir] != 0,
            };
            phone = Flow(slots[IPhoneFlow]);
            airPods = Flow(slots[IAirFlow]);
            PhoneSamplesPerSec = (int)slots[IPhoneRate];
            AirPodsSamplesPerSec = (int)slots[IAirRate];
        }

        static SensorFlow Flow(double v) =>
            Enum.IsDefined(typeof(SensorFlow), (int)v) ? (SensorFlow)(int)v : SensorFlow.Lost;

        protected override void OnBeginStage(float speedKmh) => api.StageBegin(speedKmh);

        protected override StageMeasurement OnEndStage(float speedKmh) => ParseStage(api.StageEnd());

        protected override void OnCancelStage() => api.StageCancel();

        protected override void OnCalibrationApplied(CalibrationModel m)
        {
            if (!api.Load(m?.ToJson()))
                Debug.LogError("[속도] Swift가 보정을 받지 않았습니다(모양이 다름). 속도는 이전 보정으로 계산됩니다.");
        }

        // Swift 단계 결과 JSON → 측정. 읽을 수 없으면 그 단계는 다시 재게 SensorLost
        internal static StageMeasurement ParseStage(string json)
        {
            try
            {
                var o = string.IsNullOrEmpty(json) ? null : JObject.Parse(json);
                if (o == null || !Enum.TryParse(o.Value<string>("result"), false, out CalStageResult r) ||
                    r == CalStageResult.NotFaster) // NotFaster는 C#만 판정
                    return Broken(json);
                if (r != CalStageResult.Ok) return new StageMeasurement { Result = r };
                var p = o["point"]?.Type == JTokenType.Object ? o["point"].ToObject<CalibrationPoint>() : null;
                if (p == null) return Broken(json);
                return new StageMeasurement { Result = CalStageResult.Ok, Point = p };
            }
            catch (JsonException)
            {
                return Broken(json);
            }
        }

        static StageMeasurement Broken(string json)
        {
            Debug.LogError("[속도] Swift 단계 결과를 읽지 못했습니다: " + json);
            return new StageMeasurement { Result = CalStageResult.SensorLost };
        }
    }
}
