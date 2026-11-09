using System.Collections.Generic;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 지환 화면이 기대는 FakeNative 규칙. 팝업 지연은 0초로 두어 프레임 없이 바로 끝나게 한다.
    public class FakeNativeTests
    {
        static FakeNativeConfig Cfg(params PermissionStatus[] start)
        {
            var c = new FakeNativeConfig { requestDelaySec = 0f };
            for (int i = 0; i < start.Length; i++) c.startStatus[i] = start[i];
            return c;
        }

        static PermissionStatus Request(FakeNative f, PermissionKind k) =>
            f.RequestAsync(k).GetAwaiter().GetResult();

        [Test]
        public void 처음_상태를_그대로_돌려준다()
        {
            var f = new FakeNative(Cfg(PermissionStatus.Granted, PermissionStatus.Denied, PermissionStatus.NotDetermined, PermissionStatus.Restricted));
            Assert.AreEqual(PermissionStatus.Granted, f.GetStatus(PermissionKind.Motion));
            Assert.AreEqual(PermissionStatus.Denied, f.GetStatus(PermissionKind.Camera));
            Assert.AreEqual(PermissionStatus.NotDetermined, f.GetStatus(PermissionKind.HeadphoneMotion));
            Assert.AreEqual(PermissionStatus.Restricted, f.GetStatus(PermissionKind.Notifications));
        }

        [Test]
        public void 안_물어본_권한을_요청하면_팝업_답대로_바뀌고_이벤트가_한_번_온다()
        {
            var c = Cfg();
            c.grantOnRequest[(int)PermissionKind.Notifications] = false;
            var f = new FakeNative(c);
            var events = new List<(PermissionKind, PermissionStatus)>();
            f.Changed += (k, s) => events.Add((k, s));

            Assert.AreEqual(PermissionStatus.Granted, Request(f, PermissionKind.Camera));
            Assert.AreEqual(PermissionStatus.Denied, Request(f, PermissionKind.Notifications));
            Assert.AreEqual(PermissionStatus.Granted, f.GetStatus(PermissionKind.Camera));
            CollectionAssert.AreEqual(new[]
            {
                (PermissionKind.Camera, PermissionStatus.Granted),
                (PermissionKind.Notifications, PermissionStatus.Denied),
            }, events);
        }

        [Test]
        public void 이미_정해진_권한은_팝업_없이_그대로_돌려주고_이벤트도_없다()
        {
            var f = new FakeNative(Cfg(PermissionStatus.Denied));
            int count = 0;
            f.Changed += (_, _) => count++;

            Assert.AreEqual(PermissionStatus.Denied, Request(f, PermissionKind.Motion));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void 설정_앱에서_바꾼_흉내는_다를_때만_이벤트가_온다()
        {
            var f = new FakeNative(Cfg(PermissionStatus.Denied));
            int count = 0;
            f.Changed += (_, _) => count++;

            f.SimulateStatus(PermissionKind.Motion, PermissionStatus.Denied);
            Assert.AreEqual(0, count);
            f.SimulateStatus(PermissionKind.Motion, PermissionStatus.Granted);
            Assert.AreEqual(1, count);
            Assert.AreEqual(PermissionStatus.Granted, f.GetStatus(PermissionKind.Motion));
        }

        [Test]
        public void 이어폰을_끼고_빼면_상태와_이름이_바뀌고_이벤트가_온다()
        {
            var c = Cfg();
            c.headphones = HeadphoneStatus.NotConnected;
            var f = new FakeNative(c);
            IHeadphones h = f;
            var events = new List<HeadphoneStatus>();
            h.Changed += events.Add;

            Assert.IsNull(h.DeviceName);
            f.SimulateHeadphones(HeadphoneStatus.Connected, "AirPods 3");
            Assert.AreEqual("AirPods 3", h.DeviceName);
            f.SimulateHeadphones(HeadphoneStatus.Connected, "AirPods 3"); // 같은 상태 → 이벤트 없음
            f.SimulateHeadphones(HeadphoneStatus.NotConnected, "AirPods 3");
            Assert.IsNull(h.DeviceName);
            CollectionAssert.AreEqual(new[] { HeadphoneStatus.Connected, HeadphoneStatus.NotConnected }, events);
        }

        [Test]
        public void 예전_모양의_저장값도_권한_개수에_맞춘다()
        {
            var c = new FakeNativeConfig { startStatus = new PermissionStatus[2], grantOnRequest = new[] { false }, headphoneName = "" };
            c.Normalize();
            Assert.AreEqual(4, c.startStatus.Length);
            CollectionAssert.AreEqual(new[] { false, true, true, true }, c.grantOnRequest);
            Assert.AreEqual("AirPods Pro", c.headphoneName);
        }
    }
}
