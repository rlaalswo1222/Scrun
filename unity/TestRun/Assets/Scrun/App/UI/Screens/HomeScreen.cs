using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 홈 탭 첫 화면 B-01–B-04(계획 0057, BL-31). 보일 때마다 GET /v1/home 한 번(처음만 스켈레톤, 다시 보일 땐 지금 화면을 둔 채 조용히).
    // 상태(첫 이용·목표 없음·진행 중·달성)는 HomeText.Pick. 이동: 큰 버튼·전체 보기 → C-01, 코스 카드 → C-02, 목표 정하기 → H-08 시트,
    // 위 카드(목표 있음) → H-07, 레벨 줄 → H-04, 친구 줄 → 친구 탭, 같이 달리기 → G-03 시트, 친구 없음 → G-05.
    // 홈을 못 받아도 큰 버튼은 동작(코스 고르기). 앱을 켠 뒤 처음이면 끝나지 않은 러닝을 묻는다(I-04, RecoveryPrompt)
    public sealed class HomeScreen : AppScreen
    {
        StateView state;
        VisualElement topPart, bottomPart, topCard, countRow, dots, character, levelFill, courses, friends, friendsEmpty;
        Label greeting, kicker, title, done, of, sub, levelChip, levelLine;
        Button setGoal, level, start;
        HomeData home;
        bool loading, reloadAfter;
        WeeklyGoalSheet sheet;
        FriendParts.Sheet runTogether;

        protected override string Uxml => "UI/Home";

        // 테스트
        internal HomeText.State? Shown { get; private set; }
        internal bool SheetOpen => sheet?.Open == true;

        protected override void OnCreate()
        {
            // 여기가 홈이라 I-09 "홈으로"는 뺌. 코스 고르기는 아래 큰 버튼이 늘 보여 그대로 됨(같은 버튼을 두 번 두지 않음)
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.Cards, HomeText.FailTitle).WithoutHomeButton();
            topPart = Q<VisualElement>("top-part");
            bottomPart = Q<VisualElement>("bottom-part");
            topCard = Q<VisualElement>("top-card");
            countRow = Q<VisualElement>("count-row");
            dots = Q<VisualElement>("dots");
            dots.AddToClassList("wg"); // 동그라미 모양은 H-07과 같음(WeeklyStatus.uss .wg …)
            dots.AddToClassList("wg-dots");
            character = Q<VisualElement>("character");
            levelFill = Q<VisualElement>("level-fill");
            courses = Q<VisualElement>("courses");
            friends = Q<VisualElement>("friends");
            friendsEmpty = Q<VisualElement>("friends-empty");
            greeting = Q<Label>("greeting");
            kicker = Q<Label>("kicker");
            title = Q<Label>("title");
            done = Q<Label>("done");
            of = Q<Label>("of");
            sub = Q<Label>("sub");
            levelChip = Q<Label>("level-chip");
            levelLine = Q<Label>("level-line");
            setGoal = Q<Button>("set-goal");
            level = Q<Button>("level");
            start = Q<Button>("start");

            setGoal.text = HomeText.SetGoal;
            Q<Label>("friends-empty-title").text = HomeText.NoFriendsTitle;
            Q<Label>("friends-empty-body").text = HomeText.NoFriendsBody;
            var addFriend = Q<Button>("add-friend");
            addFriend.text = HomeText.AddFriend;

            start.text = HomeText.StartPlain;
            start.clicked += OpenCourses;
            Q<Button>("see-all").clicked += OpenCourses;
            setGoal.clicked += OpenGoalSheet;
            topCard.RegisterCallback<ClickEvent>(_ => OpenGoalStatus());
            level.clicked += () => { if (CanPush && home?.Progress != null) Nav.Push(new LevelScreen(home.Progress)); };
            addFriend.clicked += () => { if (CanPush) Nav.Push(new FriendAddScreen()); };

            topPart.SetVisible(false);
            bottomPart.SetVisible(false);
        }

        // 두 번 눌러 둘 쌓이지 않게(맨 위일 때만), 시트가 떠 있으면 무시
        bool CanPush => Nav != null && Nav.Current == this && !SheetOpen && runTogether?.Open != true;

        void OpenCourses()
        {
            if (CanPush) Nav.Push(new CourseListScreen());
        }

        // 앱을 켠 뒤 처음이면 끝나지 않은 러닝을 묻고(I-04, 계획 0052), 지난번에 서버에 못 보낸 달리기 기록(기기에 남긴 것)을
        // 뒤에서 보낸다(계획 0037). 보내기 실패는 화면에 안 알림(다음에 다시)
        protected internal override void OnShow()
        {
            RecoveryPrompt.CheckOnShow(this, FlushPendingRuns);
            Load();
        }

        protected internal override bool OnBack()
        {
            if (RecoveryPrompt.OnBack()) return true; // I-04가 맨 위(목표 시트 위에 뜰 수 있음)
            if (SheetOpen)
            {
                sheet.Close();
                return true;
            }
            if (runTogether?.Open == true)
            {
                runTogether.Close();
                return true;
            }
            return false;
        }

        static async void FlushPendingRuns()
        {
            try { await PendingRunStore.Default.FlushAsync(RunRecordUploader.TokenSubject(Api.Session.AccessToken)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // 불러오는 중에 다시 부르면(목표를 저장한 직후 등) 지금 것이 끝난 뒤 한 번 더 받음(진행 중 응답은 저장 전 값일 수 있음, 리뷰)
        async void Load()
        {
            if (loading)
            {
                reloadAfter = true;
                return;
            }
            loading = true;
            reloadAfter = false;
            HomeData got = null;
            bool ok = await Feedback.Load(state, home != null, async () => got = await Api.Users.HomeAsync(), Load);
            loading = false;
            if (Root.panel == null) return; // 기다리는 사이 화면을 떠남(다시 보일 때 OnShow가 받음)
            if (ok && got != null) Show(got);
            if (reloadAfter) Load();
        }

        internal void Show(HomeData h)
        {
            home = h ?? new HomeData();
            state.Clear();
            topPart.SetVisible(true);
            bottomPart.SetVisible(true);
            greeting.text = HomeText.Greeting(home.Nickname);

            var s = HomeText.Pick(home);
            Shown = s;
            var g = home.WeeklyGoal;
            bool counting = s == HomeText.State.Progress || s == HomeText.State.Done;
            kicker.text = s == HomeText.State.First ? HomeText.FirstKicker : HomeText.WeeklyKicker;
            title.SetVisible(!counting);
            title.text = s == HomeText.State.First ? HomeText.FirstTitle : s == HomeText.State.NoGoal ? HomeText.NoGoalTitle : "";
            countRow.SetVisible(counting);
            dots.SetVisible(counting);
            setGoal.SetVisible(s == HomeText.State.NoGoal);
            sub.text = s switch
            {
                HomeText.State.First => HomeText.FirstSub,
                HomeText.State.NoGoal => HomeText.NoGoalSub,
                HomeText.State.Progress => HomeText.RemainingLine(g),
                _ => HomeText.DoneLine,
            };
            if (counting)
            {
                done.text = WeeklyGoalText.Done(g);
                of.text = WeeklyGoalText.Of(g);
                BuildDots(g);
            }
            character.EnableInClassList("hm-character--run", s == HomeText.State.First);
            character.EnableInClassList("hm-character--stretch", s == HomeText.State.Done);

            var p = home.Progress;
            level.SetVisible(p != null);
            if (p != null)
            {
                levelChip.text = MyText.Level(p.Level);
                levelLine.text = HomeText.LevelLine(p);
                levelFill.style.width = Length.Percent(HomeText.LevelFill(p) * 100f);
            }
            start.text = HomeText.Button(s);

            BuildCourses(HomeText.Courses(home));
            BuildFriends(home.RunningFriends ?? new List<RunningFriend>());
        }

        void BuildDots(WeeklyGoal g)
        {
            dots.Clear();
            int filled = WeeklyGoalText.Filled(g);
            for (int i = 0; i < WeeklyGoalText.Dots(g); i++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("wg-dot");
                bool on = i < filled;
                dot.EnableInClassList("wg-dot--done", on);
                if (on) dot.Add(UiExtensions.Text("✓", "wg-dot-mark"));
                dots.Add(dot);
            }
        }

        void BuildCourses(List<Course> list)
        {
            courses.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                string id = c.CourseId;
                var card = new Button(() => { if (CanPush) Nav.Push(new CourseDetailScreen(id)); }) { name = "home-course-" + id };
                card.AddToClassList("hm-course");
                if (i == HomeText.CourseCards - 1) card.AddToClassList("hm-course--last");
                var thumb = CourseParts.Thumb(c);
                thumb.pickingMode = PickingMode.Ignore;
                card.Add(thumb);
                var info = new VisualElement { pickingMode = PickingMode.Ignore };
                info.AddToClassList("hm-course-info");
                info.Add(UiExtensions.Text(c.Name, "hm-course-name"));
                info.Add(UiExtensions.Text(HomeText.CourseMeta(c), "hm-course-meta"));
                card.Add(info);
                courses.Add(card);
            }
            if (list.Count == 1) // 하나뿐이면 반 칸(Figma 두 칸 자리의 왼쪽)
            {
                var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
                spacer.AddToClassList("hm-course-spacer");
                courses.Add(spacer);
            }
            courses.SetVisible(list.Count > 0);
        }

        void BuildFriends(List<RunningFriend> list)
        {
            friends.Clear();
            foreach (var f in list)
            {
                var friend = f;
                friends.Add(FriendParts.Row(f.Nickname, FriendText.RunningLine(f), live: true,
                    onTap: () => { if (CanPush) Nav.SelectTab(AppTab.Friends); },
                    action: FriendParts.Action(HomeText.RunTogether, () =>
                    {
                        if (CanPush) runTogether = FriendParts.RunTogether(this, friend);
                    })));
            }
            friendsEmpty.SetVisible(list.Count == 0);
        }

        // B-02 "목표 정하기" → H-08 시트(저장하면 홈을 다시 받아 B-01로)
        void OpenGoalSheet()
        {
            if (!CanPush || sheet?.Saving == true || home == null) return;
            sheet = new WeeklyGoalSheet(Root, home.WeeklyGoal ?? new WeeklyGoal(), _ => { if (Root.panel != null) Load(); });
        }

        // 위 카드(목표가 있을 때) → H-07 현황
        void OpenGoalStatus()
        {
            if (Shown == HomeText.State.Progress || Shown == HomeText.State.Done)
                if (CanPush) Nav.Push(new WeeklyGoalStatusScreen());
        }
    }
}
