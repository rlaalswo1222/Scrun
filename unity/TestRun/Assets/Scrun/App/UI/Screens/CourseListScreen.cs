using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // C-01 코스 선택(GET /v1/courses). 홈 탭에 쌓이는 화면이라 Figma대로 뒤로 가기 버튼이 없다(아래 탭 "홈"을 다시 누르면 홈).
    // 보일 때마다 다시 불러 "지금 달리는 사람 수"를 새로 보여 준다. 칩으로 지금 달리는 코스만 거르고, 카드를 누르면 상세(C-02).
    // 기록 상세 "이 기록으로 고스트 달리기"(F-03, 계획 0053)로 오면 기록 탭에 쌓이고, 고른 코스의 러닝 설정에 그 기록을 고스트로 켜 둠
    public sealed class CourseListScreen : AppScreen
    {
        readonly RunSummary ghost;

        public CourseListScreen(RunSummary ghost = null) => this.ghost = ghost;

        VisualElement list;
        StateView state;
        Button filterAll, filterLive;
        List<Course> courses;
        bool liveOnly, loading;

        protected override string Uxml => "UI/CourseList";
        public override string RootClass => SpaceArt.HomeRootClass; // 우주 시안 C-01(계획 0062)

        protected override void OnCreate()
        {
            list = Q<VisualElement>("list");
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.Cards, "코스를 불러오지 못했어요");
            filterAll = Q<Button>("filter-all");
            filterLive = Q<Button>("filter-live");
            filterAll.clicked += () => SetFilter(false);
            filterLive.clicked += () => SetFilter(true);
            if (ghost != null) Q<Label>("lead").text = CourseText.GhostLead;
            var back = Q<Button>("back");
            back.SetVisible(ghost != null); // 홈 탭에선 Figma대로 없음, 기록 탭에 쌓였을 땐 상세로 돌아갈 길
            back.clicked += () => { if (Nav.Current == this) Nav.Pop(); };
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
            // 처음만 스켈레톤(실패하면 그 자리에 실패 안내), 다시 보일 때(달리는 사람 수 새로)는 지금 목록을 둔 채 조용히
            bool ok = await Feedback.Load(state, courses != null, async () => got = await Api.Courses.ListAsync(), Load);
            loading = false;
            if (Root.panel == null || !ok) return; // 기다리는 사이 화면을 떠남
            courses = got ?? new List<Course>();
            Build();
        }

        void SetFilter(bool live)
        {
            liveOnly = live;
            filterAll.EnableInClassList("cl-chip--active", !live);
            filterLive.EnableInClassList("cl-chip--active", live);
            Build();
        }

        void Build()
        {
            list.Clear();
            if (courses == null) return;
            var shown = Filter(courses, liveOnly);
            foreach (var c in shown) list.Add(Card(c));
            if (shown.Count == 0) state.Empty(CourseText.Empty(liveOnly));
            else state.Clear();
        }

        // 우주 시안 Course Card(353×236): 그림 136(LIVE·지금 N명) + 이름·"한 바퀴 6.7km · 3D 루프 · 5구간"·달리는 사람 얼굴·구간 행성 5개
        Button Card(Course c)
        {
            var id = c.CourseId;
            // 두 번 눌러 상세가 둘 쌓이지 않게(맨 위일 때만)
            var card = new Button(() => { if (Nav.Current == this) Nav.Push(new CourseDetailScreen(id, ghost: ghost)); }) { name = "course-" + id };
            card.AddToClassList("bc-card");
            card.AddToClassList("cl-card");
            card.Add(CourseParts.SpaceThumb(c.RunnersNow));
            var info = CourseParts.Part("cl-info");
            var nameRow = CourseParts.Part("cl-name-row");
            var text = CourseParts.Part("cl-name-text");
            text.Add(UiExtensions.Text(c.Name, "cl-name"));
            text.Add(UiExtensions.Text(CourseText.CardMeta(c.LengthM), "cl-meta"));
            nameRow.Add(text);
            nameRow.Add(CourseParts.Runners(c.RunnersNow));
            info.Add(nameRow);
            info.Add(CourseParts.Zones("cl-zones", "cl-zone"));
            card.Add(info);
            return card;
        }
    }
}
