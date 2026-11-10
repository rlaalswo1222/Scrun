using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 앱 씬(Scrun → Build App Scene)의 시작점. 화면 틀(AppShell.uxml)을 띄우고, 화면에 따라
    // 세로 고정·가로 전환, 노치·홈 바 영역(안전 영역) 띄우기, 뒤로 가기(에디터 Esc)를 맞춘다.
    // 다른 코드에서는 AppRoot.Nav로 화면을 바꾼다.
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(-1000)] // Awake에서 UIDocument가 패널을 만들기 전에 패널 설정 복사본을 끼움
    public sealed class AppRoot : MonoBehaviour
    {
        // 세로 기준 크기(아이폰 14·15 = 390×844pt, Figma 프레임과 같은 크기라고 가정). 패널 설정 에셋도 이 값으로 만든다
        public static readonly Vector2Int PortraitReference = new Vector2Int(390, 844);

        public static Navigator Nav { get; private set; }

        UIDocument doc;
        PanelSettings panel; // 실행 중에만 쓰는 복사본(원본 에셋을 바꾸지 않게)
        VisualElement safeArea;
        VisualElement appRoot;
        VisualElement tabBar;
        bool started;
        bool pendingInit;
        bool subscribed;
        bool landscape;
        bool orientationApplied;
        // 안전 영역을 마지막으로 맞춘 조건(화면 크기·안전 영역·패널 크기·탭 바 보임 중 하나라도 바뀌면 다시)
        Rect lastSafe;
        Vector2Int lastScreen;
        Vector2 lastPanelSize;
        bool lastTabsShown;

        void Awake()
        {
            doc = GetComponent<UIDocument>();
            panel = Instantiate(doc.panelSettings);
            panel.name = doc.panelSettings.name + " (실행 중)";
            doc.panelSettings = panel;
        }

        // 화면 틀은 UIDocument가 UXML을 펼친 뒤(Start)에 잡는다. 껐다 켜면(OnEnable) UIDocument가 틀을 다시 만들므로 다시 잡는다
        void Start()
        {
            started = true;
            Init();
        }

        void OnEnable()
        {
            // 다시 켤 때는 UIDocument가 이 뒤에 틀을 다시 펼치므로 다음 프레임에 잡는다(이 스크립트가 먼저 불림)
            if (started) pendingInit = true;
        }

        void Init()
        {
            var root = doc.rootVisualElement;
            safeArea = root.Q("safe-area");
            appRoot = root.Q("app-root");
            tabBar = root.Q("tab-bar");
            Nav = new Navigator(root.Q("screen-host"), tabBar, TabRoot);
            Nav.Changed += OnScreenChanged;
            orientationApplied = false;
            lastPanelSize = default;
            Feedback.Attach(root);
            Feedback.OnNotLoggedIn = GoToLogin;

            // 저장된 로그인이 있으면 바로 홈, 없으면 로그인부터(토큰이 실제로 살아 있는지는 첫 서버 호출 때 재발급으로 확인)
            bool loggedIn = false;
            try
            {
                Api.Session.Expired += OnSessionExpired;
                subscribed = true;
                loggedIn = Api.Session.IsLoggedIn;
            }
            catch (Exception e)
            {
                // 기기 기능이 없는 빌드(PlatformNotSupportedException)·네이티브 초기화 실패: 빈 화면 대신 로그인 화면을 띄우고, 누르면 안내가 뜬다
                Debug.LogException(e);
            }
            if (loggedIn) Onboarding.Begin(Nav); // 모션 권한을 아직 안 물어봤으면 권한 안내부터(계획 0025)
            else Nav.StartFlow(new LoginScreen());
        }

        void OnDisable()
        {
            if (subscribed) Api.Session.Expired -= OnSessionExpired;
            subscribed = false;
            Feedback.OnNotLoggedIn = null;
            Feedback.Detach();
            Nav?.Dispose(); // 화면마다 OnHide·OnDestroy(구독 해제)가 불리게
            Nav = null;
        }

        void OnSessionExpired() => GoToLogin("다시 로그인해 주세요.");

        // 로그인이 끝났을 때(재발급 거절·가입용 토큰 만료): 지금 화면을 모두 정리하고 로그인부터
        static void GoToLogin(string notice)
        {
            if (Nav == null) return;
            Feedback.HideError();
            try { Api.Session.Clear(); } catch (Exception e) { Debug.LogException(e); } // 만료된 가입용 토큰까지 정리. 실패해도 로그인 화면으로는 간다
            if (!Nav.InTabs && Nav.Current is LoginScreen) return;
            Nav.StartFlow(new LoginScreen(notice));
        }

        void OnDestroy()
        {
            if (panel != null) Destroy(panel);
        }

        static AppScreen TabRoot(AppTab tab) => tab switch
        {
            AppTab.Home => new HomeScreen(),
            AppTab.Records => new PlaceholderScreen("기록", "달린 기록 목록이 들어갈 자리"),
            AppTab.Friends => new PlaceholderScreen("친구", "친구 목록·지금 달리는 친구가 들어갈 자리"),
            _ => new PlaceholderScreen("마이", "인증 마크·연속 일수·설정이 들어갈 자리", devTools: true),
        };

        void OnScreenChanged(AppScreen screen)
        {
            Feedback.HideError(); // 오류창은 그 화면 것 → 화면이 바뀌면 닫음(다시 시도가 사라진 화면을 부르지 않게)
            appRoot?.EnableInClassList("app-root--world", screen.ShowsWorld); // 맵: 앱 배경을 투명하게
            if (orientationApplied && screen.Landscape == landscape) return;
            orientationApplied = true;
            landscape = screen.Landscape;
            AppOrientation.Apply(landscape);
            panel.referenceResolution = landscape
                ? new Vector2Int(PortraitReference.y, PortraitReference.x)
                : PortraitReference;
        }

        void Update()
        {
            if (pendingInit)
            {
                pendingInit = false;
                Init();
            }

            // 회전하면 화면 크기가 먼저 바뀌고 패널 크기 맞춤은 한 프레임 뒤에 따라온다 → 패널 크기도 같이 보고,
            // 셋 중 하나라도 바뀌면 다시 맞춘다
            if (safeArea?.panel != null)
            {
                var screen = new Vector2Int(Screen.width, Screen.height);
                var panelSize = safeArea.panel.visualTree.layout.size;
                bool tabsShown = tabBar != null && SafeArea.BarShown(tabBar);
                if (Screen.safeArea != lastSafe || screen != lastScreen || panelSize != lastPanelSize || tabsShown != lastTabsShown)
                {
                    if (SafeArea.Apply(safeArea, tabBar))
                    {
                        lastSafe = Screen.safeArea;
                        lastScreen = screen;
                        lastPanelSize = panelSize;
                        lastTabsShown = tabsShown;
                    }
                }
            }

            // 뒤로 가기: 에디터·안드로이드는 Esc/뒤로 키. 아이폰은 화면의 뒤로 버튼(각 화면이 Nav.Pop)
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !Feedback.IsBlocking && Nav != null) // 로딩·오류창 중엔 막음
            {
                if (Nav.Current == null || !Nav.Current.OnBack()) Nav.Pop(); // 화면이 직접 처리하면(확인 묻기 등) 닫지 않음
            }
        }
    }
}
