using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Scrun.App
{
    public enum AppTab { Home, Records, Friends, Me }

    // 화면 전환. 두 가지 모드:
    //  - 흐름(Flow): 로그인·온보딩·달리기처럼 아래 탭 없이 한 줄로 쌓는 화면들(StartFlow)
    //  - 탭(Tabs): 홈·친구·기록·마이. 탭마다 따로 쌓이고, 탭을 바꿔도 각 탭의 쌓인 화면은 남는다(ShowTabs)
    // Push는 지금 모드의 맨 위에 쌓고, Pop은 하나 뒤로(맨 아래면 false).
    // 새 화면은 먼저 만들어 본 뒤에 바꾼다(만들다 실패하면 지금 화면이 그대로 남음).
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
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.tabBar = tabBar ?? throw new ArgumentNullException(nameof(tabBar));
            this.tabRoot = tabRoot;
            foreach (AppTab t in Enum.GetValues(typeof(AppTab)))
            {
                var name = "tab-" + t.ToString().ToLowerInvariant();
                var b = tabBar.Q<Button>(name) ?? throw new InvalidOperationException($"탭 버튼 {name}이 없음");
                var tab = t;
                b.clicked += () => SelectTab(tab);
                tabButtons[t] = b;
            }
        }

        // 아래 탭 없는 흐름을 새로 시작(그 전 화면은 탭까지 전부 정리)
        public void StartFlow(AppScreen first)
        {
            var prev = Current;
            Create(first);
            Hide(prev);
            DestroyAll();
            flow = new List<AppScreen> { first };
            ShowTop();
        }

        // 흐름을 끝내고 탭으로(예: 가입이 끝나면 홈)
        public void ShowTabs(AppTab tab = AppTab.Home)
        {
            var prev = Current; // 만들기 전에 잡는다(처음엔 새로 만든 탭 화면이 '지금 화면'으로 보이므로)
            EnsureTab(tab);
            Hide(prev);
            if (flow != null)
            {
                foreach (var s in flow) Destroy(s);
                flow = null;
            }
            SetTab(tab);
            ShowTop();
        }

        // 탭 버튼. 지금 탭을 다시 누르면 그 탭의 첫 화면으로
        public void SelectTab(AppTab tab)
        {
            if (!InTabs) return;
            if (tab == CurrentTab && tabStacks.ContainsKey(tab))
            {
                PopToRoot();
                return;
            }
            var prev = Current;
            EnsureTab(tab);
            Hide(prev);
            SetTab(tab);
            ShowTop();
        }

        public void Push(AppScreen screen)
        {
            var stack = Stack ?? throw new InvalidOperationException("StartFlow나 ShowTabs를 먼저 부르세요.");
            var prev = Current;
            Create(screen);
            Hide(prev);
            stack.Add(screen);
            ShowTop();
        }

        // 하나 뒤로. 맨 아래 화면이면 아무것도 안 하고 false
        public bool Pop()
        {
            var stack = Stack;
            if (stack == null || stack.Count <= 1) return false;
            HideCurrent();
            var top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            Destroy(top);
            ShowTop();
            return true;
        }

        // 맨 아래 화면만 남기고 정리(중간 화면은 다시 보이지 않고 바로 없어짐)
        public void PopToRoot()
        {
            var stack = Stack;
            if (stack == null || stack.Count <= 1) return;
            HideCurrent();
            for (int i = stack.Count - 1; i >= 1; i--)
            {
                Destroy(stack[i]);
                stack.RemoveAt(i);
            }
            ShowTop();
        }

        // 앱 화면을 끌 때: 지금 화면을 숨기고 전부 정리
        public void Dispose()
        {
            HideCurrent();
            DestroyAll();
            Changed = null;
        }

        // 탭 첫 화면이 없으면 만들어 둔다(아직 보이지 않음)
        void EnsureTab(AppTab tab)
        {
            if (tabStacks.ContainsKey(tab)) return;
            var root = tabRoot(tab);
            Create(root);
            tabStacks[tab] = new List<AppScreen> { root };
        }

        void SetTab(AppTab tab)
        {
            CurrentTab = tab;
            foreach (var kv in tabButtons) kv.Value.EnableInClassList("tab--selected", kv.Key == tab);
        }

        void Create(AppScreen s)
        {
            s.Create(this); // 실패하면 여기서 예외 → 지금 화면은 그대로
            s.Root.style.display = DisplayStyle.None;
            host.Add(s.Root);
        }

        void HideCurrent() => Hide(Current);

        static void Hide(AppScreen s)
        {
            if (s == null || s.Root.style.display == DisplayStyle.None) return; // 이미 숨겨졌거나 아직 안 보인 화면
            s.Root.style.display = DisplayStyle.None;
            s.OnHide();
        }

        void ShowTop()
        {
            var cur = Current;
            if (cur == null) return;
            cur.Root.style.display = DisplayStyle.Flex;
            tabBar.style.display = InTabs && !cur.HidesTabBar ? DisplayStyle.Flex : DisplayStyle.None;
            cur.OnShow();
            // OnShow 안에서 다른 화면으로 넘어갔으면 그쪽이 이미 알렸으므로 옛 화면으로 다시 알리지 않는다
            if (Current == cur) Changed?.Invoke(cur);
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
