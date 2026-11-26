using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // A-01 스플래시(계획 0030): 시간이 다 되면 다음으로 한 번만, 그 사이 다른 화면이 떴으면 안 넘어감, 화면 배경 클래스
    public class SplashTests
    {
        sealed class Other : AppScreen
        {
            protected override string Uxml => "UI/Placeholder";
        }

        Navigator nav;
        int nextCalls;

        [SetUp]
        public void SetUp()
        {
            var tabBar = new VisualElement();
            foreach (var n in new[] { "home", "records", "friends", "me" }) tabBar.Add(new Button { name = "tab-" + n });
            nav = new Navigator(new VisualElement(), tabBar, _ => new Other());
            nextCalls = 0;
        }

        SplashScreen Show()
        {
            var splash = new SplashScreen(() => nextCalls++);
            nav.StartFlow(splash);
            return splash;
        }

        [Test]
        public void 시간이_다_되면_다음으로_한_번만()
        {
            var splash = Show();
            splash.Finish();
            splash.Finish();
            Assert.AreEqual(1, nextCalls);
        }

        [Test]
        public void 그_사이_다른_화면이_뜨면_넘어가지_않는다()
        {
            var splash = Show();
            nav.StartFlow(new Other()); // 예: 로그인 만료로 로그인 화면
            splash.Finish();
            Assert.AreEqual(0, nextCalls);
        }

        [Test]
        public void 화면은_로그인에서_카카오_버튼만_뺀_모양()
        {
            var splash = Show();
            var root = splash.Root;
            Assert.IsNotNull(root.Q(className: "ob-login-logo"), "로그인과 같은 로고");
            Assert.IsNotNull(root.Q(className: "ob-login-planet"), "로그인과 같은 행성");
            Assert.IsTrue(root.Q("fit").ClassListContains(SplashScreen.ArtClass), "로고·행성은 페이드인");
            // 글·버튼 칸은 자리만 남기고 숨김(visibility) → 로고·행성이 로그인과 같은 자리
            Assert.IsTrue(root.Q(className: "ob-login-text").ClassListContains(SplashScreen.HoldClass));
            Assert.IsTrue(root.Q(className: "ob-login-foot").ClassListContains(SplashScreen.HoldClass));
            Assert.IsFalse(root.Q<Button>("kakao").enabledSelf, "숨긴 카카오 버튼은 못 누름");
            Assert.AreEqual(DisplayStyle.None, root.Q("notice").style.display.value, "알림 줄은 로그인처럼 없을 때 안 보임");
            Assert.AreEqual(SpaceArt.LoginRootClass, splash.RootClass, "바탕도 로그인과 같게(노치·홈 바까지)");
            Assert.IsTrue(splash.HidesTabBar);
        }

        [Test]
        public void 스플래시_USS가_붙는다()
        {
            var splash = Show();
            var uss = UnityEngine.Resources.Load<StyleSheet>("UI/Splash");
            Assert.IsNotNull(uss);
            Assert.IsTrue(splash.Root.styleSheets.Contains(uss), "페이드인·숨김 규칙");
        }

        [Test]
        public void 로그인_안_됐으면_스플래시_다음은_로그인()
        {
            Show();
            AppRoot.AfterSplash(nav, loggedIn: false);
            Assert.IsInstanceOf<LoginScreen>(nav.Current);
        }

        [Test]
        public void 로그인됐으면_스플래시_다음은_로그인이_아니다()
        {
            Show();
            AppRoot.AfterSplash(nav, loggedIn: true); // 권한 안내(계획 0025) 또는 홈 — 이 기기 상태에 따라
            Assert.IsNotInstanceOf<LoginScreen>(nav.Current);
            Assert.IsNotInstanceOf<SplashScreen>(nav.Current);
        }

        [Test]
        public void 다른_화면은_앱_배경을_바꾸지_않는다() => Assert.IsNull(new Other().RootClass);

        [Test]
        public void 보이는_시간은_2에서_3초_다_보이는_건_1초_이상()
        {
            Assert.That(SplashScreen.ShowMs, Is.InRange(2000, 3000), "Figma 노트 1–2초에서 1초 늘림(11/25)");
            Assert.That(SplashScreen.ShowMs - SplashScreen.FadeMs, Is.GreaterThanOrEqualTo(1000), "페이드 뒤 다 보이는 시간");
        }
    }
}
