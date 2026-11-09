using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Scrun.App.Tests
{
    // 아이폰 브리지의 C# 쪽 규칙. Swift 대신 가짜 통로가 답한다(이름·JSON 모양은 ScrunBridge.swift와 같음).
    public class IosNativeTests
    {
        sealed class FakeChannel : INativeChannel
        {
            public readonly List<(int reqId, string name, string json)> Calls = new List<(int, string, string)>();
            public readonly Dictionary<string, string> QueryReplies = new Dictionary<string, string>();
            public readonly List<(string name, string json)> Queries = new List<(string, string)>();

            public void Call(int reqId, string name, string json) => Calls.Add((reqId, name, json));

            public string Query(string name, string json)
            {
                Queries.Add((name, json));
                return QueryReplies.TryGetValue(name, out var r) ? r : "{\"ok\":false,\"error\":\"모르는 조회\"}";
            }
        }

        FakeChannel ch;
        NativeBridge bridge;
        IosNative native;

        [SetUp]
        public void SetUp()
        {
            ch = new FakeChannel();
            ch.QueryReplies["headphones.watch"] = "{\"ok\":true,\"value\":null}";
            bridge = new NativeBridge(ch);
            native = new IosNative(bridge);
        }

        [Test]
        public void 권한_요청은_요청_번호로_답을_짝지어_돌려준다()
        {
            var cam = native.RequestAsync(PermissionKind.Camera);
            var noti = native.RequestAsync(PermissionKind.Notifications);
            Assert.AreEqual(2, ch.Calls.Count);
            Assert.AreEqual("permissions.request", ch.Calls[0].name);
            StringAssert.Contains("\"camera\"", ch.Calls[0].json);
            StringAssert.Contains("\"notifications\"", ch.Calls[1].json);

            // 순서를 바꿔 답해도 각자 자기 답을 받는다
            bridge.Post(ch.Calls[1].reqId, "{\"ok\":true,\"value\":\"denied\"}");
            bridge.Post(ch.Calls[0].reqId, "{\"ok\":true,\"value\":\"granted\"}");
            bridge.Drain();

            Assert.AreEqual(PermissionStatus.Granted, cam.GetAwaiter().GetResult());
            Assert.AreEqual(PermissionStatus.Denied, noti.GetAwaiter().GetResult());
            Assert.AreEqual(0, bridge.PendingCount);
        }

        [Test]
        public void 답은_Drain_전에는_끝나지_않는다_메인_스레드에서만_끝냄()
        {
            var a = native.RequestAsync(PermissionKind.Motion);
            bridge.Post(ch.Calls[0].reqId, "{\"ok\":true,\"value\":\"granted\"}");
            Assert.IsFalse(a.GetAwaiter().IsCompleted);
            bridge.Drain();
            Assert.IsTrue(a.GetAwaiter().IsCompleted);
        }

        [Test]
        public void 다른_스레드에서_온_답도_받는다()
        {
            var a = native.RequestAsync(PermissionKind.Camera);
            int id = ch.Calls[0].reqId;
            var t = new Thread(() => bridge.Post(id, "{\"ok\":true,\"value\":\"restricted\"}"));
            t.Start();
            t.Join();
            bridge.Drain();
            Assert.AreEqual(PermissionStatus.Restricted, a.GetAwaiter().GetResult());
        }

        [Test]
        public void 실패_답은_NativeException()
        {
            var k = native.LoginAsync();
            bridge.Post(ch.Calls[0].reqId, "{\"ok\":false,\"error\":\"카카오 SDK가 없습니다\"}");
            bridge.Drain();
            var e = Assert.Throws<NativeException>(() => k.GetAwaiter().GetResult());
            StringAssert.Contains("카카오 SDK가 없습니다", e.Message);
        }

        [Test]
        public void 카카오_취소는_null()
        {
            var k = native.LoginAsync();
            Assert.AreEqual("kakao.login", ch.Calls[0].name);
            bridge.Post(ch.Calls[0].reqId, "{\"ok\":true,\"value\":null}");
            bridge.Drain();
            Assert.IsNull(k.GetAwaiter().GetResult());
        }

        [Test]
        public void 권한_이벤트와_이어폰_이벤트가_메인_스레드에서_온다()
        {
            var perms = new List<(PermissionKind, PermissionStatus)>();
            var phones = new List<HeadphoneStatus>();
            native.Changed += (k, s) => perms.Add((k, s));
            ((IHeadphones)native).Changed += s => phones.Add(s);

            bridge.Post(0, "{\"event\":\"permission\",\"kind\":\"headphoneMotion\",\"status\":\"granted\"}");
            bridge.Post(0, "{\"event\":\"headphones\",\"status\":\"connected\",\"name\":\"AirPods Pro\"}");
            Assert.AreEqual(0, perms.Count + phones.Count);
            bridge.Drain();

            CollectionAssert.AreEqual(new[] { (PermissionKind.HeadphoneMotion, PermissionStatus.Granted) }, perms);
            CollectionAssert.AreEqual(new[] { HeadphoneStatus.Connected }, phones);
        }

        [Test]
        public void 잘못된_메시지가_있어도_나머지는_처리한다()
        {
            var a = native.RequestAsync(PermissionKind.Camera);
            bridge.Post(0, "깨진 JSON");
            bridge.Post(999, "{\"ok\":true,\"value\":\"granted\"}"); // 기다리는 요청 없음
            bridge.Post(ch.Calls[0].reqId, "{\"ok\":true,\"value\":\"granted\"}");
            LogAssert.Expect(LogType.Exception, new Regex("NativeException"));
            LogAssert.Expect(LogType.Warning, new Regex("999"));
            bridge.Drain();
            Assert.AreEqual(PermissionStatus.Granted, a.GetAwaiter().GetResult());
        }

        [Test]
        public void 상태_읽기와_키체인은_바로_답한다()
        {
            ch.QueryReplies["permissions.status"] = "{\"ok\":true,\"value\":\"notDetermined\"}";
            ch.QueryReplies["headphones.status"] = "{\"ok\":true,\"value\":{\"status\":\"connectedNoMotion\",\"name\":\"유선 이어폰\"}}";
            ch.QueryReplies["secure.get"] = "{\"ok\":true,\"value\":null}";
            ch.QueryReplies["secure.set"] = "{\"ok\":true,\"value\":null}";

            Assert.AreEqual(PermissionStatus.NotDetermined, native.GetStatus(PermissionKind.Motion));
            StringAssert.Contains("\"motion\"", ch.Queries[0].json);
            Assert.AreEqual(HeadphoneStatus.ConnectedNoMotion, native.Status);
            Assert.AreEqual("유선 이어폰", native.DeviceName);
            Assert.IsNull(native.Get("session.v1"));
            native.Set("session.v1", "{\"a\":\"x\"}");
            StringAssert.Contains("session.v1", ch.Queries[ch.Queries.Count - 1].json);
        }

        [Test]
        public void 키체인_실패는_삼키지_않고_던진다()
        {
            ch.QueryReplies["secure.set"] = "{\"ok\":false,\"error\":\"키체인 저장 실패(-25308)\"}";
            var e = Assert.Throws<NativeException>(() => native.Set("k", "v"));
            StringAssert.Contains("-25308", e.Message);
        }

        [Test]
        public void 키체인을_못_읽어도_앱은_로그인_안_된_상태로_시작한다()
        {
            ch.QueryReplies["secure.get"] = "{\"ok\":false,\"error\":\"키체인 읽기 실패(-34018)\"}";
            LogAssert.Expect(LogType.Exception, new Regex("-34018"));
            var s = new Session(native);
            Assert.IsFalse(s.IsLoggedIn);
            Assert.IsFalse(ch.Queries.Exists(q => q.name == "secure.delete")); // 못 읽은 값을 지워 버리지 않음
        }

        [Test]
        public void 이어폰은_구독자가_있는_동안만_지켜본다()
        {
            IHeadphones h = native;
            System.Action<HeadphoneStatus> a = _ => { }, b = _ => { };
            int Watches(string on) => ch.Queries.FindAll(q => q.name == "headphones.watch" && q.json.Contains(on)).Count;

            h.Changed += a;
            h.Changed += b;
            Assert.AreEqual(1, Watches("true"));
            h.Changed -= a;
            Assert.AreEqual(0, Watches("false"));
            h.Changed -= a; // 이미 뺀 것을 또 빼도 수가 안 틀어짐
            h.Changed -= b;
            Assert.AreEqual(1, Watches("false"));
        }

        [Test]
        public void 키체인_삭제가_실패해도_로그아웃_전환은_끝까지_한다()
        {
            ch.QueryReplies["secure.get"] = "{\"ok\":true,\"value\":null}";
            ch.QueryReplies["secure.set"] = "{\"ok\":true,\"value\":null}";
            ch.QueryReplies["secure.delete"] = "{\"ok\":false,\"error\":\"키체인 삭제 실패(-25308)\"}";
            var s = new Session(native);
            s.SetTokens("a1", "r1");
            int epoch = s.Epoch;
            bool expired = false;
            s.Expired += () => expired = true;

            LogAssert.Expect(LogType.Exception, new Regex("-25308"));
            s.Expire();

            Assert.IsFalse(s.IsLoggedIn);
            Assert.AreEqual(epoch + 1, s.Epoch); // 재발급 중이던 결과가 버려짐
            Assert.IsTrue(expired);
        }

        [Test]
        public void 이름_변환은_모든_값을_왕복한다()
        {
            foreach (PermissionKind k in System.Enum.GetValues(typeof(PermissionKind)))
                Assert.AreEqual(k, IosNative.ParseKind(IosNative.KindName(k)));
            Assert.Throws<NativeException>(() => IosNative.ParseStatus("authorized"));
        }
    }
}
