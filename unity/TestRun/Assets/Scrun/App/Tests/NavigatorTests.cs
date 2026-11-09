using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 화면 전환 규칙: 탭마다 따로 쌓임, 같은 탭 다시 누르면 첫 화면으로, 흐름 화면은 아래 탭 없음, 화면 생명주기 순서.
    public class NavigatorTests
    {
        sealed class Probe : AppScreen
        {
            public readonly string Name;
            public readonly List<string> Log;
            public bool Hides;
            public Probe(string name, List<string> log) { Name = name; Log = log; }
            protected override string Uxml => "UI/Placeholder";
            public override bool HidesTabBar => Hides;
            protected override void OnCreate() => Log.Add(Name + ":create");
            protected internal override void OnShow() => Log.Add(Name + ":show");
            protected internal override void OnHide() => Log.Add(Name + ":hide");
            protected internal override void OnDestroy() => Log.Add(Name + ":destroy");
        }

        List<string> log;
        VisualElement host, tabBar;
        Navigator nav;

        [SetUp]
        public void SetUp()
        {
            log = new List<string>();
            host = new VisualElement();
            tabBar = new VisualElement();
            foreach (var n in new[] { "home", "records", "friends", "me" }) tabBar.Add(new Button { name = "tab-" + n });
            nav = new Navigator(host, tabBar, t => new Probe(t.ToString(), log));
        }

        static bool Visible(VisualElement e) => e.style.display != DisplayStyle.None;
        string Top => ((Probe)nav.Current).Name;

        [Test]
        public void 탭마다_따로_쌓이고_탭을_바꿔도_남는다()
        {
            nav.ShowTabs(AppTab.Home);
            nav.Push(new Probe("상세", log));
            nav.SelectTab(AppTab.Records);
            Assert.AreEqual("Records", Top);
            nav.SelectTab(AppTab.Home);
            Assert.AreEqual("상세", Top, "홈 탭에 쌓아 둔 화면이 그대로");
            Assert.IsTrue(tabBar.Q<Button>("tab-home").ClassListContains("tab--selected"));
            Assert.IsFalse(tabBar.Q<Button>("tab-records").ClassListContains("tab--selected"));
        }

        [Test]
        public void 지금_탭을_다시_누르면_그_탭_첫_화면으로()
        {
            nav.ShowTabs(AppTab.Home);
            nav.Push(new Probe("상세1", log));
            nav.Push(new Probe("상세2", log));
            nav.SelectTab(AppTab.Home);
            Assert.AreEqual("Home", Top);
            CollectionAssert.Contains(log, "상세2:destroy");
            CollectionAssert.Contains(log, "상세1:destroy");
        }

        [Test]
        public void 뒤로는_맨_아래에서_멈추고_생명주기는_순서대로()
        {
            nav.ShowTabs(AppTab.Home);
            nav.Push(new Probe("상세", log));
            Assert.IsTrue(nav.Pop());
            Assert.IsFalse(nav.Pop(), "탭 첫 화면에서는 더 못 감");
            CollectionAssert.AreEqual(new[]
            {
                "Home:create", "Home:show",
                "Home:hide", "상세:create", "상세:show",
                "상세:hide", "상세:destroy", "Home:show",
            }, log);
            Assert.AreEqual(1, host.childCount, "없어진 화면은 트리에서도 빠짐");
        }

        [Test]
        public void 흐름_화면은_아래_탭이_없고_탭으로_가면_흐름은_정리된다()
        {
            nav.StartFlow(new Probe("로그인", log));
            Assert.IsFalse(nav.InTabs);
            Assert.IsFalse(Visible(tabBar));
            nav.SelectTab(AppTab.Friends); // 흐름 중엔 탭 버튼이 무시됨
            Assert.AreEqual("로그인", Top);

            nav.ShowTabs(AppTab.Me);
            Assert.IsTrue(nav.InTabs);
            Assert.IsTrue(Visible(tabBar));
            Assert.AreEqual("Me", Top);
            CollectionAssert.Contains(log, "로그인:destroy");
        }

        [Test]
        public void 탭_안에서도_아래_탭을_숨기는_화면이_있다()
        {
            nav.ShowTabs(AppTab.Home);
            nav.Push(new Probe("전체 화면", log) { Hides = true });
            Assert.IsFalse(Visible(tabBar));
            nav.Pop();
            Assert.IsTrue(Visible(tabBar));
        }

        [Test]
        public void 흐름을_새로_시작하면_탭에_쌓인_화면도_모두_정리된다()
        {
            nav.ShowTabs(AppTab.Home);
            nav.Push(new Probe("상세", log));
            nav.StartFlow(new Probe("다시 로그인", log));
            CollectionAssert.Contains(log, "상세:destroy");
            CollectionAssert.Contains(log, "Home:destroy");
            Assert.AreEqual(1, host.childCount);
        }
    }
}
