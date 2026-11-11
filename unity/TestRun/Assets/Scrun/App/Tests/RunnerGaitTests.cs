using NUnit.Framework;
using UnityEngine;

namespace Scrun.App.Tests
{
    // 아바타 발 속도 표(계획 0027): 표 읽기, 블렌드 목표값, 재생 속도, 서 있기 ↔ 걷기 문턱
    public class RunnerGaitTests
    {
        RunnerGait gait;

        [SetUp]
        public void SetUp()
        {
            gait = ScriptableObject.CreateInstance<RunnerGait>();
            // 블렌드 값 3.5(걷기)·6·10, 실제 발 속도 6·6·9
            gait.Set(new[] { 3.5f, 6f, 10f }, new[] { 6f, 6f, 9f });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(gait);

        [Test]
        public void ValidTableOnly()
        {
            Assert.IsTrue(gait.IsValid);
            var bad = ScriptableObject.CreateInstance<RunnerGait>();
            Assert.IsFalse(bad.IsValid, "빈 표");
            bad.Set(new[] { 3f, 2f }, new[] { 1f, 1f });
            Assert.IsFalse(bad.IsValid, "순서가 꼬임");
            bad.Set(new[] { 2f, 3f }, new[] { 1f, 0f });
            Assert.IsFalse(bad.IsValid, "발 속도 0");
            bad.Set(new[] { 2f, 3f }, new[] { 1f });
            Assert.IsFalse(bad.IsValid, "길이가 다름");
            Object.DestroyImmediate(bad);
        }

        [Test]
        public void FootSpeedBetweenAndBeyond()
        {
            Assert.AreEqual(6f, gait.FootKmh(1f), 1e-4, "표 앞은 첫 값");
            Assert.AreEqual(6f, gait.FootKmh(4f), 1e-4);
            Assert.AreEqual(7.5f, gait.FootKmh(8f), 1e-4, "6과 10 사이 곧게");
            Assert.AreEqual(9f, gait.FootKmh(20f), 1e-4, "표 뒤는 끝 값");
        }

        [Test]
        public void TargetNeverBlendsIdleWhileMoving()
        {
            Assert.AreEqual(3.5f, gait.TargetParam(1f, moving: true), 1e-4, "느리면 걷기(서 있기와 섞지 않음)");
            Assert.AreEqual(8f, gait.TargetParam(8f, moving: true), 1e-4);
            Assert.AreEqual(10f, gait.TargetParam(20f, moving: true), 1e-4, "표 끝을 넘으면 끝 동작");
            Assert.AreEqual(0f, gait.TargetParam(0.2f, moving: false), 1e-4, "서 있기");
        }

        [Test]
        public void PlaybackKeepsFeetOnGround()
        {
            Assert.AreEqual(1f / 6f, gait.PlaybackSpeed(1f, true, 3.5f), 1e-4, "1 km/h면 걷기를 1/6배로");
            Assert.AreEqual(8f / 7.5f, gait.PlaybackSpeed(8f, true, 8f), 1e-4);
            Assert.AreEqual(20f / 9f, gait.PlaybackSpeed(20f, true, 10f), 1e-4, "끝 동작을 빠르게");
            Assert.AreEqual(1f, gait.PlaybackSpeed(0f, false, 0f), 1e-4, "서 있으면 1배");
            Assert.AreEqual(0f, gait.PlaybackSpeed(0f, false, 8f), 1e-4, "멈췄는데 블렌드에 달리기가 남아 있으면 그 자리에 세움");
            Assert.AreEqual(1f, gait.PlaybackSpeed(0f, false, 0.4f), 1e-4, "거의 서 있기가 되면 1배");
            Assert.AreEqual(0.3f / 6f, gait.PlaybackSpeed(0.3f, true, 3.5f), 1e-4, "0.3 km/h도 발이 붙음(하한 0.05배)");
            Assert.AreEqual(1f / 6f, gait.PlaybackSpeed(1f, true, 0.4f), 1e-4, "블렌드가 아직 걷기에 못 미쳐도 걷기 기준");
            Assert.AreEqual(3f, gait.PlaybackSpeed(100f, true, 10f), 1e-4, "너무 빠르면 3배에서 멈춤");
        }

        [Test]
        public void StartStopHasTwoThresholds()
        {
            Assert.IsFalse(RunnerGait.IsMoving(0.4f, wasMoving: false), "서 있다가 0.5 아래면 그대로");
            Assert.IsTrue(RunnerGait.IsMoving(0.5f, wasMoving: false));
            Assert.IsTrue(RunnerGait.IsMoving(0.4f, wasMoving: true), "걷다가 0.3 위면 계속");
            Assert.IsFalse(RunnerGait.IsMoving(0.29f, wasMoving: true));
        }
    }
}
