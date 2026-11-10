using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 권한 안내·이어폰 흐름 판단(계획 0023)
    public class OnboardingTests
    {
        // 이어폰 화면까지 끝냈는지(done) + 모션 상태 → 권한 안내부터 보여 줄지
        [TestCase(false, PermissionStatus.NotDetermined, true)]
        [TestCase(false, PermissionStatus.Granted, true)]   // A-07 뒤 이어폰 화면 전에 앱을 끔 → 다시 이어짐
        [TestCase(false, PermissionStatus.Denied, true)]
        [TestCase(true, PermissionStatus.Granted, false)]
        [TestCase(true, PermissionStatus.Denied, false)]    // 끝까지 갔으면 거부여도 홈(맵 입장 때 막음)
        [TestCase(true, PermissionStatus.NotDetermined, true)] // 다시 깔아 권한이 초기화됨
        public void 온보딩을_보여_줄지(bool done, PermissionStatus motion, bool expected) =>
            Assert.AreEqual(expected, Onboarding.NeedsOnboarding(done, motion));

        [TestCase(PermissionStatus.Granted, AfterAsking.Earphone)]
        [TestCase(PermissionStatus.NotDetermined, AfterAsking.Earphone)]
        [TestCase(PermissionStatus.Denied, AfterAsking.MotionDenied)]
        [TestCase(PermissionStatus.Restricted, AfterAsking.MotionDenied)]
        public void 권한을_물은_뒤_갈_곳(PermissionStatus motion, AfterAsking expected) =>
            Assert.AreEqual(expected, Onboarding.Next(motion));

        [TestCase(PermissionStatus.Granted, MotionGate.Go)]
        [TestCase(PermissionStatus.NotDetermined, MotionGate.Ask)]
        [TestCase(PermissionStatus.Denied, MotionGate.Blocked)]
        [TestCase(PermissionStatus.Restricted, MotionGate.Blocked)]
        public void 맵_입장_전_모션_확인(PermissionStatus motion, MotionGate expected) =>
            Assert.AreEqual(expected, Onboarding.BeforeMap(motion));

        [Test]
        public void Figma대로_네_가지를_모션부터_묻는다()
        {
            CollectionAssert.AreEqual(new[]
            {
                PermissionKind.Motion, PermissionKind.HeadphoneMotion, PermissionKind.Camera, PermissionKind.Notifications,
            }, Onboarding.AskOrder);
        }

        [Test]
        public void 권한마다_문구와_아이콘이_있다()
        {
            foreach (var kind in Onboarding.AskOrder)
            {
                var info = OnboardingText.Permission(kind);
                Assert.IsNotEmpty(info.Title);
                Assert.IsNotEmpty(info.IconClass);
            }
            Assert.IsTrue(OnboardingText.Permission(PermissionKind.Motion).Required);
            Assert.IsFalse(OnboardingText.Permission(PermissionKind.Notifications).Required);
        }

        [TestCase(true, PermissionStatus.Granted, "허용됨")]
        [TestCase(true, PermissionStatus.Denied, "꺼짐")]
        [TestCase(false, PermissionStatus.Restricted, "꺼짐")]
        [TestCase(true, PermissionStatus.NotDetermined, "필수")]
        [TestCase(false, PermissionStatus.NotDetermined, "선택")]
        public void 권한_칩_문구(bool required, PermissionStatus status, string expected) =>
            Assert.AreEqual(expected, OnboardingText.Chip(required, status));

        [Test]
        public void 이어폰_상태별_문구()
        {
            var off = OnboardingText.Earphone(HeadphoneStatus.NotConnected, null, PermissionStatus.NotDetermined);
            Assert.AreEqual("이어폰을 연결해 보세요", off.title);
            Assert.AreEqual("미연결", off.chip);

            var on = OnboardingText.Earphone(HeadphoneStatus.Connected, "AirPods Pro", PermissionStatus.Granted);
            Assert.AreEqual("AirPods Pro", on.name);
            Assert.AreEqual("헤드폰 모션 사용 가능", on.detail);

            var noMotion = OnboardingText.Earphone(HeadphoneStatus.ConnectedNoMotion, null, PermissionStatus.Granted);
            Assert.AreEqual("이어폰", noMotion.name);
            Assert.AreEqual("헤드폰 모션 없음", noMotion.detail);
        }

        [Test]
        public void 헤드폰_모션이_꺼져_있으면_사용_가능이라고_하지_않는다()
        {
            var denied = OnboardingText.Earphone(HeadphoneStatus.Connected, "AirPods Pro", PermissionStatus.Denied);
            StringAssert.DoesNotContain("사용 가능", denied.detail);
            StringAssert.Contains("폰 센서로만", denied.lead);
        }
    }
}
