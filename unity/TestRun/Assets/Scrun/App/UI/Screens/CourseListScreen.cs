using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // C-01 코스 선택(GET /v1/courses). 홈 탭에 쌓이는 화면이라 Figma대로 뒤로 가기 버튼이 없다(아래 탭 "홈"을 다시 누르면 홈).
    // 보일 때마다 다시 불러 "지금 달리는 사람 수"를 새로 보여 준다. 칩으로 지금 달리는 코스만 거르고, 카드를 누르면 상세(C-02).
    public sealed class CourseListScreen : AppScreen
    {
        VisualElement list, loadFailed;
        Label empty;
        Button filterAll, filterLive;
        List<Course> courses;
        bool liveOnly, loading;

        protected override string Uxml => "UI/CourseList";

        protected override void OnCreate()
        {
            list = Q<VisualElement>("list");
            empty = Q<Label>("list-empty");
            loadFailed = Q<VisualElement>("load-failed");
            filterAll = Q<Button>("filter-all");
            filterLive = Q<Button>("filter-live");
            filterAll.clicked += () => SetFilter(false);
            filterLive.clicked += () => SetFilter(true);
            Q<Button>("reload").clicked += Load;
            loadFailed.SetVisible(false);
            empty.SetVisible(false);
        }

        protected internal override void OnShow() => Load();

        // 보여 줄 코스: 실사 영상(VIDEO)은 아직 달릴 수 없어 빼고, liveOnly면 지금 달리는 사람이 있는 코스만
        public static List<Course> Filter(IEnumerable<Course> all, bool liveOnly) =>
            (all ?? Enumerable.Empty<Course>())
            .Where(c => c != null && c.Type != CourseText.VideoType && (!liveOnly || c.RunnersNow > 0))
            .ToList();

        async void Load()
        {
            if (loading) return;
            loading = true;
            List<Course> got = null;
            bool ok = await Feedback.Run(async () => got = await Api.Courses.ListAsync(), Load);
            loading = false;
            if (Root.panel == null) return; // 기다리는 사이 화면을 떠남
            loadFailed.SetVisible(!ok && courses == null);
            if (!ok) return;
            courses = got ?? new List<Course>();
            Build();
        }

        void SetFilter(bool live)
        {
            liveOnly = live;
            filterAll.EnableInClassList("chip--active", !live);
            filterLive.EnableInClassList("chip--active", live);
            Build();
        }

        void Build()
        {
            list.Clear();
            if (courses == null) return;
            var shown = Filter(courses, liveOnly);
            foreach (var c in shown) list.Add(Card(c));
            empty.text = CourseText.Empty(liveOnly);
            empty.SetVisible(shown.Count == 0);
        }

        // Course Card: 그림 150 + 이름 + "한 바퀴 6.7km" + 지금 달리는 사람 칩
        Button Card(Course c)
        {
            var id = c.CourseId;
            // 두 번 눌러 상세가 둘 쌓이지 않게(맨 위일 때만)
            var card = new Button(() => { if (Nav.Current == this) Nav.Push(new CourseDetailScreen(id)); }) { name = "course-" + id };
            card.AddToClassList("course-card");
            var thumb = CourseParts.Thumb(c);
            thumb.pickingMode = PickingMode.Ignore;
            card.Add(thumb);
            var info = new VisualElement { pickingMode = PickingMode.Ignore };
            info.AddToClassList("course-card-info");
            info.Add(CourseParts.Text(c.Name, "course-name"));
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("course-meta-row");
            row.Add(CourseParts.Text(CourseText.Lap(c.LengthM), "course-meta"));
            row.Add(CourseParts.LiveChip(c.RunnersNow));
            info.Add(row);
            card.Add(info);
            return card;
        }
    }
}
