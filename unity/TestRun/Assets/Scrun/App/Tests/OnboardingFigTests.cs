using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0031: 온보딩 A-05–A-19 흐름과 화면 규칙(Figma 노트). 소개 A-02–A-04는 계획 0068에서 뺌
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
        public void 스플래시_다음은_로그인됨이면_온보딩_아니면_로그인()
        {
            Assert.AreEqual(StartStep.Onboarding, Onboarding.AfterSplash(loggedIn: true));
            Assert.AreEqual(StartStep.Login, Onboarding.AfterSplash(loggedIn: false));
            CollectionAssert.AreEquivalent(new[] { StartStep.Onboarding, StartStep.Login },
                (StartStep[])System.Enum.GetValues(typeof(StartStep)), "소개(A-02–A-04) 단계는 없음(계획 0068)");
        }

        [Test]
        public void 권한을_안_끝냈으면_권한부터_가입_직후면_주간_목표_아니면_홈()
        {
            Assert.AreEqual(OnboardingStep.Permissions, Onboarding.Step(done: false, goalPending: true));
            Assert.AreEqual(OnboardingStep.WeeklyGoal, Onboarding.Step(done: true, goalPending: true));
            Assert.AreEqual(OnboardingStep.Home, Onboarding.Step(done: true, goalPending: false));
        }

        [Test]
        public void 로그인_안_된_기기는_처음_켜도_스플래시_다음_바로_로그인()
        {
            nav.StartFlow(new Other());
            AppRoot.AfterSplash(nav, loggedIn: false);
            Assert.IsInstanceOf<LoginScreen>(nav.Current);
            Assert.IsFalse(((LoginScreen)nav.Current).Failed);
            Assert.IsFalse(nav.CanPop, "로그인이 흐름의 맨 아래(스플래시·소개로 돌아가지 않음)");
        }

        // ---- A-05·A-19 로그인 ----
        [Test]
        public void 로그인과_실패는_다른_화면()
        {
            var login = new LoginScreen();
            nav.StartFlow(login);
            Assert.IsNotNull(login.Root.Q<Button>("kakao"));
            Assert.AreEqual("카카오로 계속하기", login.Root.Q<Label>("kakao-label").text, "우주 시안: 심볼 + 글자 한 줄");
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
            Assert.AreEqual(7, goal.Root.Q("days").childCount);
            Assert.IsTrue(goal.Root.Q("days")[0].ClassListContains("week-day--today"), "오늘 = 1일");
            goal.Set(5);
            Assert.AreEqual("5", goal.Root.Q<Label>("count").text);
            goal.Set(0);
            goal.Set(8);
            Assert.AreEqual(5, goal.Count, "범위 밖은 무시");
        }

        [Test]
        public void 주간_목표_빼기_더하기는_1과_7에서_멈춤()
        {
            var goal = new WeeklyGoalScreen();
            nav.StartFlow(goal);
            var minus = goal.Root.Q<Button>("minus");
            var plus = goal.Root.Q<Button>("plus");
            goal.Set(1);
            Assert.IsFalse(minus.enabledSelf);
            Assert.IsTrue(plus.enabledSelf);
            goal.Set(7);
            Assert.IsTrue(minus.enabledSelf);
            Assert.IsFalse(plus.enabledSelf);
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
        public void 완료_화면은_축하_바탕()
        {
            var done = new OnboardingDoneScreen();
            nav.StartFlow(done);
            Assert.AreEqual(SpaceArt.CelebrateRootClass, done.RootClass);
            Assert.AreEqual("홈으로", done.Root.Q<Button>("home").text);
        }

        [Test]
        public void 우주_디자인_화면의_바탕()
        {
            Assert.AreEqual(SpaceArt.LoginRootClass, new LoginScreen().RootClass, "A-05 로그인 바탕(bg_login)");
            Assert.AreEqual(SpaceArt.LoginRootClass, new LoginScreen(failed: true).RootClass, "A-19도 같은 화면 모양");
            Assert.AreEqual(SpaceArt.RootClass, new PermissionsScreen().RootClass);
            Assert.AreEqual(SpaceArt.RootClass, new WeeklyGoalScreen().RootClass);
            Assert.AreEqual(SpaceArt.RootClass, new CalibrationIntroScreen().RootClass);
        }

        [Test]
        public void 그림_칸은_자리보다_크면_줄이고_키우지는_않음()
        {
            Assert.AreEqual(1f, SpaceArt.ScaleFor(new UnityEngine.Vector2(393, 900), new UnityEngine.Vector2(393, 420)), 1e-5f);
            Assert.AreEqual(0.5f, SpaceArt.ScaleFor(new UnityEngine.Vector2(393, 210), new UnityEngine.Vector2(393, 420)), 1e-5f);
            Assert.AreEqual(0.5f, SpaceArt.ScaleFor(new UnityEngine.Vector2(196.5f, 900), new UnityEngine.Vector2(393, 420)), 1e-5f);
            Assert.AreEqual(0f, SpaceArt.ScaleFor(new UnityEngine.Vector2(393, 0), new UnityEngine.Vector2(393, 420)), 1e-5f, "자리가 없으면 안 그림");
        }

        [Test]
        public void 보정_안내_속도_칩은_실제_사다리()
        {
            var ladder = CalibrationLadder.SpeedsKmh;
            var intro = new CalibrationIntroScreen();
            nav.StartFlow(intro);
            var chips = intro.Root.Q("speeds");
            Assert.AreEqual(3, chips.childCount);
            Assert.AreEqual($"{ladder[0]:0.#} → {ladder[ladder.Length - 1]:0.#} km/h", ((Label)chips[0]).text);
            Assert.AreEqual($"{ladder.Length}단계", ((Label)chips[1]).text);
            Assert.AreEqual($"× {CalibrationLadder.StageSec:0}초", ((Label)chips[2]).text);
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
