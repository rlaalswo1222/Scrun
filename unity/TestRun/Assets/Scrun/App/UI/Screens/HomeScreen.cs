using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 홈 탭 첫 화면 B-01–B-04(계획 0057, BL-31, 모양은 우주 시안 계획 0062). 보일 때마다 GET /v1/home 한 번(처음만 스켈레톤, 다시 보일 땐 지금 화면을 둔 채 조용히).
    // 상태(첫 이용·목표 없음·진행 중·달성)는 HomeText.Pick. 이동: 달리기 시작 → C-01, 목표 정하기 → H-08 시트,
    // 위 카드(목표 있음) → H-07, 프로필 카드 → H-04, 친구 줄 → 한 명이면 G-03 시트·여럿이면 친구 탭, 친구 없음 → G-05.
    // 홈을 못 받아도 큰 버튼은 동작(코스 고르기). 앱을 켠 뒤 처음이면 끝나지 않은 러닝을 묻는다(I-04, RecoveryPrompt)
    public sealed class HomeScreen : AppScreen
    {
        StateView state;
        VisualElement topPart, bottomPart, topCard, countRow, levelFill, levelTrack;
        Label name, title, sub, done, levelChip, levelLine, friendsTitle, friendsNames, friendsCount;
        Button setGoal, level, start, friends, friendsEmpty;
        MiniRing ring;
        HomeData home;
        List<RunningFriend> running = new List<RunningFriend>();
        bool loading, reloadAfter;
        WeeklyGoalSheet sheet;
        FriendParts.Sheet runTogether;

        protected override string Uxml => "UI/Home";
        public override string RootClass => SpaceArt.HomeRootClass;

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
            levelFill = Q<VisualElement>("level-fill");
            levelTrack = Q<VisualElement>("level-track");
            name = Q<Label>("name");
            title = Q<Label>("title");
            sub = Q<Label>("sub");
            done = Q<Label>("done");
            levelChip = Q<Label>("level-chip");
            levelLine = Q<Label>("level-line");
            friendsTitle = Q<Label>("friends-title");
            friendsNames = Q<Label>("friends-names");
            friendsCount = Q<Label>("friends-count");
            setGoal = Q<Button>("set-goal");
            level = Q<Button>("level");
            start = Q<Button>("start");
            friends = Q<Button>("friends");
            friendsEmpty = Q<Button>("friends-empty");
            ring = new MiniRing();
            Q<VisualElement>("ring-slot").Add(ring);

            setGoal.text = HomeText.SetGoal;
            start.text = HomeText.Start;
            Q<Label>("friends-empty-title").text = HomeText.NoFriendsTitle;
            Q<Label>("friends-empty-body").text = HomeText.NoFriendsBody;

            start.clicked += OpenCourses;
            setGoal.clicked += OpenGoalSheet;
            topCard.RegisterCallback<ClickEvent>(_ => OpenGoalStatus());
            level.clicked += () => { if (CanPush && home?.Progress != null) Nav.Push(new LevelScreen(home.Progress)); };
            friends.clicked += OpenRunningFriends;
            friendsEmpty.clicked += () => { if (CanPush) Nav.Push(new FriendAddScreen()); };

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
            // 앱을 다시 켰을 때: 지난 응답(캐시)으로 먼저 그리고 아래에서 조용히 새로 받음(스켈레톤 없음, 계획 0074 A)
            if (home == null && Api.Users.CachedHome() is HomeData cached) Show(cached);
            HomeData got = null;
            bool ok = await Feedback.Load(state, home != null, async () => got = await Api.Users.HomeAsync(), Load);
            loading = false;
            if (Root.panel == null) return; // 기다리는 사이 화면을 떠남(다시 보일 때 OnShow가 받음)
            if (ok && got != null)
            {
                Show(got);
                TabPrefetch.Run(); // 기록·친구 탭 첫 화면을 뒤에서 받아 둠(이번 실행에 한 번, 계획 0074 B)
            }
            if (reloadAfter) Load();
        }

        internal void Show(HomeData h)
        {
            home = h ?? new HomeData();
            state.Clear();
            topPart.SetVisible(true);
            bottomPart.SetVisible(true);

            var p = home.Progress;
            name.text = home.Nickname ?? "";
            levelChip.SetVisible(p != null);
            levelLine.SetVisible(p != null);
            levelTrack.SetVisible(p != null);
            if (p != null)
            {
                levelChip.text = MyText.Level(p.Level);
                levelLine.text = HomeText.LevelLine(p);
                levelFill.style.width = Length.Percent(HomeText.LevelFill(p) * 100f);
            }
            var portrait = Q<VisualElement>("portrait"); // 레벨 얼굴(Figma Set face Lv.N, HomeSpace.uss hs-face--N). 레벨을 모르면 기본 얼굴
            for (int lv = 1; lv <= 6; lv++) portrait.EnableInClassList("hs-face--" + lv, p != null && System.Math.Clamp(p.Level, 1, 6) == lv);

            var s = HomeText.Pick(home);
            Shown = s;
            var g = home.WeeklyGoal;
            title.text = HomeText.Title(s, g);
            sub.text = HomeText.Sub(s);
            sub.SetVisible(sub.text.Length > 0);
            // 목표가 있으면 왼쪽 위에 이번 주 진행(첫 이용도 목표가 있으면 0/3, 시안 B-04). 목표 없음은 짧은 카드 + 목표 정하기
            bool hasGoal = WeeklyGoalText.HasGoal(g);
            countRow.SetVisible(hasGoal);
            if (hasGoal)
            {
                done.text = HomeText.Count(g);
                ring.Progress = HomeText.Ring(g);
            }
            topCard.EnableInClassList("hs-hero--short", s == HomeText.State.NoGoal);
            topCard.EnableInClassList("hs-hero--done", s == HomeText.State.Done); // B-03: 캐릭터 없는 행성 + 기쁜 포즈(미리보기 hero_nochar·pose_joy)
            setGoal.SetVisible(s == HomeText.State.NoGoal);

            BuildFriends(home.RunningFriends ?? new List<RunningFriend>());
        }

        // 지금 달리는 친구를 한 줄로(시안 B-01): 얼굴 둘·"지민 · 하준"·"2명 달리는 중"·LIVE. 없으면 "친구" + 친구 추가(B-04)
        void BuildFriends(List<RunningFriend> list)
        {
            running = list;
            bool any = list.Count > 0;
            friendsTitle.text = any ? HomeText.FriendsTitle : HomeText.NoFriendsSection;
            friends.SetVisible(any);
            friendsEmpty.SetVisible(!any);
            if (!any) return;
            friendsNames.text = HomeText.FriendNames(list);
            friendsCount.text = HomeText.FriendCount(list.Count);
            Q<VisualElement>("avatar-2").SetVisible(list.Count > 1);
        }

        // 한 명이면 바로 같이 달리기(G-03), 여럿이면 친구 탭(달리는 친구마다 같이 달리기가 있음)
        void OpenRunningFriends()
        {
            if (!CanPush || running.Count == 0) return;
            if (running.Count == 1) runTogether = FriendParts.RunTogether(this, running[0]);
            else Nav.SelectTab(AppTab.Friends);
        }

        // B-02 "목표 정하기" → H-08 시트(저장하면 홈을 다시 받아 B-01로)
        void OpenGoalSheet()
        {
            if (!CanPush || sheet?.Saving == true || home == null) return;
            sheet = new WeeklyGoalSheet(Root, home.WeeklyGoal ?? new WeeklyGoal(), _ => { if (Root.panel != null) Load(); });
        }

        // 위 카드(목표가 있을 때, 첫 이용 0/3 포함) → H-07 현황
        void OpenGoalStatus()
        {
            if (WeeklyGoalText.HasGoal(home?.WeeklyGoal) && CanPush) Nav.Push(new WeeklyGoalStatusScreen());
        }
    }
}
