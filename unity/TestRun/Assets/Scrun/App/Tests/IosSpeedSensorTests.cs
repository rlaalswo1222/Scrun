using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Scrun.App.Tests
{
    // 아이폰 속도 센서의 C# 쪽(계획 0024): Swift 칸 → SpeedReading, 단계 결과 JSON, 보정 넘기기.
    // Swift는 가짜 C 함수로 바꿔 끼운다(약속 표 그대로).
    public class IosSpeedSensorTests
    {
        sealed class FakeApi : ISpeedNativeApi
        {
            public double[] Slots = Make(1, 12.5, 8.4, 2, 168, 120, 1, 2, 2, 100, 49);
            public int Count = IosSpeedSensor.SlotCount;
            public string StageResult = "{\"result\":\"Ok\",\"point\":{\"v\":8,\"limp\":2.41,\"cad\":158.2,\"amp\":0.62,\"running\":true}}";
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Loaded = new List<string>();
            public bool LoadOk = true;

            public static double[] Make(params double[] v) => v;
            public void Start() => Calls.Add("start");
            public void Stop() => Calls.Add("stop");
            public int Read(double[] slots)
            {
                int n = System.Math.Min(Count, System.Math.Min(slots.Length, Slots.Length));
                System.Array.Copy(Slots, slots, n);
                return n;
            }
            public void StageBegin(double kmh) => Calls.Add("begin " + kmh);
            public string StageEnd() { Calls.Add("end"); return StageResult; }
            public void StageCancel() => Calls.Add("cancel");
            public bool Load(string json) { Loaded.Add(json); return LoadOk; }
        }

        FakeApi api;
        IosSpeedSensor s;

        [SetUp]
        public void SetUp()
        {
            api = new FakeApi();
            s = new IosSpeedSensor(api);
        }

        [TearDown]
        public void TearDown()
        {
            if (s.IsRunning) s.Stop();
        }

        [Test]
        public void 칸을_읽어_속도_걸음_흐름으로_옮긴다()
        {
            s.Start();
            var r = s.Current;
            Assert.AreEqual(12.5, r.Time, 1e-9);
            Assert.IsTrue(r.HasSpeed);
            Assert.AreEqual(8.4f, r.SpeedKmh, 1e-5f);
            Assert.AreEqual(Gait.Running, r.Gait);
            Assert.AreEqual(168f, r.CadenceSpm, 1e-5f);
            Assert.AreEqual(120, r.Steps);
            Assert.IsTrue(r.UsingAirPods);
            Assert.AreEqual(SensorFlow.Ok, s.Phone);
            Assert.AreEqual(SensorFlow.Ok, s.AirPods);
            Assert.AreEqual(100, s.PhoneSamplesPerSec);
            Assert.AreEqual(49, s.AirPodsSamplesPerSec);
            CollectionAssert.AreEqual(new[] { "start" }, api.Calls);
        }

        [Test]
        public void 속도가_NaN이면_속도_없음()
        {
            api.Slots[2] = double.NaN;
            api.Slots[3] = 0;
            s.Start();
            Assert.IsFalse(s.Current.HasSpeed);
            Assert.AreEqual(Gait.None, s.Current.Gait);
        }

        [Test]
        public void 걸음이_바뀌면_한_번만_알린다()
        {
            var got = new List<Gait>();
            s.GaitChanged += g => got.Add(g);
            api.Slots[3] = 0;
            s.Start();
            api.Slots[3] = 1;
            s.Tick();
            s.Tick();
            api.Slots[3] = 2;
            s.Tick();
            CollectionAssert.AreEqual(new[] { Gait.Walking, Gait.Running }, got);
        }

        [Test]
        public void 칸_약속이_다르면_끊긴_것으로_보고_한_번만_알린다()
        {
            api.Slots[0] = 2; // 다른 버전
            LogAssert.Expect(LogType.Error, new Regex("칸이 약속과 다릅니다"));
            s.Start();
            s.Tick();
            Assert.AreEqual(SensorFlow.Lost, s.Phone);
            Assert.IsFalse(s.Current.HasSpeed);
        }

        [Test]
        public void 칸이_모자라도_끊긴_것으로()
        {
            api.Count = 8;
            LogAssert.Expect(LogType.Error, new Regex("칸이 약속과 다릅니다"));
            s.Start();
            Assert.AreEqual(SensorFlow.Lost, s.Phone);
        }

        [Test]
        public void 단계_결과_Ok면_점이_쌓인다()
        {
            s.Start();
            s.BeginCalibration();
            s.BeginCalibrationStage(8f);
            Assert.AreEqual(CalStageResult.Ok, s.EndCalibrationStage());
            Assert.AreEqual(1, s.CalibrationPointCount);
            var p = s.DraftPoints[0];
            Assert.AreEqual(8, p.V, 1e-9);
            Assert.AreEqual(2.41, p.LnImpact, 1e-9);
            Assert.AreEqual(0.62, p.AirAmp.Value, 1e-9);
            Assert.IsTrue(p.Running);
            CollectionAssert.AreEqual(new[] { "start", "begin 8", "end" }, api.Calls);
        }

        [TestCase("{\"result\":\"NoSteps\",\"point\":null}", CalStageResult.NoSteps)]
        [TestCase("{\"result\":\"Irregular\",\"point\":null}", CalStageResult.Irregular)]
        [TestCase("{\"result\":\"TooShort\",\"point\":null}", CalStageResult.TooShort)]
        [TestCase("{\"result\":\"SensorLost\",\"point\":null}", CalStageResult.SensorLost)]
        public void 단계_실패_결과를_그대로(string json, CalStageResult expected) =>
            Assert.AreEqual(expected, IosSpeedSensor.ParseStage(json).Result);

        [TestCase("이상한 값")]
        [TestCase("{\"result\":\"Ok\",\"point\":null}")]
        [TestCase("{\"result\":\"모름\"}")]
        [TestCase("{\"result\":\"NotFaster\"}")] // NotFaster는 C#만 판정
        [TestCase(null)]
        public void 읽을_수_없는_단계_결과는_다시_재게_SensorLost(string json)
        {
            LogAssert.Expect(LogType.Error, new Regex("단계 결과를 읽지 못했습니다"));
            Assert.AreEqual(CalStageResult.SensorLost, IosSpeedSensor.ParseStage(json).Result);
        }

        [Test]
        public void 단계를_취소하면_Swift에도_알린다()
        {
            s.Start();
            s.BeginCalibration();
            s.BeginCalibrationStage(4f);
            s.CancelCalibrationStage();
            CollectionAssert.Contains(api.Calls, "cancel");
        }

        [Test]
        public void 보정을_불러오면_Swift에_넘기고_지우면_null()
        {
            var model = FakeSpeedSensor.SampleModel().ToJson();
            Assert.IsTrue(s.LoadCalibration(model));
            Assert.AreEqual(model, api.Loaded[0]);
            s.ClearCalibration();
            Assert.IsNull(api.Loaded[1]);
        }

        [Test]
        public void Swift가_보정을_거절하면_알린다()
        {
            api.LoadOk = false;
            LogAssert.Expect(LogType.Error, new Regex("보정을 받지 않았습니다"));
            Assert.IsFalse(s.LoadCalibration(FakeSpeedSensor.SampleModel().ToJson()));
            Assert.IsFalse(s.HasCalibration); // C#만 "있음"이면 다시 불러오지 않아 보정 없이 달림
        }

        [Test]
        public void 끄면_Swift도_끈다()
        {
            s.Start();
            s.Stop();
            CollectionAssert.AreEqual(new[] { "start", "stop" }, api.Calls);
            Assert.AreEqual(SensorFlow.Off, s.Phone);
        }
    }
}
