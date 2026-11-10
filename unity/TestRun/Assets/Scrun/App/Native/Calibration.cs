using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Scrun.App
{
    // 보정 단계 하나를 잰 결과(계획 0023).
    public enum CalStageResult
    {
        Ok,
        NoSteps,    // 걸음이 안 잡힘(벨트 옆 발판, 폰을 손에 듦)
        Irregular,  // 걸음이 들쭉날쭉(손잡이를 잡음, 비틀거림, 폰이 미끄러짐, 단계 중에 속도를 바꿈)
        NotFaster,  // 앞 단계보다 빨라지지 않음(속도 버튼을 깜빡함)
        SensorLost, // 폰 가속도가 끊김(앱이 백그라운드로 감, 화면 꺼짐, 센서를 끔)
        TooShort,   // 버리는 5초 뒤 잴 시간이 3초도 안 됨(단계를 너무 빨리 끝냄)
    }

    // 모두에게 같은 보정 사다리(ADR-0005 추가 결정 11/10, 계획 0023). 헬스장 실험 뒤 숫자만 바꾼다.
    public static class CalibrationLadder
    {
        public static readonly float[] SpeedsKmh = { 2f, 4f, 6f, 8f, 10f, 12f, 14f };
        // 앞 SpeedSensorBase.StageSkipSec(5)초는 속도 바꾸는 시간이라 버리고 10초를 잰다. 우리 기록에서 3–17초가 오차 차이 없음(calibration_design.md)
        public const float StageSec = 15f;
    }

    // 보정점 하나: "안내 속도 v일 때 이 사람의 ln(충격)·걸음 수·에어팟 흔들림 폭".
    // 이름은 SpeedEstimator.swift의 Point(Codable)와 같다. 바꾸면 둘 다 고친다.
    public sealed class CalibrationPoint
    {
        [JsonProperty("v")] public double V;          // 안내한 속도(러닝머신 계기판 값), km/h
        [JsonProperty("limp")] public double LnImpact; // ln(충격)
        [JsonProperty("cad")] public double Cadence;   // 분당 걸음 수
        [JsonProperty("amp")] public double? AirAmp;   // 에어팟 흔들림 폭(g), 에어팟을 못 썼으면 null
        [JsonProperty("running")] public bool Running; // 이 점에서 달렸는지(걷기·달리기는 센서가 판정)
    }

    // 서버 calibration.model 칸에 그대로 들어가는 보정(서버는 해석 안 함, docs/api/data_model.md).
    // 계수가 아니라 보정점 원본을 저장하고, 불러올 때 Swift가 다시 맞춘다(SpeedEstimator.load와 같음).
    public sealed class CalibrationModel
    {
        public const int CurrentVersion = 1;

        [JsonProperty("version")] public int Version = CurrentVersion;
        [JsonProperty("points")] public List<CalibrationPoint> Points = new List<CalibrationPoint>();

        public CalibrationModel() { }
        public CalibrationModel(IEnumerable<CalibrationPoint> points) => Points = points.ToList();

        public string ToJson() => JsonConvert.SerializeObject(this);

        // 모양이 맞지 않으면 null(다른 버전, 깨진 값, 점 없음)
        public static CalibrationModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var m = JsonConvert.DeserializeObject<CalibrationModel>(json);
                if (m == null || m.Version != CurrentVersion || m.Points == null || m.Points.Count == 0) return null;
                if (m.Points.Any(p => p == null || p.V <= 0 || double.IsNaN(p.LnImpact) || p.Cadence <= 0)) return null;
                return m;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
