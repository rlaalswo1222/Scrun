using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 맵을 나가며 상세 화면에 알릴 때
    public enum MapExit
    {
        OtherCourse,      // I-11 "다른 코스 보기" → 목록
        NeedsCalibration, // 보정 먼저(상세에서 안내, Figma C-03)
        VerifyConsole,    // D-22 "계기판 촬영하기" → 인증(지환 E-02, MapScreen.FinishedRunId)
        Finished,         // D-22 "촬영 없이 마무리" 등 달리기를 끝내고 나감
    }

    // 맵(가로, D-10 자리). Figma 흐름대로 가로로 돌린 뒤 입장(POST /entries)과 섬 씬 불러오기를 I-10 로딩 화면 아래에서 하고,
    // 입장이 안 되면(가득 참·없는 코스·연결 문제) I-11 카드("다른 코스 보기"·"다시 입장하기")로 알린다.
    // 서버 호출을 Feedback.Run으로 감싸지 않는 예외: 로딩은 I-10이, 실패 안내는 I-11 카드가 맡는다(로그인 만료만 공통 처리).
    // 섬 씬(Island01)은 앱 위에 겹쳐 불러오고(additive: 앱 화면·로그인은 그대로), 이 화면을 떠나면 내린다.
    // 고른 코스·입장 위치는 MapLaunch로 넘기고 섬 쪽 MapLaunchApplier가 적용한다. 앱 배경은 투명(ShowsWorld).
    // 섬이 열리면 흐린 맵 위에 러닝 설정 → 거치 안내 → 달리는 매뉴얼(RunSetupView, 계획 0026)을 띄우고, 끝나면 맵을 보여 준다.
    // 입장 응답의 realtime으로 같은 맵 실시간 방에 붙고(계획 0029, 화면은 IRunTogether만 봄), 달리기를 시작하면 HUD(RunHudView)를 띄운다.
    // 섬과는 RunLink로 주고받는다(내 거리·속도, 달리는 중 스위치, 다른 러너).
    // 안내가 끝나면 센서 준비 → 달림 인식 → 3·2·1(RunReadyView, 계획 0035)을 띄우고, 끝나면 달리기를 시작하고 서버에 알린다(POST /runs).
    // 달리는 동안 오른쪽 위 버튼은 멈춤(D-10) → 일시정지·종료 확인·목표 달성·인증 안내(RunEndView, 계획 0037).
    // 속도 시계열은 1분마다, 끝나면 남은 것과 종료를 서버에 올린다(RunRecordUploader, 못 보내면 기기에 남김).
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
        RunReadyView readyView;
        MapBackdrop backdrop;
        Task<CalibrationState> calibrationLoad; // 입장 뒤 서버 보정 불러오기 결과(센서 준비 D-07의 보정값 줄)
        HeadphoneWatch headphones;   // 입장부터 나갈 때까지 에어팟 모션 연결을 지켜보게 함(D-07 이어폰 줄)
        string mapInstanceId;        // 입장 응답(POST /runs)
        CancellationTokenSource runStart; // 맵을 나가면 POST /runs 다시 보내기를 그만둠
        IRunTogether together;
        RunSession session;
        RunHudView hud;
        RunEndView endView;
        RunRecordUploader uploader;
        RunMeasureWatch measure;     // 측정 끊김·이상·발열(계획 0039)
        bool lastUnsteady;           // I-03 띠가 바뀐 순간만 기록
        double deviceReadAt = double.NegativeInfinity; // 발열·배터리는 자주 안 바뀌어 몇 초마다만(아이폰에선 네이티브 호출, 리뷰)
        ThermalLevel thermal = ThermalLevel.Nominal;
        float battery = -1f;
        bool charging, deviceWarned;
        const double DeviceReadSec = 5;
        bool goalPending;            // 다른 패널이 떠 있는 동안 목표에 닿음 → 닫히면 D-19
        IVisualElementScheduledItem runTick;
        Button topButton;
        CancellationTokenSource runUpload; // 맵을 나가면 달리는 동안의 묶음 올리기를 그만둠(끝낸 기록 보내기는 끝까지)

        const long RunTickMs = 200;

        // 이 맵에서 끝낸 달리기의 서버 기록 ID(D-22 "계기판 촬영하기" → 상세 화면이 인증 화면에 넘김, 지환 E-02). 서버에 못 보냈으면 null
        public string FinishedRunId { get; private set; }

        // 러닝 설정에서 고른 것(안내를 끝내기 전에는 null). S3 센서 준비·카운트다운이 쓴다
        public RunSetup Setup { get; private set; }

        // 이번 달리기(3·2·1이 끝나야 생김, 계획 0035)
        public RunSession Session => session;

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
            topButton = Q<Button>("leave");
            topButton.clicked += OnTopButton;
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

        // 안내가 떠 있으면 한 단계 앞으로(D-01에서는 맵 나가기), 센서 준비·대기·3·2·1이면 D-01로. 맵만 보이면 그냥 닫힘
        protected internal override bool OnBack()
        {
            if (RunInProgress)
            {
                BackWhileRunning();
                return true;
            }
            if (session != null && session.Finished) return true; // D-22는 버튼으로만(저장 중에 뒤로 가기로 맵이 닫히지 않게)
            if (readyView != null && readyView.Visible)
            {
                readyView.Back();
                return true;
            }
            if (setupView == null || !setupView.Visible) return false;
            setupView.Back();
            return true;
        }

        protected internal override void OnDestroy()
        {
            leaving = true;
            MapLaunch.Clear();
            CancelRunStart();
            runTick?.Pause();
            runUpload?.Cancel();
            runUpload?.Dispose();
            runUpload = null;
            endView?.Dispose();
            endView = null;
            headphones?.Dispose();
            headphones = null;
            CloseSetup(); // 센서 준비 층이 켠 센서도 끔
            EndRun();
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
                EndRun();
                ReleaseIsland();
                headphones?.Dispose(); // 실패 카드 동안 에어팟 모션을 지켜보지 않음(배터리). 다시 입장하면 새로
                headphones = null;
                if (!leaving) ShowUnavailable("맵을 열지 못했어요", "잠시 후 다시 시도해 주세요.", canRetry: true);
            }
            finally
            {
                busy = false;
            }
        }

        // 입장이 됐다 = 서버에 보정이 있음. 센서에 아직 없으면(앱을 다시 켬) 불러온다(계획 0023 4번).
        // 실패해도 입장은 계속하고, 이유를 센서 준비(D-07) 보정값 줄에 넘긴다(ReadyCalibration, 계획 0035 리뷰):
        // 일시 실패면 다시 시도, 서버에도 없거나 못 읽으면 보정 안내, 4xx면 넘어갈 수 없음, 로그인 만료면 로그인 화면(맵 입장과 같음)
        async Task<CalibrationState> LoadCalibrationAsync()
        {
            try
            {
                return ReadyCalibration.From(await CalibrationStore.EnsureLoadedAsync(Native.Speed));
            }
            catch (ApiException e)
            {
                Debug.LogWarning("[보정] 서버 보정을 못 불러옴: " + e);
                // 로그인 화면으로는 이 맵이 아직 맨 위일 때만: 로그아웃·다른 로그인으로 이미 화면이 옮겨졌으면(로그인 바뀜) 두 번 옮기지 않음(#49 2차 리뷰)
                if (e.Failure == ApiFailure.NotLoggedIn && !leaving && Nav.Current == this) Feedback.Handle(e);
                return ReadyCalibration.From(e);
            }
            catch (Exception e)
            {
                Debug.LogException(e); // 센서가 없는 빌드 등(폰 센서 줄이 I-01로 감)
                return CalibrationState.Failed;
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
            mapInstanceId = entry.MapInstanceId;
            calibrationLoad = LoadCalibrationAsync(); // 섬 불러오기와 같이. 센서 준비(D-07)가 끝나길 기다림
            headphones ??= new HeadphoneWatch();
            SetProgress(EntryShare);

            await islandFree;
            if (leaving) return;
            islandHeld = new TaskCompletionSource<bool>();
            islandFree = islandHeld.Task;
            // 섬 씬이 깨어나는 동안 Camera.main이 섬 카메라를 가리키게(ScrunQuality·이름표가 앱 카메라를 잡지 않게) 미리 비켜 둔다
            YieldAppCamera();
            MapLaunch.Store(entry);
            JoinRoom(entry); // 섬을 불러오는 동안 붙어서, 맵이 열릴 때 다른 러너가 이미 보이게

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
                // 센서 준비에서 X로 돌아오면 흐린 배경이 이미 있음 → 그대로 씀(아바타가 서 있어 같은 그림. 지우고 같은 프레임에 다시 찍으면
                // Camera.main이 아직 섬 카메라를 못 잡아 남색만 남음, 11/16 에디터 확인)
                SetBackdrop(true);
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

        // D-06 "시작할게요" → 흐린 맵 그대로 센서 준비(D-07)
        void OnSetupDone(RunSetup setup)
        {
            Setup = setup;
            if (readyView == null)
            {
                var view = new RunReadyView(Root, course.Name, () => together, CalibrationNow, RetryCalibration);
                view.Blur += SetBackdrop;
                view.Started += OnReadyStarted;
                view.Cancelled += OnReadyCancelled;
                view.NeedsCalibration += () => onExit?.Invoke(MapExit.NeedsCalibration, null); // 코스 상세가 보정 안내(A-11)
                readyView = view;
            }
            ReloadCalibrationIfFailed();
            readyView.Start(setup.Goal);
        }

        // 3·2·1이 끝남
        void OnReadyStarted()
        {
            backdrop?.Dispose(); // 달리는 동안엔 흐린 그림이 필요 없음(D-01로 돌아올 일 없음) → 텍스처를 놓음
            backdrop = null;
            mapTop.SetVisible(true);
            hud?.Dispose();
            session = new RunSession(Setup.Goal);
            hud = new RunHudView(Root, session, together);
            StartRunning();
        }

        // ---- 달리는 동안: 멈춤·끝(계획 0037) ----

        bool RunInProgress => session != null && session.Started && !session.Finished;

        // 오른쪽 위 버튼: 달리기 전엔 나가기(X), 달리는 동안엔 멈춤(D-10)
        void OnTopButton()
        {
            if (RunInProgress) OpenPause();
            else Nav.Pop();
        }

        void SetTopButtonPause(bool pause) => topButton.EnableInClassList("map-round-btn--pause", pause);

        RunEndView EndView()
        {
            if (endView != null) return endView;
            var v = new RunEndView(Root);
            v.Resume += ResumeRun;
            v.AskFinish += () => { if (session != null) v.ShowConfirm(session.DistanceM(RunLink.PlayerDistanceM), session.ElapsedSec); };
            v.Finish += FinishRun;
            v.CloseGoal += HideEnd;
            v.Verify += () => onExit?.Invoke(MapExit.VerifyConsole, null);
            v.SkipVerify += SkipVerify;
            v.LostPause += () => { measure?.DismissLost(); OpenPause(); };
            v.LostRetry += RetryMeasure;
            v.HeatContinue += HideEnd;
            v.HeatLower += () => { if (!(RunLink.LowerQuality?.Invoke() ?? false)) Debug.Log("[품질] 더 낮출 수 없음"); HideEnd(); };
            endView = v;
            return v;
        }

        // 패널을 띄우면 윗줄(코스 이름·멈춤 버튼)을 숨김(Figma D-17–D-22에 없음). 패널은 이 둘로만 띄우고 닫는다
        void ShowEnd(Action<RunEndView> show)
        {
            show(EndView());
            mapTop.SetVisible(false);
        }

        void HideEnd()
        {
            endView?.Hide();
            mapTop.SetVisible(true);
        }

        // D-10 멈춤 → D-17: 시간 멈춤, 내 아바타 서 있음(남의 화면에도 running=false로 대기, Figma D-17 노트)
        void OpenPause()
        {
            if (!RunInProgress) return;
            session.Pause();
            RunLink.Moving = false;
            ShowEnd(v => v.ShowPause(session.DistanceM(RunLink.PlayerDistanceM), session.ElapsedSec));
        }

        // "계속 달리기": 아바타는 걸음을 따라 바로 움직이고(AvatarSpeed), 시간은 걸음이 다시 잡히면(아바타가 움직이면) 흐름
        void ResumeRun()
        {
            if (!RunInProgress) return;
            session.Resume();
            RunLink.Moving = true;
            HideEnd();
        }

        // Esc·안드로이드 뒤로: 달리는 중이면 멈춤, D-18이면 D-17로, D-17·D-19면 닫기. D-22는 버튼으로만
        void BackWhileRunning()
        {
            switch (endView?.Shown ?? RunEndPanel.None)
            {
                case RunEndPanel.None: OpenPause(); break;
                case RunEndPanel.Confirm: endView.ShowPause(session.DistanceM(RunLink.PlayerDistanceM), session.ElapsedSec); break;
                case RunEndPanel.Pause: ResumeRun(); break;
                case RunEndPanel.Goal: HideEnd(); break;
                case RunEndPanel.MeasureLost: measure?.DismissLost(); OpenPause(); break; // "일시정지"와 같게
                case RunEndPanel.Heat: HideEnd(); break;                                  // "계속 달리기"와 같게
            }
        }

        // 0.2초마다: 시계열 칸, 다시 걷기, 목표 달성(D-19), 1분 묶음 올리기
        void RunTick()
        {
            if (leaving || session == null || !session.Started || session.Finished) return;
            session.Update(RunLink.PlayerSpeedKmh);
            var shown = endView?.Shown ?? RunEndPanel.None;
            if (session.CheckGoalReached(RunLink.PlayerDistanceM)) goalPending = true;
            if (goalPending && shown == RunEndPanel.None)
            {
                goalPending = false;
                ShowEnd(v => v.ShowGoal(session.Goal, session.ElapsedSec)); // 달리기는 그대로(아바타·시간 계속)
                shown = RunEndPanel.Goal;
            }
            WatchMeasure(shown);
            if (uploader != null && runUpload != null) uploader.StartUploadDue(runUpload.Token);
        }

        // 측정 끊김(D-15)·에어팟만 끊김(알림)·측정값 이상(I-03 띠)·배터리·발열(I-12). 패널이 떠 있으면 새로 띄우지 않음
        void WatchMeasure(RunEndPanel shown)
        {
            if (measure == null) return;
            // "계속 달리기" 뒤 걸음을 기다리는 동안(Resuming)도 달리는 중으로: 폰이 끊긴 채 계속을 누르면 아바타가 못 움직여
            // Resuming에 머무는데, 그때도 D-15가 떠야 함(리뷰)
            bool running = (session.Phase == RunPhase.Running || session.Phase == RunPhase.Resuming) && shown == RunEndPanel.None;
            var i = new MeasureInputs { Running = running, Unsteady = RunLink.SpeedUnsteady };
            try
            {
                var sensor = Native.Speed;
                i.Phone = sensor.Phone;
                i.AirPods = sensor.AirPods;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[측정] 센서 상태를 못 읽음: " + e.Message);
                return;
            }
            ReadDevice(); // 따로: 발열을 못 읽어도 D-15 감지는 계속(리뷰)
            i.Thermal = thermal;
            i.Battery = battery;
            i.Charging = charging;

            var c = measure.Update(i);
            if (c.HideLost && shown == RunEndPanel.MeasureLost) HideEnd();
            if (c.ShowLost) ShowEnd(v => v.ShowMeasureLost());
            if (c.AirPodsDropped) hud?.Notice(RunEndText.AirPodsDropped);
            bool canLower = RunLink.CanLowerQuality?.Invoke() ?? false;
            if (c.AskHeat) ShowEnd(v => v.ShowHeat(canLower));
            hud?.SetUnsteady(measure.Unsteady);

            // 헬스장 뒤 문턱 조정 근거(계획 0039 4번): 개발 빌드 Documents/run-log/measure-events.csv
            double t = session.ElapsedSec;
            if (c.ShowLost) RunEventLog.Write("측정끊김", t, "");
            if (c.HideLost) RunEventLog.Write("측정복구", t, "");
            if (c.AirPodsDropped) RunEventLog.Write("에어팟끊김", t, "");
            if (c.AskHeat) RunEventLog.Write("발열배터리", t, $"{thermal} {battery:0.00} {(charging ? "충전" : "")}");
            if (measure.Unsteady != lastUnsteady)
            {
                lastUnsteady = measure.Unsteady;
                RunEventLog.Write(lastUnsteady ? "측정이상시작" : "측정이상끝", t, $"{RunLink.PlayerSpeedKmh:0.0}");
            }
        }

        void ReadDevice()
        {
            double now = RealtimeClock.Now();
            if (now - deviceReadAt < DeviceReadSec) return;
            deviceReadAt = now;
            try
            {
                var dev = Native.Device;
                thermal = dev.Thermal;
                battery = dev.BatteryLevel;
                charging = dev.Charging;
            }
            catch (Exception e)
            {
                if (!deviceWarned) Debug.LogWarning("[측정] 발열·배터리를 못 읽음(이번 달리기엔 안 물음): " + e.Message);
                deviceWarned = true;
                thermal = ThermalLevel.Nominal;
                battery = -1f;
            }
        }

        // D-15 "다시 측정": 센서를 껐다 켬(#49 I-01 "다시 시도"와 같은 방식). 섬 쪽이 켠 센서라도 켜 둔 채로 끝나 "켠 쪽이 끈다"를 안 깸.
        // 카드는 폰 데이터가 다시 들어오면 저절로 닫힘
        void RetryMeasure()
        {
            measure?.SensorRestarted();
            RunEventLog.Write("다시측정", session?.ElapsedSec ?? 0, "");
            try
            {
                var sensor = Native.Speed;
                sensor.Stop();
                sensor.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[측정] 센서를 다시 켜지 못함: " + e.Message);
            }
        }

        // D-18 "종료하고 인증" · D-19 "마무리하기" → D-22, 뒤에서 서버에 남은 묶음 + 종료
        async void FinishRun()
        {
            if (!RunInProgress) return;
            var body = session.Finish(RunLink.PlayerDistanceM, RunLink.PlayerSpeedKmh);
            if (body == null) return;
            RunLink.Moving = false;
            runTick?.Pause();
            CancelRunStart(); // 아직 runId가 없으면 올리는 쪽이 같은 clientRunId로 다시 시작함
            runUpload?.Cancel();
            ShowEnd(v => v.ShowDone(session.DistanceM(RunLink.PlayerDistanceM)));
            FinishedRunId = null;
            var s = session;
            RunSaveState state;
            try
            {
                var summary = await uploader.FinishAsync(CancellationToken.None); // 맵을 나가도 끝까지(못 보내면 기기에 남김)
                state = summary != null ? RunSaveState.Saved : PendingRunStore.Default.Has(s.ClientRunId) ? RunSaveState.KeptLocal : RunSaveState.Failed;
                if (summary != null) FinishedRunId = summary.RunId;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                state = RunSaveState.Failed;
            }
            if (!leaving && endView != null && session == s) endView.SetSaving(state);
        }

        // D-22 "촬영 없이 마무리 (미인증)": 미인증으로 저장, 나중에 인증 못 함(Figma D-22 노트, 11/06 확정). 인증 API는 지환 BL-18.
        // 기록을 폰에 남겼으면(오프라인) 그 기록에 "미인증으로 마무리"를 적어 두고 나중에 같이 보냄
        async void SkipVerify()
        {
            string runId = FinishedRunId;
            string clientRunId = session?.ClientRunId;
            onExit?.Invoke(MapExit.Finished, null);
            if (runId == null)
            {
                if (clientRunId != null) PendingRunStore.Default.MarkSkipVerification(clientRunId);
                return;
            }
            try { await Api.Runs.SkipVerificationAsync(runId); }
            catch (ApiException e) { Debug.LogWarning("[기록] 미인증 마무리를 서버에 못 알림(인증 대기로 남음): " + e); }
        }

        // D-08 X·뒤로 → D-01 러닝 설정부터 다시(맵·입장 자리 그대로, 정한 것 3)
        void OnReadyCancelled()
        {
            if (leaving) return;
            StartSetup();
        }

        // 흐린 배경(D-01–D-08·I-01)과 맵(D-09). 한 번 찍은 흐린 그림은 숨겼다 다시 보이기만 한다(D-08 ↔ D-09, X → D-01):
        // 다시 찍으면 캡처·흐림·CPU 읽기가 반복돼 아이폰 끊김·발열(리뷰), 그 사이 아바타는 서 있어 같은 그림.
        // 처음 찍을 땐 섬 카메라를 쓰므로 섬이 열려 있어야 함
        void SetBackdrop(bool on)
        {
            if (backdrop != null)
            {
                backdrop.SetShown(on); // 숨기면 섬 카메라 다시 켬
                return;
            }
            if (!on || leaving) return;
            // 앱 카메라는 태그를 비켜 둬서 Camera.main = 섬 카메라. 화면이 패널에서 떨어졌으면(await 사이) 흐림 없이 안내만
            var panel = Root.panel;
            if (panel != null) backdrop = MapBackdrop.Show(panel.visualTree, Camera.main);
        }

        CalibrationState CalibrationNow()
        {
            try
            {
                if (Native.Speed.HasCalibration) return CalibrationState.Ready;
            }
            catch (Exception)
            {
                return CalibrationState.Failed; // 센서가 없는 빌드(폰 센서 줄이 I-01로 감)
            }
            if (calibrationLoad == null || !calibrationLoad.IsCompleted) return CalibrationState.Loading;
            var r = calibrationLoad.Result;
            // 불러왔다(Ready)는데 센서에 없음 = 그 사이 로그아웃으로 지워짐 → 로그인 쪽이 화면을 옮기므로 아무것도 안 함.
            // 보정 안내(A-11)로 보내면 로그인 화면 이동과 겹치고, 로그아웃한 사람이 보정 화면에 남음(#49 2차 리뷰)
            return r == CalibrationState.Ready ? CalibrationState.Cancelled : r;
        }

        // D-07을 다시 열 때(X → D-01 → … → D-06) 지난번 불러오기가 실패로 끝났으면 다시 불러온다.
        // 넘어갈 수 없는 실패(Failed, 429 등)도 맵을 나갔다 들어오지 않고 다시 해 볼 수 있게(#49 2차 리뷰)
        void ReloadCalibrationIfFailed()
        {
            if (calibrationLoad == null || !calibrationLoad.IsCompleted) return;
            var r = calibrationLoad.Result;
            if (r == CalibrationState.Failed || r == CalibrationState.RetryLater) calibrationLoad = LoadCalibrationAsync();
        }

        void RetryCalibration()
        {
            if (calibrationLoad != null && !calibrationLoad.IsCompleted) return;
            calibrationLoad = LoadCalibrationAsync();
        }

        // 3·2·1 끝 → 시간·거리 기록 시작, 내 아바타가 센서 속도로 움직임(BL-11 ③). 서버에는 뒤에서 알림(정한 것 2)
        public void StartRunning()
        {
            if (session == null || session.Started) return;
            session.Start(RunLink.PlayerDistanceM);
            RunLink.Moving = true;
            SendRunStart(session);
            runUpload?.Cancel();
            runUpload?.Dispose();
            runUpload = new CancellationTokenSource();
            uploader = new RunRecordUploader(session, mapInstanceId, userId: RunRecordUploader.TokenSubject(Api.Session.AccessToken));
            measure = new RunMeasureWatch();
            goalPending = false;
            lastUnsteady = false;
            deviceReadAt = double.NegativeInfinity;
            deviceWarned = false;
            RunEventLog.Begin(session.ClientRunId);
            SetTopButtonPause(true);
            runTick ??= Root.schedule.Execute(RunTick).Every(RunTickMs);
            runTick.Resume();
        }

        async void SendRunStart(RunSession s)
        {
            CancelRunStart();
            runStart = new CancellationTokenSource();
            var ct = runStart.Token;
            try
            {
                var r = await new RunStartSender().SendAsync(s.StartBody(mapInstanceId), ct);
                if (r != null && session == s) s.RunId = r.RunId;
            }
            catch (OperationCanceledException) { } // 맵을 나감
            catch (Exception e)
            {
                Debug.LogException(e); // async void라 여기서 놓치면 원인을 모름. 달리기는 계속
            }
        }

        void CancelRunStart()
        {
            runStart?.Cancel();
            runStart?.Dispose();
            runStart = null;
        }

        // 같은 맵 실시간 방(ADR-0010). 서버가 realtime을 안 주면(옛 스텁) 혼자 달림
        void JoinRoom(MapEntry entry)
        {
            EndRun();
            if (entry.Realtime != null && !string.IsNullOrEmpty(entry.Realtime.Url))
            {
                together = new WsRunTogether(entry.Realtime, RunLink.MyState);
                together.Start();
            }
            else Debug.LogWarning("[같이 뛰기] 입장 응답에 realtime이 없음 → 혼자 달림");
            RunLink.Begin(together);
        }

        void EndRun()
        {
            runTick?.Pause();
            endView?.Hide();
            if (topButton != null) SetTopButtonPause(false);
            hud?.Dispose();
            hud = null;
            together?.Dispose();
            together = null;
            session = null;
            RunLink.End();
        }

        // 섬 씬을 내리기 전에: 흐린 배경이 끈 섬 카메라·바꾼 그리기 간격을 먼저 되돌린다
        void CloseSetup()
        {
            setupView?.Hide();
            readyView?.Dispose();
            readyView = null;
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
            // I-07(계획 0041): 기기에 네트워크가 아예 없을 때만. 시간 초과(망은 있는데 서버가 느림)는 공통 안내
            if (e.Failure == ApiFailure.Network && Application.internetReachability == NetworkReachability.NotReachable)
            {
                ShowUnavailable(CourseText.NoNetworkTitle, CourseText.NoNetworkBody, canRetry: true);
                return;
            }
            var (title, body, canRetry) = Feedback.Describe(e); // 서버 오류 등
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
