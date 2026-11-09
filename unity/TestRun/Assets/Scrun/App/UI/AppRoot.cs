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
        bool started;
        bool pendingInit;
        bool landscape;
        bool orientationApplied;
        // 안전 영역을 마지막으로 맞춘 조건(화면 크기·안전 영역·패널 크기 중 하나라도 바뀌면 다시)
        Rect lastSafe;
        Vector2Int lastScreen;
        Vector2 lastPanelSize;

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
            Nav = new Navigator(root.Q("screen-host"), root.Q("tab-bar"), TabRoot);
            Nav.Changed += OnScreenChanged;
            orientationApplied = false;
            lastPanelSize = default;
            Nav.ShowTabs(AppTab.Home); // 로그인 화면은 계획 0018의 4번에서 이 앞에 붙임
        }

        void OnDisable()
        {
            Nav?.Dispose(); // 화면마다 OnHide·OnDestroy(구독 해제)가 불리게
            Nav = null;
        }

        void OnDestroy()
        {
            if (panel != null) Destroy(panel);
        }

        static AppScreen TabRoot(AppTab tab) => tab switch
        {
            AppTab.Home => new PlaceholderScreen("홈", "달리기 시작, 이번 주 목표, 최근 기록이 들어갈 자리"),
            AppTab.Records => new PlaceholderScreen("기록", "달린 기록 목록이 들어갈 자리"),
            AppTab.Friends => new PlaceholderScreen("친구", "친구 목록·지금 달리는 친구가 들어갈 자리"),
            _ => new PlaceholderScreen("내 정보", "인증 마크·연속 일수·설정이 들어갈 자리"),
        };

        void OnScreenChanged(AppScreen screen)
        {
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
                if (Screen.safeArea != lastSafe || screen != lastScreen || panelSize != lastPanelSize)
                {
                    if (SafeArea.Apply(safeArea))
                    {
                        lastSafe = Screen.safeArea;
                        lastScreen = screen;
                        lastPanelSize = panelSize;
                    }
                }
            }

            // 뒤로 가기: 에디터·안드로이드는 Esc/뒤로 키. 아이폰은 화면의 뒤로 버튼(각 화면이 Nav.Pop)
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Nav?.Pop();
        }
    }
}
