using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 기록 탭 첫 화면(F-01 기록 목록·F-02 빈 상태, 계획 0053, BL-20). GET /runs(끝난 기록 최신순 20개씩)를 그대로 그림.
    // 달 칩(이번 달·지난 달·전체)은 month로 서버가 자르고, 이번 주 요약 3칸은 전체 목록 첫 쪽에서 앱이 셈(정할 것 1 가, 한 주가 20개를 넘으면 다음 쪽까지).
    // 주간 목표가 있으면 "이번 주"는 그 주기(설정한 날부터 7일)이고 셋째 칸은 서버가 센 "N/목표"(11/19 민재 결정, 계획 0054), 없으면 월요일부터·인증 횟수.
    // 기록이 하나도 없으면 F-02. 카드 → 기록 상세(RunResultScreen 상세 모양, F-03·F-04). 보일 때마다(탭에 다시 들어옴·상세에서 돌아옴) 조용히 새로 받음
    public sealed class RecordsScreen : AppScreen
    {
        ScrollView scroll;
        VisualElement months, list, empty;
        StateView state;
        Label weekCount, weekKm, weekVerified, monthEmpty;
        Button more;
        PullToRefresh pull;
        List<(string label, string month)> chips;
        int selected; // 처음은 이번 달(Figma F-01)
        List<RunSummary> shown; // 지금 칩의 기록(받은 쪽까지)
        string cursor;
        bool? hasAny; // 전체 기록이 하나라도 있는지(모르면 null)
        bool loading, loadingMore;
        int loadSeq; // 칩을 빨리 바꾸면 늦게 온 옛 응답을 버림

        protected override string Uxml => "UI/Records";

        protected override void OnCreate()
        {
            scroll = Q<ScrollView>("scroll");
            months = Q<VisualElement>("months");
            list = Q<VisualElement>("list");
            empty = Q<VisualElement>("empty");
            // 처음·다른 달을 받는 동안·실패하면 목록과 빈 화면(F-02)을 숨기고 공통 칸만(옛 달 카드가 새 칩 아래 남지 않게, 리뷰)
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.List, "기록을 불러오지 못했어요", scroll, empty).Padded();
            weekCount = Q<Label>("week-count");
            weekKm = Q<Label>("week-km");
            weekVerified = Q<Label>("week-verified");
            monthEmpty = Q<Label>("month-empty");
            more = Q<Button>("more");
            more.clicked += LoadMore;
            Q<Label>("empty-title").text = RecordsText.EmptyTitle;
            Q<Label>("empty-body").text = RecordsText.EmptyBody;
            var start = Q<Button>("start");
            start.text = RecordsText.EmptyAction;
            start.clicked += StartRunning;
            pull = new PullToRefresh(scroll, Q<Label>("pull"), () => Load(fromPull: true));

            chips = RecordsText.MonthChips(DateTime.UtcNow);
            for (int i = 0; i < chips.Count; i++)
            {
                int index = i;
                var b = new Button(() => SelectMonth(index)) { text = chips[i].label, name = "month-" + i };
                b.AddToClassList("rc-month");
                months.Add(b);
            }
            MarkChip();
            ShowState(loaded: false);
        }

        protected internal override void OnShow()
        {
            RefreshMonths();
            Load();
        }

        // 앱을 켜 둔 채 달이 바뀌면 칩 이름·달을 새로(고른 자리는 그대로: "이번 달"이면 새 이번 달)
        void RefreshMonths()
        {
            var now = RecordsText.MonthChips(DateTime.UtcNow);
            if (now[0].month == chips[0].month) return;
            chips = now;
            for (int i = 0; i < chips.Count; i++) ((Button)months[i]).text = chips[i].label;
        }

        // F-02 "달리기 시작하기" → 홈 탭의 코스 목록(C-01은 홈 탭 화면, 뒤로 버튼 없음)
        void StartRunning()
        {
            if (Nav.Current != this) return;
            var nav = Nav;
            nav.ShowTabRoot(AppTab.Home);
            if (!(nav.Current is CourseListScreen)) nav.Push(new CourseListScreen());
        }

        void SelectMonth(int index)
        {
            if (index == selected) return;
            selected = index;
            MarkChip();
            shown = null; // 다른 달은 처음 받는 것처럼(덮개, 실패하면 다시 불러오기). 옛 달 카드가 새 칩 아래 남지 않게
            Load(chipChanged: true);
        }

        void MarkChip()
        {
            for (int i = 0; i < months.childCount; i++) months[i].EnableInClassList("rc-month--on", i == selected);
        }

        // 전체 첫 쪽(이번 주 요약·빈 상태 판단)과 고른 달 첫 쪽을 같이. 처음만 덮개, 다시 보이면 조용히(#89)
        async void Load(bool fromPull = false, bool chipChanged = false)
        {
            if (loading && !fromPull && !chipChanged) return;
            loading = true;
            int seq = ++loadSeq;
            if (fromPull) pull.Busy(true);
            string month = chips[selected].month;
            DateTime weekStart = default;
            RunListPage gotMonth = null;
            List<RunSummary> week = null;
            WeeklyGoal goal = null;
            Func<Task> work = async () =>
            {
                var all = Api.Runs.ListAsync();
                var byMonth = month == null ? all : Api.Runs.ListAsync(month: month);
                var goalTask = GoalOrNull();
                await Task.WhenAll(all, byMonth, goalTask);
                gotMonth = byMonth.Result;
                goal = goalTask.Result;
                weekStart = RecordsText.WeekStartUtc(DateTime.UtcNow, goal);
                week = new List<RunSummary>(all.Result.Items ?? new List<RunSummary>());
                string next = all.Result.NextCursor;
                while (RecordsText.WeekNeedsMore(week, next, weekStart)) // 한 주에 20번 넘게 달린 드문 경우
                {
                    var page = await Api.Runs.ListAsync(next);
                    week.AddRange(page.Items ?? new List<RunSummary>());
                    next = page.NextCursor;
                }
            };
            // 칩을 빨리 바꿔 버려진 옛 요청의 실패는 삼킴(더 새 요청이 안내함)
            bool ok = fromPull && shown != null
                ? await Feedback.Run(work, () => Load(fromPull: true),
                                     e => seq != loadSeq || (e.Failure != ApiFailure.NotLoggedIn && Nav.Current != this), overlay: false)
                : await Feedback.Load(state, shown != null, work, () => Load(), null, () => seq == loadSeq);
            if (seq != loadSeq) return; // 더 새로 받는 중
            loading = false;
            pull.Busy(false);
            if (Root.panel == null) return; // 기다리는 사이 화면을 떠남
            if (!ok) return; // 처음이면 공통 칸이 실패를 보임, 아니면 지난 목록 그대로
            Show(week, gotMonth, weekStart, keepLoaded: !fromPull && !chipChanged, goal: goal);
        }

        // 요약 카드의 주 기준(주간 목표 주기)과 셋째 칸. 곁들이는 값이라 못 받으면 월요일부터·인증 횟수로(목록을 막지 않음)
        static async Task<WeeklyGoal> GoalOrNull()
        {
            try { return await Api.Users.GetWeeklyGoalAsync(); }
            catch (ApiException e) when (e.Failure != ApiFailure.NotLoggedIn)
            {
                UnityEngine.Debug.LogWarning("[기록] 주간 목표를 못 받음: " + e);
                return null;
            }
        }

        // 받은 값으로 그림(테스트도 이걸로).
        // keepLoaded: 다시 보일 때(탭·상세에서 돌아옴) "더 보기"로 받아 둔 쪽을 버리지 않고 첫 쪽만 합침(#94 리뷰). 당겨 새로고침·칩 바꿈은 처음부터
        internal void Show(List<RunSummary> recentAll, RunListPage page, DateTime weekStartUtc, bool keepLoaded = false,
                           WeeklyGoal goal = null)
        {
            hasAny = recentAll != null && recentAll.Count > 0;
            var w = RecordsText.Summarize(recentAll, weekStartUtc);
            weekCount.text = RecordsText.Count(w.Count);
            weekKm.text = RecordsText.WeekKm(w.DistanceM);
            weekVerified.text = RecordsText.VerifiedCell(w.Verified, goal); // 목표가 있으면 "N/목표"(weekStartUtc도 그 주기)
            if (keepLoaded && shown != null && shown.Count > (page?.Items?.Count ?? 0))
            {
                Merge(page);
                ShowState(loaded: true);
                return;
            }
            shown = new List<RunSummary>();
            list.Clear();
            cursor = null;
            Add(page);
            ShowState(loaded: true);
        }

        async void LoadMore()
        {
            if (loadingMore || cursor == null) return;
            loadingMore = true;
            int seq = loadSeq;
            string from = cursor, month = chips[selected].month;
            RunListPage got = null;
            bool ok = await Feedback.Run(async () => got = await Api.Runs.ListAsync(from, month), LoadMore);
            loadingMore = false;
            if (ok && Root.panel != null && seq == loadSeq && from == cursor) Add(got);
        }

        void Add(RunListPage page)
        {
            foreach (var r in page?.Items ?? new List<RunSummary>())
            {
                shown.Add(r);
                list.Add(Card(r));
            }
            cursor = page?.NextCursor;
            more.SetVisible(cursor != null);
            monthEmpty.text = RecordsText.EmptyMonth(chips[selected].label, chips[selected].month);
            monthEmpty.SetVisible(shown.Count == 0);
        }

        // 펼쳐 둔 목록에 새로 받은 첫 쪽을 합침: 있던 기록은 그 자리 카드만 바꾸고(인증 대기 → 완료 등), 새 기록은 맨 위에(최신순).
        // 커서는 받아 둔 마지막 쪽 그대로라 "더 보기"가 이어짐
        void Merge(RunListPage page)
        {
            int top = 0;
            foreach (var r in page?.Items ?? new List<RunSummary>())
            {
                int at = shown.FindIndex(x => x.RunId == r.RunId);
                if (at >= 0)
                {
                    shown[at] = r;
                    list.RemoveAt(at);
                    list.Insert(at, Card(r));
                }
                else
                {
                    shown.Insert(top, r);
                    list.Insert(top, Card(r));
                    top++;
                }
            }
            monthEmpty.SetVisible(shown.Count == 0);
        }

        // F-01 카드(미리보기 records.js): 날짜 / 큰 거리 + 인증 칩(완료 초록·미인증 주황·대기 유리) / "시간 · 평균 페이스"
        Button Card(RunSummary r)
        {
            var run = r;
            var card = new Button(() => Open(run)) { name = "run-" + r.RunId };
            card.AddToClassList("rc-card");
            card.Add(UiExtensions.Text(FriendText.RunDate(r.StartedAt), "fr-record-date"));
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("rc-card-row");
            row.Add(UiExtensions.Text(FriendText.Km(r.DistanceM), "fr-record-km"));
            var (label, verified) = RunResultText.Chip(r.Verification);
            var chip = FriendParts.Chip(label, verified ? FriendParts.ChipKind.Success : FriendParts.ChipKind.Gray);
            chip.EnableInClassList("rr-chip--warn", RunResultText.ChipWarn(r.Verification));
            row.Add(chip);
            card.Add(row);
            card.Add(UiExtensions.Text(RecordsText.CardDetail(r), "rc-card-detail"));
            return card;
        }

        void Open(RunSummary r)
        {
            if (Nav.Current == this) Nav.Push(RunResultScreen.Detail(r.RunId));
        }

        // loaded: 받은 값이 있음(없으면 받는 중·실패 — 그때 보이는 건 공통 칸 state)
        void ShowState(bool loaded)
        {
            bool none = loaded && hasAny == false;
            scroll.SetVisible(loaded && !none);
            empty.SetVisible(none);
        }
    }
}
