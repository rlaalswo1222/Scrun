using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0031: 온보딩 A-02–A-19 흐름과 화면 규칙(Figma 노트)
    public class OnboardingFigTests
    {
        sealed class Other : AppScreen
        {
            protected override string Uxml => "UI/Placeholder";
        }

        Navigator nav;

        [SetUp]
        public void SetUp()
        {
            var tabBar = new VisualElement();
            foreach (var n in new[] { "home", "records", "friends", "me" }) tabBar.Add(new Button { name = "tab-" + n });
            nav = new Navigator(new VisualElement(), tabBar, _ => new Other());
        }

        // ---- 흐름 판단 ----
        [Test]
        public void 스플래시_다음은_로그인됨이면_온보딩_처음이면_소개_아니면_로그인()
        {
            Assert.AreEqual(StartStep.Onboarding, Onboarding.AfterSplash(loggedIn: true, introSeen: false));
            Assert.AreEqual(StartStep.Intro, Onboarding.AfterSplash(loggedIn: false, introSeen: false));
            Assert.AreEqual(StartStep.Login, Onboarding.AfterSplash(loggedIn: false, introSeen: true));
        }

        [Test]
        public void 권한을_안_끝냈으면_권한부터_가입_직후면_주간_목표_아니면_홈()
        {
            Assert.AreEqual(OnboardingStep.Permissions, Onboarding.Step(done: false, goalPending: true));
            Assert.AreEqual(OnboardingStep.WeeklyGoal, Onboarding.Step(done: true, goalPending: true));
            Assert.AreEqual(OnboardingStep.Home, Onboarding.Step(done: true, goalPending: false));
        }

        [Test]
        public void 처음_켠_기기는_스플래시_다음_소개()
        {
            nav.StartFlow(new Other());
            AppRoot.AfterSplash(nav, loggedIn: false, introSeen: false);
            Assert.IsInstanceOf<IntroScreen>(nav.Current);
        }

        // ---- A-02–A-04 소개 ----
        [Test]
        public void 소개는_세_장_마지막만_시작하기()
        {
            var intro = new IntroScreen();
            nav.StartFlow(intro);
            Assert.AreEqual(3, IntroScreen.Pages.Length);
            Assert.AreEqual("다음", intro.Root.Q<Button>("next").text);
            intro.Next();
            intro.Next();
            Assert.AreEqual(2, intro.PageIndex);
            Assert.AreEqual("기록이 남고, 이어져요", intro.Root.Q<Label>("title").text);
            Assert.AreEqual("시작하기", intro.Root.Q<Button>("next").text);
            Assert.AreEqual(Visibility.Hidden, intro.Root.Q<Button>("skip").style.visibility.value, "A-04는 건너뛰기 없음");
            var dots = intro.Root.Q("dots");
            Assert.IsTrue(dots[2].ClassListContains("dot--on"));
            Assert.IsFalse(dots[0].ClassListContains("dot--on"));
        }

        [Test]
        public void 소개에서_뒤로는_앞_장_첫_장에선_닫지_않음은_화면에_맡김()
        {
            var intro = new IntroScreen();
            nav.StartFlow(intro);
            Assert.IsFalse(intro.OnBack(), "첫 장");
            intro.Next();
            Assert.IsTrue(intro.OnBack());
            Assert.AreEqual(0, intro.PageIndex);
        }

        // ---- A-05·A-19 로그인 ----
        [Test]
        public void 로그인과_실패는_다른_화면()
        {
            var login = new LoginScreen();
            nav.StartFlow(login);
            Assert.AreEqual("카카오로 계속하기", login.Root.Q<Button>("kakao").text);
            var failed = new LoginScreen(failed: true);
            nav.Push(failed);
            Assert.AreEqual("카카오로 다시 시도", failed.Root.Q<Button>("kakao").text);
            Assert.IsTrue(failed.Failed);
        }

        // ---- A-18 약관 ----
        static List<Agreement> Agreements() => new List<Agreement>
        {
            new Agreement { AgreementId = "terms", Title = "서비스 이용약관 동의", Required = true, Url = "https://example.com/t" },
            new Agreement { AgreementId = "privacy", Title = "개인정보 수집·이용 동의", Required = true, Url = "https://example.com/p" },
            new Agreement { AgreementId = "age14", Title = "만 14세 이상입니다", Required = true, Url = "" },
            new Agreement { AgreementId = "notice", Title = "운동 알림 받기", Required = false, Url = "https://example.com/n" },
        };

        [Test]
        public void 약관_필수를_다_골라야_계속()
        {
            var terms = new TermsScreen();
            nav.StartFlow(terms);
            terms.Show(Agreements());
            Assert.IsFalse(terms.CanGoNext);
            terms.Toggle("terms");
            terms.Toggle("privacy");
            Assert.IsFalse(terms.CanGoNext, "만 14세 빠짐");
            terms.Toggle("age14");
            Assert.IsTrue(terms.CanGoNext, "선택(알림)은 없어도 됨");
            Assert.IsTrue(terms.Root.Q<Button>("next").enabledSelf);
        }

        [Test]
        public void 전체_동의는_전부_켜고_다시_누르면_전부_끔()
        {
            var terms = new TermsScreen();
            nav.StartFlow(terms);
            terms.Show(Agreements());
            terms.ToggleAll();
            Assert.IsTrue(terms.CanGoNext);
            Assert.IsTrue(terms.Root.Q("all-check").ClassListContains("check--on"));
            terms.ToggleAll();
            Assert.IsFalse(terms.CanGoNext);
        }

        [Test]
        public void 약관_줄_문구와_보기()
        {
            var a = Agreements();
            Assert.AreEqual("[필수] 서비스 이용약관 동의", TermsText.Title(a[0]));
            Assert.AreEqual("[선택] 운동 알림 받기", TermsText.Title(a[3]));
            Assert.AreEqual("센서 데이터, 계기판 인증 결과, 프로필(키·몸무게)", TermsText.Detail("privacy"));
            Assert.IsNull(TermsText.Detail("terms"));
            var terms = new TermsScreen();
            nav.StartFlow(terms);
            terms.Show(a);
            var rows = terms.Root.Q("list");
            Assert.AreEqual(4, rows.childCount);
            Assert.IsNull(rows[2].Q<Button>(className: "agree-view"), "주소 없는 만 14세는 보기 없음");
            Assert.IsNotNull(rows[0].Q<Button>(className: "agree-view"));
        }

        [Test]
        public void 약관이_비었으면_계속_못_함() => Assert.IsFalse(TermsText.CanContinue(new List<Agreement>(), new HashSet<string>()));

        // ---- A-16 주간 목표 ----
        [Test]
        public void 주간_목표는_1에서_7_기본_3()
        {
            var goal = new WeeklyGoalScreen();
            nav.StartFlow(goal);
            Assert.AreEqual(3, goal.Count);
            Assert.AreEqual(7, goal.Root.Q("chips").childCount);
            Assert.AreEqual(7, goal.Root.Q("days").childCount);
            goal.Set(5);
            Assert.AreEqual("5", goal.Root.Q<Label>("count").text);
            Assert.IsTrue(goal.Root.Q("chips")[4].ClassListContains("goal-chip--on"));
            goal.Set(0);
            goal.Set(8);
            Assert.AreEqual(5, goal.Count, "범위 밖은 무시");
        }

        [Test]
        public void 주간_목표가_첫_화면이면_뒤로_버튼을_숨김()
        {
            var goal = new WeeklyGoalScreen();
            nav.StartFlow(goal);
            Assert.AreEqual(Visibility.Hidden, goal.Root.Q<Button>("back").style.visibility.value);
            nav.StartFlow(new Other());
            var pushed = new WeeklyGoalScreen();
            nav.Push(pushed);
            Assert.AreEqual(Visibility.Visible, pushed.Root.Q<Button>("back").style.visibility.value);
        }

        // ---- A-17 완료 ----
        [Test]
        public void 완료_화면은_스플래시_배경()
        {
            var done = new OnboardingDoneScreen();
            nav.StartFlow(done);
            Assert.AreEqual("app-root--splash", done.RootClass);
            Assert.AreEqual("홈으로", done.Root.Q<Button>("home").text);
        }

        // ---- A-06 프로필 ----
        [Test]
        public void 프로필_설명은_지금_쓰지_않는_용도를_말하지_않는다()
        {
            StringAssert.DoesNotContain("에만 사용", ProfileForm.Lead);
            StringAssert.DoesNotContain("아바타 크기", ProfileForm.Lead);
        }
    }
}
