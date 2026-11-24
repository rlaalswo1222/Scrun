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

        // 화면 넘김 움직임(계획 0068). null이면 바로 바뀜(테스트·처음 상태). AppRoot가 붙인다
        public ScreenTransitions Transitions { get; set; }

        // 탭 바에 보이는 순서(왼쪽 → 오른쪽). 탭 넘김 방향을 정함
        static readonly AppTab[] TabOrder = { AppTab.Home, AppTab.Friends, AppTab.Records, AppTab.Me };

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
            var motion = MotionBetween(prev, first, NavMotion.Fade);
            Hide(prev, motion);
            DestroyAll();
            flow = new List<AppScreen> { first };
            ShowTop(motion);
        }

        // 흐름을 끝내고 탭으로(예: 가입이 끝나면 홈)
        public void ShowTabs(AppTab tab = AppTab.Home)
        {
            var prev = Current; // 만들기 전에 잡는다(처음엔 새로 만든 탭 화면이 '지금 화면'으로 보이므로)
            EnsureTab(tab);
            var motion = MotionBetween(prev, TopOf(tab), NavMotion.Fade);
            Hide(prev, motion);
            if (flow != null)
            {
                foreach (var s in flow) Destroy(s);
                flow = null;
            }
            SetTab(tab);
            ShowTop(motion);
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
            var from = CurrentTab;
            EnsureTab(tab);
            var motion = MotionBetween(prev, TopOf(tab),
                Array.IndexOf(TabOrder, tab) > Array.IndexOf(TabOrder, from) ? NavMotion.TabRight : NavMotion.TabLeft);
            Hide(prev, motion);
            SetTab(tab);
            ShowTop(motion);
        }

        public void Push(AppScreen screen)
        {
            var stack = Stack ?? throw new InvalidOperationException("StartFlow나 ShowTabs를 먼저 부르세요.");
            var prev = Current;
            Create(screen);
            var motion = MotionBetween(prev, screen, NavMotion.Push);
            Hide(prev, motion);
            stack.Add(screen);
            ShowTop(motion);
        }

        // 뒤로 갈 화면이 있는지(지금 화면이 맨 아래가 아님). 화면이 뒤로 버튼을 숨길지 정할 때
        public bool CanPop => Stack != null && Stack.Count > 1;

        // 하나 뒤로. 맨 아래 화면이면 아무것도 안 하고 false
        public bool Pop()
        {
            var stack = Stack;
            if (stack == null || stack.Count <= 1) return false;
            var top = stack[stack.Count - 1];
            var motion = MotionBetween(top, stack[stack.Count - 2], NavMotion.Back);
            Hide(top, motion);
            stack.RemoveAt(stack.Count - 1);
            Destroy(top);
            ShowTop(motion);
            return true;
        }

        // 맨 아래 화면만 남기고 정리(중간 화면은 다시 보이지 않고 바로 없어짐)
        public void PopToRoot()
        {
            var stack = Stack;
            if (stack == null || stack.Count <= 1) return;
            var motion = MotionBetween(Current, stack[0], NavMotion.Back);
            Hide(Current, motion);
            for (int i = stack.Count - 1; i >= 1; i--)
            {
                Destroy(stack[i]);
                stack.RemoveAt(i);
            }
            ShowTop(motion);
        }

        // 지금 탭에 쌓인 화면(예: 결과 화면들)을 정리하고 tab의 첫 화면으로. tab에 전에 쌓아 둔 화면도 정리한다.
        // SelectTab 뒤 PopToRoot와 달리 중간 화면(남의 탭에 옛 상세 등)을 잠깐 보이지 않음(OnShow로 요청을 보내고 바로 없어지던 것, #94 리뷰)
        public void ShowTabRoot(AppTab tab)
        {
            if (!InTabs) return;
            var prev = Current;
            EnsureTab(tab);
            var motion = MotionBetween(prev, tabStacks[tab][0], NavMotion.Fade);
            Hide(prev, motion);
            TrimToRoot(CurrentTab);
            TrimToRoot(tab);
            SetTab(tab);
            ShowTop(motion);
        }

        void TrimToRoot(AppTab tab)
        {
            if (!tabStacks.TryGetValue(tab, out var stack)) return;
            for (int i = stack.Count - 1; i >= 1; i--)
            {
                Destroy(stack[i]);
                stack.RemoveAt(i);
            }
        }

        // 앱 화면을 끌 때: 지금 화면을 숨기고 전부 정리
        public void Dispose()
        {
            Hide(Current, NavMotion.None);
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
            s.NavShown = false;
            host.Add(s.Root);
        }

        // 화면 로직은 바로 숨김(OnHide), 보이는 것은 움직임이 끝난 뒤 display: none
        void Hide(AppScreen s, NavMotion motion)
        {
            if (s == null || !s.NavShown) return; // 이미 숨겨졌거나 아직 안 보인 화면
            s.NavShown = false;
            var root = s.Root;
            if (Transitions == null) root.style.display = DisplayStyle.None;
            else Transitions.Leave(root, motion, () => { if (!s.NavShown) root.style.display = DisplayStyle.None; });
            s.OnHide();
        }

        // 가로 화면(맵)으로 오가거나 처음 띄울 때는 회전·첫 화면이라 움직이지 않음(흐려짐만)
        static NavMotion MotionBetween(AppScreen from, AppScreen to, NavMotion wanted)
        {
            if (from == null || to == null) return NavMotion.Fade;
            if (from.Landscape || to.Landscape || from.ShowsWorld || to.ShowsWorld) return NavMotion.None;
            return wanted;
        }

        AppScreen TopOf(AppTab tab) =>
            tabStacks.TryGetValue(tab, out var s) && s.Count > 0 ? s[s.Count - 1] : null;

        void ShowTop(NavMotion motion = NavMotion.None)
        {
            var cur = Current;
            if (cur == null) return;
            cur.NavShown = true;
            cur.Root.style.display = DisplayStyle.Flex;
            cur.Root.BringToFront(); // 들어오는 화면이 나가는 화면 위(뒤로 갈 때는 나가는 화면이 위에서 밀려나야 하므로 아래)
            if (motion == NavMotion.Back) cur.Root.SendToBack();
            Transitions?.Enter(cur.Root, motion);
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

        void Destroy(AppScreen s)
        {
            s.OnDestroy();
            if (Transitions == null || !Transitions.RemoveWhenDone(s.Root)) s.Root.RemoveFromHierarchy(); // 나가는 중이면 끝난 뒤 뗌
        }
    }
}
