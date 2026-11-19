using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 로딩 덮개(I-07)·오류창(I-08)·조용히 다시 받기
    public class FeedbackTests
    {
        static void AttachFake()
        {
            var root = new VisualElement();
            foreach (var n in new[] { "loading", "error" }) root.Add(new VisualElement { name = n });
            foreach (var n in new[] { "error-title", "error-body", "error-id" }) root.Add(new Label { name = n });
            foreach (var n in new[] { "error-retry", "error-close" }) root.Add(new Button { name = n });
            Feedback.Attach(root);
        }

        [TearDown]
        public void Detach()
        {
            Feedback.Detach();
            Feedback.OnNotLoggedIn = null;
        }

        [Test]
        public void 오류창의_다시_시도는_창을_닫고_그_일을_한_번_다시_한다()
        {
            AttachFake();
            int count = 0;
            Feedback.ShowError("연결이 안 돼요", "다시", null, () => count++);
            Assert.IsTrue(Feedback.IsBlocking);
            Feedback.PressRetry();
            Assert.AreEqual(1, count);
            Assert.IsFalse(Feedback.IsBlocking, "창은 닫힘");
            Feedback.PressRetry();
            Assert.AreEqual(1, count, "닫힌 창의 다시 시도는 아무것도 안 함");
        }

        [Test]
        public void 이미_보이는_화면은_덮개도_오류창도_없이_다시_받는다()
        {
            AttachFake();
            bool blockedDuring = true;
            ApiException seen = null;
            var quiet = Feedback.RunQuiet(() =>
            {
                blockedDuring = Feedback.IsBlocking;
                return Task.FromException(new ApiException(ApiFailure.Server, "없음", 404, "USER_NOT_FOUND"));
            }, e => { seen = e; return true; });
            Assert.IsTrue(quiet.IsCompleted);
            Assert.IsFalse(quiet.Result);
            Assert.IsFalse(blockedDuring, "받는 동안 덮개 없음(탭에 다시 들어올 때 '잠시만요'가 뜨지 않게)");
            Assert.AreEqual("USER_NOT_FOUND", seen?.Code, "화면이 직접 처리할 실패는 넘겨줌");
            Assert.IsFalse(Feedback.IsBlocking, "실패해도 오류창 없음");

            var noOverlay = Feedback.Run(() =>
            {
                blockedDuring = Feedback.IsBlocking;
                return Task.CompletedTask;
            }, overlay: false);
            Assert.IsTrue(noOverlay.Result);
            Assert.IsFalse(blockedDuring, "끌어서 새로고침: 덮개 없이");
        }

        [Test]
        public void 처음_받을_때는_덮개를_띄우고_실패를_알린다()
        {
            AttachFake();
            bool blockedDuring = false;
            var first = Feedback.Run(() =>
            {
                blockedDuring = Feedback.IsBlocking;
                return Task.FromException(new ApiException(ApiFailure.Network, "x"));
            });
            Assert.IsFalse(first.Result);
            Assert.IsTrue(blockedDuring, "처음엔 덮개(I-07)");
            Assert.IsTrue(Feedback.IsBlocking, "실패 안내창");
        }

        [Test]
        public void 조용히_받다가_로그인이_풀리면_로그인으로_보낸다()
        {
            AttachFake();
            string sent = null;
            Feedback.OnNotLoggedIn = m => sent = m;
            var r = Feedback.RunQuiet(() => Task.FromException(new ApiException(ApiFailure.NotLoggedIn, "만료")), _ => true);
            Assert.IsFalse(r.Result);
            Assert.IsNotNull(sent, "화면이 처리한다고 해도 로그인 풀림은 늘 로그인으로");

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("버그"));
            var crash = Feedback.RunQuiet(() => Task.FromException(new System.InvalidOperationException("버그")));
            Assert.IsFalse(crash.Result);
            Assert.IsFalse(Feedback.IsBlocking, "앱 버그도 조용히(기록만)");
        }
    }
}
