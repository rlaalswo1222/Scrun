using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 맵이 입장하지 못해 상세 화면으로 돌려보낼 때
    public enum MapExit
    {
        OtherCourse,      // I-11 "다른 코스 보기" → 목록
        NeedsCalibration, // 보정 먼저(상세에서 안내, Figma C-03)
    }

    // 맵(가로, D-10 자리). Figma 흐름대로 가로로 돌린 뒤 입장(POST /entries)과 섬 씬 불러오기를 I-10 로딩 화면 아래에서 하고,
    // 입장이 안 되면(가득 참·없는 코스·연결 문제) I-11 카드("다른 코스 보기"·"다시 입장하기")로 알린다.
    // 서버 호출을 Feedback.Run으로 감싸지 않는 예외: 로딩은 I-10이, 실패 안내는 I-11 카드가 맡는다(로그인 만료만 공통 처리).
    // 섬 씬(Island01)은 앱 위에 겹쳐 불러오고(additive: 앱 화면·로그인은 그대로), 이 화면을 떠나면 내린다.
    // 고른 코스·입장 위치는 MapLaunch로 넘기고 섬 쪽 MapLaunchApplier가 적용한다. 앱 배경은 투명(ShowsWorld).
    // 섬이 열리면 흐린 맵 위에 러닝 설정 → 거치 안내 → 달리는 매뉴얼(RunSetupView, 계획 0026)을 띄우고, 끝나면 맵을 보여 준다.
    // S3에 센서 준비·카운트다운·HUD·멈춤이 이 화면 위에 들어간다(고른 목표는 Setup).
    public sealed class MapScreen : AppScreen
    {
        const float EntryShare = 0.15f; // 진행 막대에서 입장 요청 몫(나머지는 씬 불러오기)
        const int AppliedTimeoutMs = 2000; // 섬 쪽 입장 위치 적용(MapLaunch.Applied)을 기다리는 최대 시간
        const int AfterAppliedFrames = 2;  // 적용 뒤 식생·LOD(카메라 위치로 매 프레임 갱신)가 새 자리로 한 번 바뀌도록
        internal static readonly Color Night = new Color32(0x0F, 0x1B, 0x3D, 0xFF); // App.uss --color-night(USS 변수는 C#에서 못 읽음)

        // 섬 씬은 한 번에 한 화면만 잡는다. 앞 맵 화면이 섬 씬을 불러오기 시작해서 다 내릴 때까지 끝나지 않음 →
        // 불러오는 도중에 나갔다가 바로 다시 들어와도 섬 씬이 둘 겹치거나 입장 정보(MapLaunch)를 앞 씬이 가져가지 않는다
        static Task islandFree = Task.CompletedTask;

        // 이 프로젝트는 Play 때 도메인 리로드를 꺼 둠 → Play를 멈춘 사이 끝나지 않은 것이 다음 Play에 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => islandFree = Task.CompletedTask;

        readonly CourseDetail course;
        readonly Action<MapExit, ApiException> onExit;
        VisualElement loadingView, progress, unavailable;
        Label unavailableTitle, unavailableBody;
        Button retry;
        TaskCompletionSource<bool> islandHeld; // 이 화면이 섬 씬을 잡고 있는 동안(다 내리면 끝냄)
        Scene island;
        bool sceneLoading;
        Camera appCamera;
        Color appCameraColor;
        string appCameraTag;
        bool busy, leaving, started;
        VisualElement mapTop;
        RunSetupView setupView;
        MapBackdrop backdrop;

        // 러닝 설정에서 고른 것(안내를 끝내기 전에는 null). S3 센서 준비·카운트다운이 쓴다
        public RunSetup Setup { get; private set; }

        public MapScreen(CourseDetail course, Action<MapExit, ApiException> onExit)
        {
            this.course = course ?? throw new ArgumentNullException(nameof(course));
            this.onExit = onExit;
        }

        protected override string Uxml => "UI/Map";
        public override bool HidesTabBar => true;
        public override bool Landscape => true;
        public override bool ShowsWorld => true;

        protected override void OnCreate()
        {
            Q<Button>("leave").clicked += () => Nav.Pop();
            mapTop = Q<VisualElement>("map-top");
            Q<Label>("course-label").text = CourseText.MapLabel(course.LengthM, course.Name);
            loadingView = Q<VisualElement>("map-loading");
            progress = Q<VisualElement>("progress");
            unavailable = Q<VisualElement>("unavailable");
            unavailableTitle = Q<Label>("unavailable-title");
            unavailableBody = Q<Label>("unavailable-body");
            Q<Button>("other-course").clicked += () => onExit?.Invoke(MapExit.OtherCourse, null);
            retry = Q<Button>("retry");
            retry.clicked += Begin;
            unavailable.SetVisible(false);
        }

        // 처음 보일 때만 입장한다. 위에 다른 화면이 올라갔다 내려와도 저절로 다시 요청하지 않음(다시 하려면 I-11 "다시 입장하기")
        protected internal override void OnShow()
        {
            if (started) return;
            started = true;
            Begin();
        }

        // 안내가 떠 있으면 한 단계 앞으로(D-01에서는 맵 나가기). 맵만 보이면 그냥 닫힘
        protected internal override bool OnBack()
        {
            if (setupView == null || !setupView.Visible) return false;
            setupView.Back();
            return true;
        }

        protected internal override void OnDestroy()
        {
            leaving = true;
            MapLaunch.Clear();
            CloseSetup();
            ReleaseIsland();
            if (appCamera != null) appCamera.backgroundColor = appCameraColor;
        }

        async void Begin()
        {
            if (busy || leaving) return;
            busy = true;
            unavailable.SetVisible(false);
            loadingView.SetVisible(true);
            SetProgress(0f);
            PaintBehind();
            try
            {
                await EnterAndLoad();
            }
            catch (Exception e)
            {
                // 씬을 못 불렀거나(빌드 씬 목록) 앱 쪽 버그. 불러온 씬은 내리고, 멈춘 것처럼 보이지 않게 카드로 알리고 기록은 남긴다
                Debug.LogException(e);
                MapLaunch.Clear();
                CloseSetup();
                ReleaseIsland();
                if (!leaving) ShowUnavailable("맵을 열지 못했어요", "잠시 후 다시 시도해 주세요.", canRetry: true);
            }
            finally
            {
                busy = false;
            }
        }

        // 입장이 됐다 = 서버에 보정이 있음. 센서에 아직 없으면(앱을 다시 켬) 불러온다(계획 0023 4번).
        // 실패해도 입장은 계속(속도가 안 나오면 다음 입장 때 다시 불러옴), 기록만 남김
        static async Task LoadCalibrationQuietly()
        {
            try
            {
                await CalibrationStore.EnsureLoadedAsync(Native.Speed);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[보정] 서버 보정을 못 불러옴(다음 입장 때 다시): " + e.Message);
            }
        }

        async Task EnterAndLoad()
        {
            MapEntry entry;
            try
            {
                entry = await Api.Courses.EnterAsync(course.CourseId);
            }
            catch (ApiException e)
            {
                if (!leaving) OnEnterFailed(e);
                return;
            }
            if (leaving) return;
            _ = LoadCalibrationQuietly(); // 섬 불러오기와 같이(달리기 시작은 카운트다운 뒤라 그 전에 끝남)
            SetProgress(EntryShare);

            await islandFree;
            if (leaving) return;
            islandHeld = new TaskCompletionSource<bool>();
            islandFree = islandHeld.Task;
            // 섬 씬이 깨어나는 동안 Camera.main이 섬 카메라를 가리키게(ScrunQuality·이름표가 앱 카메라를 잡지 않게) 미리 비켜 둔다
            YieldAppCamera();
            MapLaunch.Store(entry);

            sceneLoading = true;
            try
            {
                var op = SceneManager.LoadSceneAsync(MapLaunch.SceneName, LoadSceneMode.Additive)
                         ?? throw new InvalidOperationException($"섬 씬을 못 불러옴: {MapLaunch.SceneName}(빌드 씬 목록 확인)");
                while (!op.isDone)
                {
                    SetProgress(EntryShare + (1f - EntryShare) * Mathf.Clamp01(op.progress / 0.9f));
                    await Task.Yield();
                }
            }
            finally
            {
                sceneLoading = false;
            }

            island = SceneManager.GetSceneByName(MapLaunch.SceneName); // 한 번에 하나뿐(islandFree)
            if (!island.IsValid() || !island.isLoaded)
                throw new InvalidOperationException($"섬 씬이 열리지 않음: {MapLaunch.SceneName}");
            if (leaving) { ReleaseIsland(); return; } // 불러오는 사이 나가기를 누름

            // 섬 씬의 하늘·안개·조명이 적용되게 활성 씬으로, 앱 카메라(단색 배경)와 그 소리 듣는 곳은 끔(섬 카메라 것만 남게)
            SceneManager.SetActiveScene(island);
            SetAppCamera(false);
            SetProgress(1f);

            // 섬 쪽이 입장 위치로 옮기고 카메라를 맞춘 뒤에 흐린 배경을 찍는다(그동안 I-10이 덮고 있음)
            var applied = MapLaunch.Applied;
            if (await Task.WhenAny(applied, Task.Delay(AppliedTimeoutMs)) != applied)
                Debug.LogWarning("[MapLaunch] 섬 쪽 입장 위치 적용 알림이 없음 → 지금 자리로 흐린 배경을 찍음");
            if (leaving) return;
            int settled = Time.frameCount + AfterAppliedFrames;
            while (Time.frameCount < settled)
            {
                await Task.Yield();
                if (leaving) return;
            }
            StartSetup();
            loadingView.SetVisible(false);
        }

        // 중간에 예외가 나면(UXML 이름이 바뀌는 등) 흐린 배경·숨긴 윗줄을 되돌리고 던진다 → Begin의 catch가 I-11 카드로
        void StartSetup()
        {
            try
            {
                if (setupView == null)
                {
                    var view = new RunSetupView(Root, course, RunSetupPrefs.Device()); // 다 만든 뒤에야 Root에 붙음
                    view.Done += OnSetupDone;
                    view.Exit += () => Nav.Pop();
                    setupView = view;
                }
                backdrop?.Dispose();
                backdrop = null;
                // 앱 카메라는 태그를 비켜 둬서 Camera.main = 섬 카메라. 화면이 패널에서 떨어졌으면(await 사이) 흐림 없이 안내만
                var panel = Root.panel;
                if (panel != null) backdrop = MapBackdrop.Show(panel.visualTree, Camera.main);
                mapTop.SetVisible(false);
                Setup = null;
                setupView.Start();
            }
            catch
            {
                CloseSetup();
                mapTop.SetVisible(true);
                throw;
            }
        }

        void OnSetupDone(RunSetup setup)
        {
            Setup = setup;
            backdrop?.Dispose(); // 섬 카메라 다시 켬
            backdrop = null;
            mapTop.SetVisible(true);
        }

        // 섬 씬을 내리기 전에: 흐린 배경이 끈 섬 카메라·바꾼 그리기 간격을 먼저 되돌린다
        void CloseSetup()
        {
            setupView?.Hide();
            backdrop?.Dispose();
            backdrop = null;
        }

        void OnEnterFailed(ApiException e)
        {
            if (e.Failure == ApiFailure.NotLoggedIn)
            {
                Feedback.Handle(e); // 로그인 화면으로
                return;
            }
            Debug.LogWarning("[Api] " + e);
            var code = e.Failure == ApiFailure.Server ? e.Code : null;
            if (code == CourseText.CalibrationRequired)
            {
                onExit?.Invoke(MapExit.NeedsCalibration, e);
                return;
            }
            if (code == CourseText.CourseFull || code == CourseText.CourseNotFound)
            {
                var (t, b) = CourseText.Failure(code).Value;
                ShowUnavailable(t, b, canRetry: code == CourseText.CourseFull); // 없는 코스는 다시 들어가도 같음
                return;
            }
            var (title, body, canRetry) = Feedback.Describe(e); // 연결 안 됨 등
            ShowUnavailable(title, body, canRetry);
        }

        void ShowUnavailable(string title, string body, bool canRetry)
        {
            unavailableTitle.text = title;
            unavailableBody.text = body;
            retry.SetVisible(canRetry);
            loadingView.SetVisible(false);
            unavailable.SetVisible(true);
        }

        // 노치·홈 바 여백까지 남색으로(앱 배경이 투명한 동안 그 뒤의 앱 카메라 색이 보임)
        void PaintBehind()
        {
            if (appCamera != null) return;
            appCamera = FindAppCamera();
            if (appCamera == null) return;
            appCameraColor = appCamera.backgroundColor;
            appCamera.backgroundColor = Night;
        }

        // 맵을 열기 전에는 앱 씬이 활성 씬이다(섬 씬은 내릴 때 활성에서 뺌)
        static Camera FindAppCamera()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var cam = go.GetComponentInChildren<Camera>(true);
                if (cam != null) return cam;
            }
            return null;
        }

        // 섬 씬을 내리고 앱 카메라를 되돌린다(섬이 바꾼 전역 설정은 섬의 ScrunQuality가 내려갈 때 되돌림).
        // 불러오는 중이면 불러오기가 끝난 뒤 EnterAndLoad가 다시 부른다
        void ReleaseIsland()
        {
            SetAppCamera(true);
            if (sceneLoading) return;
            var held = islandHeld;
            islandHeld = null;
            if (island.IsValid() && island.isLoaded)
            {
                var app = appCamera != null ? appCamera.gameObject.scene : default;
                if (app.IsValid()) SceneManager.SetActiveScene(app);
                var op = SceneManager.UnloadSceneAsync(island);
                island = default;
                if (op != null)
                {
                    op.completed += _ => held?.TrySetResult(true);
                    return;
                }
            }
            island = default;
            held?.TrySetResult(true);
        }

        // 섬 씬을 부르기 전: MainCamera 태그와 소리 듣는 곳을 섬 카메라에 넘긴다. 앱 카메라는 섬이 다 열릴 때까지 화면을 칠함(켜 둠)
        void YieldAppCamera()
        {
            if (appCamera == null) return;
            if (appCameraTag == null) appCameraTag = appCamera.tag;
            appCamera.tag = "Untagged";
            var listener = appCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }

        // 섬이 열리면 끄고(false), 섬을 내리면 태그·소리까지 되돌린다(true)
        void SetAppCamera(bool on)
        {
            if (appCamera == null) return;
            appCamera.enabled = on;
            if (!on) return;
            if (appCameraTag != null) appCamera.tag = appCameraTag;
            var listener = appCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = true;
        }

        void SetProgress(float p) => progress.style.width = Length.Percent(Mathf.Clamp01(p) * 100f);
    }
}
