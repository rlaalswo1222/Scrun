using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Scrun.App.Tests
{
    // 계기판 카메라(계획 0033): 아이폰 쪽 C# 규칙(이름·JSON 모양은 ScrunCamera.swift·ScrunBridge.swift와 같음)과 에디터 가짜.
    public class CameraTests
    {
        sealed class FakeChannel : INativeChannel
        {
            public readonly List<(int reqId, string name, string json)> Calls = new List<(int, string, string)>();
            public readonly List<(string name, string json)> Queries = new List<(string, string)>();
            public void Call(int reqId, string name, string json) => Calls.Add((reqId, name, json));
            public string Query(string name, string json)
            {
                Queries.Add((name, json));
                return "{\"ok\":true,\"value\":null}";
            }
        }

        FakeChannel ch;
        NativeBridge bridge;
        IosNative native;
        string dir;

        [SetUp]
        public void SetUp()
        {
            ch = new FakeChannel();
            bridge = new NativeBridge(ch);
            native = new IosNative(bridge);
            dir = Path.Combine(Path.GetTempPath(), "scrun-camera-test-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        CameraShot Reply(Awaitable<CameraShot> a, string json)
        {
            bridge.Post(ch.Calls[^1].reqId, json);
            bridge.Drain();
            return a.GetAwaiter().GetResult();
        }

        // ---- 아이폰(IosNative) ----

        [Test]
        public void 촬영은_camera_shoot로_시간_문구_노출을_보낸다()
        {
            native.ShootAsync(new CameraShootOptions { TimeLimitSec = 30, ExposureBiases = new[] { 0f, -2f } });
            Assert.AreEqual("camera.shoot", ch.Calls[0].name);
            var j = ch.Calls[0].json; // 테스트 묶음은 Newtonsoft를 참조하지 않아 글자로 확인
            StringAssert.Contains("\"timeLimitSec\":30", j);
            StringAssert.Contains("\"guideLabel\":\"계기판이 이 안에 들어오게\"", j);
            StringAssert.Contains("\"biases\":[0.0,-2.0]", j);
        }

        [Test]
        public void 찍으면_사진_경로와_노출을_순서대로_받는다()
        {
            var shot = Reply(native.ShootAsync(),
                "{\"ok\":true,\"value\":{\"outcome\":\"taken\",\"photos\":[{\"path\":\"/tmp/a-0.jpg\",\"bias\":0},{\"path\":\"/tmp/a-1.jpg\",\"bias\":-1.5}]}}");
            Assert.AreEqual(CameraOutcome.Taken, shot.Outcome);
            Assert.AreEqual(2, shot.Photos.Count);
            Assert.AreEqual("/tmp/a-1.jpg", shot.Photos[1].Path);
            Assert.AreEqual(-1.5f, shot.Photos[1].ExposureBias);
        }

        [TestCase("cancelled", CameraOutcome.Cancelled)]
        [TestCase("timedOut", CameraOutcome.TimedOut)]
        [TestCase("noPermission", CameraOutcome.NoPermission)]
        [TestCase("unavailable", CameraOutcome.Unavailable)]
        public void 안_찍은_결과는_사진_없이_종류만(string outcome, CameraOutcome expected)
        {
            var shot = Reply(native.ShootAsync(), "{\"ok\":true,\"value\":{\"outcome\":\"" + outcome + "\"}}");
            Assert.AreEqual(expected, shot.Outcome);
            Assert.AreEqual(0, shot.Photos.Count);
        }

        [Test]
        public void 모르는_결과나_사진_없는_찍음은_NativeException()
        {
            var a = native.ShootAsync();
            bridge.Post(ch.Calls[0].reqId, "{\"ok\":true,\"value\":{\"outcome\":\"exploded\"}}");
            bridge.Drain();
            Assert.Throws<NativeException>(() => a.GetAwaiter().GetResult());

            var b = native.ShootAsync();
            bridge.Post(ch.Calls[1].reqId, "{\"ok\":true,\"value\":{\"outcome\":\"taken\",\"photos\":[]}}");
            bridge.Drain();
            Assert.Throws<NativeException>(() => b.GetAwaiter().GetResult());
        }

        [Test]
        public void 화면이_이미_떠_있다는_실패는_NativeException()
        {
            var a = native.ShootAsync();
            bridge.Post(ch.Calls[0].reqId, "{\"ok\":false,\"error\":\"카메라 화면이 이미 떠 있습니다.\"}");
            bridge.Drain();
            StringAssert.Contains("이미", Assert.Throws<NativeException>(() => a.GetAwaiter().GetResult()).Message);
        }

        [Test]
        public void 지우기는_경로만_보내고_사진이_없으면_안_부른다()
        {
            native.Discard(new CameraShot { Outcome = CameraOutcome.Cancelled });
            native.Discard(null);
            Assert.AreEqual(0, ch.Queries.Count);

            native.Discard(new CameraShot
            {
                Outcome = CameraOutcome.Taken,
                Photos = { new CameraPhoto { Path = "/tmp/a-0.jpg" }, new CameraPhoto { Path = "/tmp/a-1.jpg" } },
            });
            Assert.AreEqual("camera.discard", ch.Queries[0].name);
            StringAssert.Contains("/tmp/a-1.jpg", ch.Queries[0].json);
        }

        // ---- 에디터 가짜(FakeCamera) ----

        FakeCamera Fake(PermissionStatus camera, CameraOutcome result = CameraOutcome.Taken, bool grant = true)
        {
            var cfg = new FakeNativeConfig { requestDelaySec = 0f, cameraDelaySec = 0f, cameraResult = result };
            cfg.startStatus[(int)PermissionKind.Camera] = camera;
            cfg.grantOnRequest[(int)PermissionKind.Camera] = grant;
            return new FakeCamera(new FakeNative(cfg), cfg, dir);
        }

        static CameraShot Shoot(FakeCamera f, CameraShootOptions o = null) => f.ShootAsync(o).GetAwaiter().GetResult();

        [Test]
        public void 가짜_찍음은_노출마다_JPEG를_쓰고_지우기로_사라진다()
        {
            var f = Fake(PermissionStatus.Granted);
            var shot = Shoot(f);
            Assert.AreEqual(CameraOutcome.Taken, shot.Outcome);
            CollectionAssert.AreEqual(new[] { 0f, -1.5f, -3f }, shot.Photos.ConvertAll(p => p.ExposureBias));
            foreach (var p in shot.Photos)
            {
                Assert.IsTrue(File.Exists(p.Path), p.Path);
                var b = File.ReadAllBytes(p.Path);
                Assert.AreEqual(0xFF, b[0]); // JPEG 시작(FF D8)
                Assert.AreEqual(0xD8, b[1]);
            }
            Assert.AreNotEqual(new FileInfo(shot.Photos[0].Path).Length, 0);

            f.Discard(shot);
            foreach (var p in shot.Photos) Assert.IsFalse(File.Exists(p.Path));
        }

        [Test]
        public void 가짜_지우기는_카메라_폴더_밖_파일을_안_지운다()
        {
            var outside = Path.Combine(Path.GetTempPath(), "scrun-keep-" + Guid.NewGuid().ToString("N") + ".jpg");
            File.WriteAllText(outside, "x");
            try
            {
                Fake(PermissionStatus.Granted).Discard(new CameraShot { Photos = { new CameraPhoto { Path = outside } } });
                Assert.IsTrue(File.Exists(outside));
            }
            finally
            {
                File.Delete(outside);
            }
        }

        [TestCase(PermissionStatus.Denied)]
        [TestCase(PermissionStatus.Restricted)]
        public void 가짜는_카메라_권한이_없으면_NoPermission(PermissionStatus status)
        {
            var shot = Shoot(Fake(status));
            Assert.AreEqual(CameraOutcome.NoPermission, shot.Outcome);
            Assert.IsFalse(Directory.Exists(dir));
        }

        [Test]
        public void 가짜는_안_물어본_권한을_먼저_묻는다()
        {
            Assert.AreEqual(CameraOutcome.Taken, Shoot(Fake(PermissionStatus.NotDetermined, grant: true)).Outcome);
            Assert.AreEqual(CameraOutcome.NoPermission, Shoot(Fake(PermissionStatus.NotDetermined, grant: false)).Outcome);
        }

        [TestCase(CameraOutcome.Cancelled)]
        [TestCase(CameraOutcome.TimedOut)]
        [TestCase(CameraOutcome.Unavailable)]
        public void 가짜는_창에서_고른_결과를_사진_없이(CameraOutcome result)
        {
            var shot = Shoot(Fake(PermissionStatus.Granted, result));
            Assert.AreEqual(result, shot.Outcome);
            Assert.AreEqual(0, shot.Photos.Count);
        }

        [Test]
        public void 가짜는_찍는_중에_또_부르면_NativeException()
        {
            var cfg = new FakeNativeConfig { requestDelaySec = 0f, cameraDelaySec = 60f };
            cfg.startStatus[(int)PermissionKind.Camera] = PermissionStatus.Granted;
            var f = new FakeCamera(new FakeNative(cfg), cfg, dir);
            var first = f.ShootAsync(); // 60초 기다리는 중(EditMode에선 프레임이 안 넘어감)
            Assert.IsFalse(first.GetAwaiter().IsCompleted);
            Assert.Throws<NativeException>(() => f.ShootAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void 노출을_하나만_주면_한_장()
        {
            var shot = Shoot(Fake(PermissionStatus.Granted), new CameraShootOptions { ExposureBiases = new[] { -2f } });
            Assert.AreEqual(1, shot.Photos.Count);
            Assert.AreEqual(-2f, shot.Photos[0].ExposureBias);
        }
    }
}
