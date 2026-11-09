using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Scrun.App
{
    public enum AppTab { Home, Records, Friends, Me }

    // 화면 전환. 두 가지 모드:
    //  - 흐름(Flow): 로그인·온보딩·달리기처럼 아래 탭 없이 한 줄로 쌓는 화면들(StartFlow)
    //  - 탭(Tabs): 홈·기록·친구·내 정보. 탭마다 따로 쌓이고, 탭을 바꿔도 각 탭의 쌓인 화면은 남는다(ShowTabs)
    // Push는 지금 모드의 맨 위에 쌓고, Pop은 하나 뒤로(맨 아래면 false).
    public sealed class Navigator
    {
        readonly VisualElement host;
        readonly VisualElement tabBar;
        readonly Func<AppTab, AppScreen> tabRoot;
        readonly Dictionary<AppTab, List<AppScreen>> tabStacks = new Dictionary<AppTab, List<AppScreen>>();
        readonly Dictionary<AppTab, Button> tabButtons = new Dictionary<AppTab, Button>();
        List<AppScreen> flow; // 흐름 모드면 null이 아님

        public AppTab CurrentTab { get; private set; }
        public bool InTabs => flow == null;
        public AppScreen Current => Stack != null && Stack.Count > 0 ? Stack[Stack.Count - 1] : null;

        // 맨 위 화면이 바뀔 때마다(가로·세로, 탭 바 표시를 여기서 맞춤)
        public event Action<AppScreen> Changed;

        List<AppScreen> Stack => flow ?? (tabStacks.TryGetValue(CurrentTab, out var s) ? s : null);

        // tabBar 안의 버튼 이름: tab-home, tab-records, tab-friends, tab-me
        public Navigator(VisualElement host, VisualElement tabBar, Func<AppTab, AppScreen> tabRoot)
        {
            this.host = host;
            this.tabBar = tabBar;
            this.tabRoot = tabRoot;
            foreach (AppTab t in Enum.GetValues(typeof(AppTab)))
            {
                var b = tabBar.Q<Button>("tab-" + t.ToString().ToLowerInvariant())
                        ?? throw new InvalidOperationException($"탭 버튼 tab-{t.ToString().ToLowerInvariant()}이 없음");
                var tab = t;
                b.clicked += () => SelectTab(tab);
                tabButtons[t] = b;
            }
        }

        // 아래 탭 없는 흐름을 새로 시작(그 전 화면은 탭까지 전부 정리)
        public void StartFlow(AppScreen first)
        {
            HideCurrent();
            DestroyAll();
            flow = new List<AppScreen>();
            Add(first);
            ShowTop();
        }

        // 흐름을 끝내고 탭으로(예: 가입이 끝나면 홈)
        public void ShowTabs(AppTab tab = AppTab.Home)
        {
            if (flow != null)
            {
                HideCurrent();
                foreach (var s in flow) Destroy(s);
                flow = null;
            }
            SwitchTab(tab, popToRootIfSame: false);
        }

        // 탭 버튼. 지금 탭을 다시 누르면 그 탭의 첫 화면으로
        public void SelectTab(AppTab tab)
        {
            if (!InTabs) return;
            SwitchTab(tab, popToRootIfSame: true);
        }

        public void Push(AppScreen screen)
        {
            if (Stack == null) throw new InvalidOperationException("StartFlow나 ShowTabs를 먼저 부르세요.");
            HideCurrent();
            Add(screen);
            ShowTop();
        }

        // 하나 뒤로. 맨 아래 화면이면 아무것도 안 하고 false
        public bool Pop()
        {
            var stack = Stack;
            if (stack == null || stack.Count <= 1) return false;
            var top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            top.OnHide();
            Destroy(top);
            ShowTop();
            return true;
        }

        void SwitchTab(AppTab tab, bool popToRootIfSame)
        {
            if (popToRootIfSame && tab == CurrentTab && tabStacks.ContainsKey(tab))
            {
                while (Pop()) { }
                return;
            }
            HideCurrent();
            CurrentTab = tab;
            if (!tabStacks.ContainsKey(tab))
            {
                tabStacks[tab] = new List<AppScreen>();
                Add(tabRoot(tab));
            }
            foreach (var kv in tabButtons) kv.Value.EnableInClassList("tab--selected", kv.Key == tab);
            ShowTop();
        }

        void Add(AppScreen s)
        {
            s.Create(this);
            s.Root.style.display = DisplayStyle.None;
            host.Add(s.Root);
            Stack.Add(s);
        }

        void HideCurrent()
        {
            var cur = Current;
            if (cur == null) return;
            cur.Root.style.display = DisplayStyle.None;
            cur.OnHide();
        }

        void ShowTop()
        {
            var cur = Current;
            if (cur == null) return;
            cur.Root.style.display = DisplayStyle.Flex;
            tabBar.style.display = InTabs && !cur.HidesTabBar ? DisplayStyle.Flex : DisplayStyle.None;
            cur.OnShow();
            Changed?.Invoke(cur);
        }

        void DestroyAll()
        {
            if (flow != null) foreach (var s in flow) Destroy(s);
            foreach (var stack in tabStacks.Values) foreach (var s in stack) Destroy(s);
            tabStacks.Clear();
            flow = null;
        }

        static void Destroy(AppScreen s)
        {
            s.OnDestroy();
            s.Root.RemoveFromHierarchy();
        }
    }
}
