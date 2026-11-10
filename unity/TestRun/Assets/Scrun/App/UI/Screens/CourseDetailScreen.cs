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

        public CourseDetailScreen(string courseId) => this.courseId = courseId;

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
            bool ok = await Feedback.Run(async () => got = await Api.Courses.GetAsync(courseId), Load, OnLoadFailure);
            loading = false;
            if (Root.panel == null) return;
            if (ok && got != null)
            {
                course = got;
                Render();
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
            if (Nav.Current == this) Nav.Push(new MapScreen(course, ReturnFromMap));
        }

        // 맵 화면이 입장하지 못하고 돌려보낼 때. 맵을 내리면 이 화면이 다시 보이지만 곧 안내·목록으로 가므로 다시 불러오지 않는다
        // (불러오기 로딩·오류창이 안내를 덮지 않게)
        void ReturnFromMap(MapExit exit, ApiException error)
        {
            skipReload = true;
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
            }
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
