using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 권한 안내·이어폰 흐름 판단(계획 0025)
    public class OnboardingTests
    {
        // 이어폰 화면까지 끝냈는지(done)만 본다. "나중에"로 모션을 미뤘어도 끝냈으면 다시 안 보여 줌(맵 입장 때 BeforeMap이 잡음)
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void 온보딩을_보여_줄지(bool done, bool expected) =>
            Assert.AreEqual(expected, Onboarding.NeedsOnboarding(done));

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
        public void 헤드폰_모션을_아직_안_물어봤으면_다음에_묻는다고_알린다()
        {
            var t = OnboardingText.Earphone(HeadphoneStatus.Connected, "AirPods Pro", PermissionStatus.NotDetermined);
            StringAssert.DoesNotContain("사용 가능", t.detail);
            StringAssert.Contains("다음을 누르면", t.lead);
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
