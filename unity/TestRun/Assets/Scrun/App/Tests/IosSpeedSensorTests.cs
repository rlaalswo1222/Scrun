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
            public readonly List<int> Methods = new List<int>();

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
            public void SetMethod(int method) => Methods.Add(method);
        }

        FakeApi api;
        IosSpeedSensor s;
        int? savedMethod; // 개발자가 에디터에서 고른 방식(테스트가 지우지 않게 되돌림)

        [SetUp]
        public void SetUp()
        {
            savedMethod = PlayerPrefs.HasKey(SpeedMethodPrefs.Key) ? PlayerPrefs.GetInt(SpeedMethodPrefs.Key) : (int?)null;
            PlayerPrefs.DeleteKey(SpeedMethodPrefs.Key);
            api = new FakeApi();
            s = new IosSpeedSensor(api);
        }

        [TearDown]
        public void TearDown()
        {
            if (s.IsRunning) s.Stop();
            if (savedMethod.HasValue) PlayerPrefs.SetInt(SpeedMethodPrefs.Key, savedMethod.Value);
            else PlayerPrefs.DeleteKey(SpeedMethodPrefs.Key);
            PlayerPrefs.Save();
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

        // ---- 달리기 방식(계획 0060) ----

        [Test]
        public void 옛_11칸이면_방식_칸은_없음으로()
        {
            s.Start();
            Assert.IsTrue(s.Current.HasSpeed, "0–10칸은 그대로 읽음");
            Assert.IsFalse(s.Methods.Has);
        }

        [Test]
        public void 덧붙인_칸에서_방식별_속도와_공중_시간을_읽는다()
        {
            api.Slots = FakeApi.Make(1, 12.5, 8.4, 2, 168, 120, 1, 2, 2, 100, 49, 8.6, 8.3, 8.2, 91.5, 1);
            api.Count = IosSpeedSensor.SlotCountMethods;
            s.Start();
            var m = s.Methods;
            Assert.IsTrue(m.Has);
            Assert.AreEqual(8.6f, m.BaseKmh, 1e-5f);
            Assert.AreEqual(8.3f, m.BlendKmh, 1e-5f);
            Assert.AreEqual(8.2f, m.FixKmh, 1e-5f);
            Assert.AreEqual(91.5f, m.FlightMs, 1e-5f);
            Assert.AreEqual(SpeedMethod.Blend, m.InUse);
            Assert.AreEqual(8.4f, s.Current.SpeedKmh, 1e-5f, "화면 속도는 2번 칸(Swift가 고른 방식) 그대로");
        }

        [Test]
        public void 칸_약속이_깨지면_방식_값도_지운다()
        {
            api.Slots = FakeApi.Make(1, 12.5, 8.4, 2, 168, 120, 1, 2, 2, 100, 49, 8.6, 8.3, 8.2, 91.5, 1);
            api.Count = IosSpeedSensor.SlotCountMethods;
            s.Start();
            Assert.IsTrue(s.Methods.Has);
            LogAssert.Expect(LogType.Error, new Regex("칸이 약속과 다릅니다"));
            api.Slots[0] = 9;
            s.Tick();
            Assert.IsFalse(s.Methods.Has);
        }

        [Test]
        public void 모르는_방식_숫자는_기본으로()
        {
            api.Slots = FakeApi.Make(1, 1, 8, 2, 168, 0, 1, 2, 2, 100, 49, 8, 8, 8, double.NaN, 7);
            api.Count = IosSpeedSensor.SlotCountMethods;
            s.Start();
            Assert.AreEqual(SpeedMethod.Base, s.Methods.InUse);
            Assert.IsNaN(s.Methods.FlightMs);
        }

        [Test]
        public void 켤_때_저장된_방식을_Swift에_넘긴다()
        {
            PlayerPrefs.SetInt(SpeedMethodPrefs.Key, (int)SpeedMethod.BlendFix);
            s.Start();
            CollectionAssert.AreEqual(new[] { (int)SpeedMethod.BlendFix }, api.Methods);
        }

        [Test]
        public void 저장된_방식_값이_이상하면_기본()
        {
            PlayerPrefs.SetInt(SpeedMethodPrefs.Key, 9);
            Assert.AreEqual(SpeedMethod.Base, SpeedMethodPrefs.Current);
        }

        [Test]
        public void 방식을_바꾸면_저장하고_켜진_센서에도_바로()
        {
            s.Start();
            SpeedMethodPrefs.Set(SpeedMethod.Blend, s);
            Assert.AreEqual(SpeedMethod.Blend, SpeedMethodPrefs.Current);
            CollectionAssert.AreEqual(new[] { (int)SpeedMethod.Base, (int)SpeedMethod.Blend }, api.Methods);
        }

        [Test]
        public void 보정점_공중_시간을_읽고_옛_보정도_그대로()
        {
            api.StageResult = "{\"result\":\"Ok\",\"point\":{\"v\":9,\"limp\":2.5,\"cad\":170,\"amp\":0.7,\"running\":true,\"fl\":95.5}}";
            var p = IosSpeedSensor.ParseStage(api.StageResult).Point;
            Assert.AreEqual(95.5, p.FlightMs.Value, 1e-9);
            StringAssert.Contains("\"fl\":95.5", new CalibrationModel(new[] { p }).ToJson());

            var old = CalibrationModel.Parse("{\"version\":1,\"points\":[{\"v\":8,\"limp\":2.41,\"cad\":158.2,\"amp\":0.62,\"running\":true}]}");
            Assert.IsNotNull(old, "공중 시간이 없는 옛 보정도 그대로 읽음");
            Assert.IsNull(old.Points[0].FlightMs);
        }

        [Test]
        public void 공중_시간이_숫자가_아니면_점이_아님()
        {
            var p = new CalibrationPoint { V = 9, LnImpact = 2.5, Cadence = 170, Running = true, FlightMs = double.NaN };
            Assert.IsFalse(p.IsValid);
        }
    }
}
