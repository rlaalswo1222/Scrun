using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // C-02 코스 상세(GET /v1/courses/{id}). "맵 입장"을 누르면 가로 맵 화면으로 가고, 입장(POST /entries)·로딩(I-10)·가득 참(I-11)은 맵 화면이 맡는다.
    // 순서: "맵 입장" → 모션 권한 확인(계획 0025, 없으면 팝업·I-05) → 맵 → 보정 없음(409)이면 보정 안내(A-11, 계획 0023) → 다시 Enter(모션은 이미 허용이라 통과).
    // 맵 화면이 입장하지 못하고 돌려보내면(ReturnFromMap): 보정 없음 → 보정 안내를 거쳐 다시 입장, 다른 코스 보기 → 목록으로.
    // 없는 코스(404)는 안내창 대신 화면 안에 "다른 코스 보기"로 보여 준다(목록으로 바로 돌아가면 목록 불러오기 안내가 덮을 수 있음).
    public sealed class CourseDetailScreen : AppScreen
    {
        readonly string courseId;
        CourseDetail course;
        Label title, description, runners, length, loadFailedText;
        Button enter, reload;
        VisualElement hero, content, loadFailed;
        bool loading, skipReload, notFound, gating, fromCalibration;
        MapScreen map; // 띄운 맵(돌아올 때 끝낸 기록 ID를 받음)
        bool enterOnLoad; // 친구 탭 "같이 달리기"(G-03, 계획 0044): 처음 불러오면 바로 "맵 입장"
        RunningFriend together; // 같이 달리기로 왔으면 그 친구(계획 0047): 다음 맵 한 번만 친구 뒤로 입장(X로 나와도 비워짐)
        readonly RunSummary ghost; // 기록 상세 "이 기록으로 고스트 달리기"로 왔으면 그 기록(계획 0053): 러닝 설정에 고스트를 켜 둠(다시 입장해도)

        public CourseDetailScreen(string courseId, bool enterOnLoad = false, RunningFriend together = null, RunSummary ghost = null)
        {
            this.courseId = courseId;
            this.enterOnLoad = enterOnLoad;
            this.together = together;
            this.ghost = ghost;
        }

        protected override string Uxml => "UI/CourseDetail";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => Nav.Pop();
            title = Q<Label>("title");
            description = Q<Label>("description");
            runners = Q<Label>("runners");
            length = Q<Label>("length");
            hero = Q<VisualElement>("hero");
            content = Q<VisualElement>("content");
            loadFailed = Q<VisualElement>("load-failed");
            loadFailedText = Q<Label>("load-failed-text");
            reload = Q<Button>("reload");
            reload.clicked += () => { if (notFound) Nav.Pop(); else Load(); };
            enter = Q<Button>("enter");
            enter.clicked += Enter;
            content.SetVisible(false);
            loadFailed.SetVisible(false);
            enter.SetEnabled(false);
        }

        // 맵에서 나왔을 때도 인원을 새로(곧 다른 곳으로 갈 때는 빼고).
        // 보정에서 돌아올 때도 다시 불러오지 않음: 끝나면 곧바로 맵으로 가서, 불러오기 오류창이 맵을 덮을 수 있음
        protected internal override void OnShow()
        {
            if (fromCalibration)
            {
                fromCalibration = false;
                return;
            }
            if (!skipReload && !notFound) Load();
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            CourseDetail got = null;
            // 처음만 덮개, 인증·맵에서 돌아오면 지금 상세를 둔 채 조용히(그새 없어진 코스면 그대로 알림)
            bool ok = await Feedback.Load(course != null, async () => got = await Api.Courses.GetAsync(courseId), Load, OnLoadFailure);
            loading = false;
            // 바로 입장은 첫 불러오기 한 번만: 실패했거나 그 사이 다른 화면이 덮였으면 그냥 상세(나중에 다시 불러올 때 몰래 맵으로 가지 않게)
            bool autoEnter = enterOnLoad && ok && got != null && Nav.Current == this;
            enterOnLoad = false;
            if (Root.panel == null) return;
            if (ok && got != null)
            {
                course = got;
                Render();
                if (autoEnter) Enter();
            }
            else if (notFound)
            {
                ShowNotFound();
            }
            else if (course == null)
            {
                loadFailed.SetVisible(true);
            }
        }

        void Render()
        {
            content.SetVisible(true);
            loadFailed.SetVisible(false);
            title.text = course.Name;
            description.text = course.Description ?? "";
            runners.text = CourseText.PeopleCount(course.RunnersNow);
            length.text = CourseText.Km(course.LengthM);
            hero.Clear();
            hero.Add(CourseParts.Thumb(course));
            hero.Add(CourseParts.LiveChip(course.RunnersNow));
            enter.SetEnabled(true);
        }

        void ShowNotFound()
        {
            var (t, b) = CourseText.Failure(CourseText.CourseNotFound).Value;
            content.SetVisible(false);
            enter.SetEnabled(false);
            loadFailedText.text = t + "\n" + b;
            reload.text = "다른 코스 보기";
            loadFailed.SetVisible(true);
        }

        // 맵 입장 전 모션 권한 확인(계획 0025): 아직 안 물어봤으면 팝업, 거부·제한이면 I-05
        async void Enter()
        {
            if (course == null || gating || Nav.Current != this) return; // 두 번 눌러 맵이 둘 쌓이지 않게
            var motion = Onboarding.MotionStatus();
            if (Onboarding.BeforeMap(motion) == MotionGate.Ask)
            {
                gating = true;
                try { motion = await Onboarding.Ask(PermissionKind.Motion); } // 답이 안 오면 시간 제한 뒤 지금 상태로(입장 버튼이 잠기지 않게)
                finally { gating = false; }
                if (Root.panel == null || Nav.Current != this) return;
            }
            switch (Onboarding.BeforeMap(motion))
            {
                case MotionGate.Go:
                    PushMap();
                    break;
                case MotionGate.Blocked:
                    Nav.Push(new MotionDeniedScreen(onGranted: AfterMotionGranted, onLater: CloseMotionDenied));
                    break;
                // Ask: 시간 제한 안에 답이 없었음(거부한 게 아님) → 아무것도 안 띄우고 다시 누를 수 있게만
            }
        }

        // I-05에서 설정을 켜고 돌아왔을 때: I-05를 내리고 바로 맵으로
        void AfterMotionGranted()
        {
            CloseMotionDenied();
            PushMap();
        }

        // I-05를 내린다. 이 화면은 다시 불러오지 않음(방금 본 값 그대로, 로딩 덮개가 맵 로딩에 겹치지 않게)
        void CloseMotionDenied()
        {
            skipReload = true;
            try { Nav.Pop(); }
            finally { skipReload = false; }
        }

        void PushMap()
        {
            if (Nav.Current != this) return;
            map = new MapScreen(course, ReturnFromMap, together, ghost);
            together = null; // 이번 맵이 가져감. 나중에 "맵 입장"을 직접 누르면 일반 입장
            Nav.Push(map);
        }

        // 맵 화면이 돌려보낼 때. 입장 실패면 맵을 내리고 곧 안내·목록으로 가므로 다시 불러오지 않는다
        // (불러오기 로딩·오류창이 안내를 덮지 않게). 달리기를 끝내고 나오면 다시 불러옴(인원이 바뀜)
        void ReturnFromMap(MapExit exit, ApiException error)
        {
            string finishedRunId = map?.FinishedRunId; // 인증 화면에 넘길 기록(지환 E-02)
            RunningFriend friendBack = map?.Friend; // 보정 안내를 거쳐 다시 들어가면 같이 달리기를 이어 감
            RunStatsBody sensor = map?.Session?.FinishBody?.Sensor; // 인증 결과 표의 센서 값(서버 답에도 있음, 에디터 가짜 OCR용)
            string clientRunId = map?.Session?.ClientRunId;
            RunSession finishedSession = map?.Session;
            Task skip = map?.SkipTask;
            map = null;
            if (exit == MapExit.NeedsCalibration) together = friendBack;
            // 끝낸 기록을 서버에 못 올렸지만 이 폰에 남겼으면 간이 결과(정할 것 1 가). 저장도 못 했으면(Failed) 결과 없이 D-22 안내로 끝
            bool keptLocal = finishedRunId == null && clientRunId != null && PendingRunStore.Default.Has(clientRunId);
            bool toResult = exit == MapExit.Finished && (finishedRunId != null || keptLocal);
            // 결과·인증 화면을 바로 올리면 다시 불러오지 않음(아무도 안 봄, 결과를 닫으면 홈으로). 결과 없이 끝내고 나오면 인원이 바뀌어 다시 불러옴.
            // 인증 안내 때는 안내창이 로딩에 덮이지 않게
            skipReload = exit != MapExit.Finished || toResult;
            try { Nav.Pop(); }
            finally { skipReload = false; }
            if (Nav.Current != this) return;
            switch (exit)
            {
                case MapExit.OtherCourse:
                    Nav.Pop(); // 목록은 보일 때 다시 불러와 인원이 새로 보임
                    break;
                case MapExit.NeedsCalibration:
                    // 서버에 못 올린 보정이 이 폰에 있으면 그것부터 올리고 다시 입장(보정을 또 시키지 않음)
                    if (CalibrationStore.Pending != null)
                    {
                        UploadPendingThenEnter();
                        break;
                    }
                    // 보정 없음(409) → A-11 보정 안내(계획 0023). 끝나면 바로 다시 입장, 그만두면 이 화면에 머묾
                    fromCalibration = true;
                    Nav.Push(new CalibrationIntroScreen(onDone: AfterCalibration));
                    break;
                case MapExit.Finished:
                    // D-22 "촬영 없이 마무리" → E-14 결과(계획 0051). 서버에 못 올렸으면 이 폰의 값으로(정할 것 1 가)
                    if (finishedRunId != null) Nav.Push(new RunResultScreen(finishedRunId, skip));
                    else if (keptLocal && finishedSession?.FinishBody != null) Nav.Push(RunResultScreen.Offline(finishedSession));
                    break;
                case MapExit.VerifyConsole:
                    // D-22 "계기판 촬영하기" → E-02(계획 0038). 끝나면 이 화면을 다시 불러옴(인원이 바뀜)
                    if (finishedRunId != null)
                    {
                        string id = finishedRunId;
                        Nav.Push(new VerifyScreen(id, sensor, verified => AfterVerify(id, verified)));
                        break;
                    }
                    // 기록을 서버에 아직 못 올림(이 폰에 남음, 나중에 올림) → 인증은 종료 직후에만이라 미인증으로 표시
                    if (clientRunId != null) PendingRunStore.Default.MarkSkipVerification(clientRunId);
                    Feedback.ShowError("지금은 인증할 수 없어요", "기록을 서버에 아직 올리지 못했어요. 연결되면 미인증 기록으로 올라가요.");
                    break;
            }
        }

        // 인증 화면이 끝남 → 인증 화면을 결과로 바꿈(계획 0051): 인증 완료면 E-11 반영 → E-12, 미인증 저장이면 E-14.
        // 이 화면(코스 상세)은 결과 아래에 남고, 결과를 닫으면 홈으로
        void AfterVerify(string runId, bool verified)
        {
            if (!(Nav.Current is VerifyScreen)) return;
            skipReload = true; // 이 화면은 결과 아래에 깔리기만 함
            try { Nav.Pop(); }
            finally { skipReload = false; }
            Nav.Push(verified ? (AppScreen)new RunRewardsScreen(runId) : new RunResultScreen(runId));
        }

        // 보정이 끝남. 서버 저장이 실패했으면(이 폰에 남음) 입장해도 또 409라, 한 번 더 올려 보고 들어간다
        void AfterCalibration()
        {
            if (CalibrationStore.Pending == null) Enter();
            else UploadPendingThenEnter();
        }

        // 실패하면 안내창의 "다시 시도"로 다시 올림. 보정 화면으로는 돌려보내지 않는다
        async void UploadPendingThenEnter()
        {
            bool ok = await Feedback.Run(CalibrationStore.UploadPendingAsync, UploadPendingThenEnter);
            if (ok && Root.panel != null) Enter();
        }

        // 없는 코스(404)는 화면 안에서 안내. true면 공통 안내창을 띄우지 않음
        bool OnLoadFailure(ApiException e)
        {
            if (e.Failure != ApiFailure.Server || e.Code != CourseText.CourseNotFound) return false;
            notFound = true;
            return true;
        }
    }
}
