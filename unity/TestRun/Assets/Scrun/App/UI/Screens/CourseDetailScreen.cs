using UnityEngine.UIElements;

namespace Scrun.App
{
    // C-02 코스 상세(GET /v1/courses/{id}). "맵 입장"을 누르면 가로 맵 화면으로 가고, 입장(POST /entries)·로딩(I-10)·가득 참(I-11)은 맵 화면이 맡는다.
    // 맵 화면이 입장하지 못하고 돌려보내면(ReturnFromMap): 보정 없음 → 여기서 안내(보정 화면은 민재 S2, Figma C-03 시트), 다른 코스 보기 → 목록으로.
    // 없는 코스(404)는 안내창 대신 화면 안에 "다른 코스 보기"로 보여 준다(목록으로 바로 돌아가면 목록 불러오기 안내가 덮을 수 있음).
    public sealed class CourseDetailScreen : AppScreen
    {
        readonly string courseId;
        CourseDetail course;
        Label title, description, runners, length, loadFailedText;
        Button enter, reload;
        VisualElement hero, content, loadFailed;
        bool loading, skipReload, notFound;

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

        // 맵에서 나왔을 때도 인원을 새로(곧 다른 곳으로 갈 때는 빼고)
        protected internal override void OnShow()
        {
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

        void Enter()
        {
            if (course == null || Nav.Current != this) return; // 두 번 눌러 맵이 둘 쌓이지 않게
            Nav.Push(new MapScreen(course, ReturnFromMap));
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
                    // TODO(민재 S2 보정 화면): Figma C-03 시트 "보정 시작하기"로 보정 흐름에 보내기
                    var (t, b) = CourseText.Failure(CourseText.CalibrationRequired).Value;
                    Feedback.ShowError(t, b, error?.RequestId);
                    break;
            }
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
