using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0053: 기록 탭(F-01·F-02)·기록 상세(F-03·F-04, RunResultScreen 상세 모양)·고스트로 달리기 연결
    public class RecordsTests
    {
        static readonly TimeZoneInfo Seoul = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");

        static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);

        static RunSummary Run(string id, DateTime startedUtc, int m, string verification, int s = 1800, int? pace = 600) => new RunSummary
        {
            RunId = id, StartedAt = startedUtc, DistanceM = m, DurationS = s, AvgPaceSPerKm = pace, Verification = verification,
        };

        [Test]
        public void MonthChipsAreThisLastAndAllInMyTimeZone()
        {
            var chips = RecordsText.MonthChips(Utc(2026, 11, 18, 3), Seoul);
            Assert.AreEqual(("11월", "2026-11"), chips[0]);
            Assert.AreEqual(("10월", "2026-10"), chips[1]);
            Assert.AreEqual(("전체", (string)null), chips[2]);

            var jan = RecordsText.MonthChips(Utc(2027, 1, 2), Seoul);
            Assert.AreEqual(("1월", "2027-01"), jan[0]);
            Assert.AreEqual(("12월", "2026-12"), jan[1], "1월이면 지난해 12월");

            // UTC로는 10/31 16시지만 서울은 11/1 1시 → 이번 달은 11월
            Assert.AreEqual("2026-11", RecordsText.MonthChips(Utc(2026, 10, 31, 16), Seoul)[0].month);
        }

        [Test]
        public void WeekStartsOnMondayMidnight()
        {
            var monday = Utc(2026, 11, 15, 15); // 11/16(월) 0시 KST
            Assert.AreEqual(monday, RecordsText.WeekStartUtc(Utc(2026, 11, 18, 1), Seoul), "수요일");
            Assert.AreEqual(monday, RecordsText.WeekStartUtc(Utc(2026, 11, 22, 14), Seoul), "일요일 밤 11시도 같은 주");
            Assert.AreEqual(monday, RecordsText.WeekStartUtc(Utc(2026, 11, 15, 15), Seoul), "월요일 0시 정각");
            Assert.AreEqual(Utc(2026, 11, 22, 15), RecordsText.WeekStartUtc(Utc(2026, 11, 22, 15), Seoul), "다음 월요일 0시면 새 주");
        }

        // 11/19 민재 결정: 주간 목표가 있으면 요약 세 칸이 모두 그 주기(설정한 날부터 7일), 셋째 칸은 서버가 센 "N/목표"
        [Test]
        public void WithAGoalTheWeekIsTheGoalCycle()
        {
            var goal = new WeeklyGoal { TargetCount = 3, CycleStart = new DateTime(2026, 11, 19), DoneCount = 2 };
            Assert.AreEqual(Utc(2026, 11, 18, 15), RecordsText.WeekStartUtc(Utc(2026, 11, 20, 3), goal, Seoul), "목요일에 정했으면 목요일 0시부터");
            Assert.AreEqual("2/3", RecordsText.VerifiedCell(1, goal), "앱이 센 인증 횟수가 아니라 서버 doneCount(5분 이상만)");

            var none = new WeeklyGoal { TargetCount = null, DoneCount = 0 };
            Assert.AreEqual(Utc(2026, 11, 15, 15), RecordsText.WeekStartUtc(Utc(2026, 11, 20, 3), none, Seoul), "목표가 없으면 월요일");
            Assert.AreEqual(Utc(2026, 11, 15, 15), RecordsText.WeekStartUtc(Utc(2026, 11, 20, 3), (WeeklyGoal)null, Seoul), "못 받았으면 월요일");
            Assert.AreEqual("1회", RecordsText.VerifiedCell(1, none));
        }

        [Test]
        public void WeekSummaryCountsThisWeekOnly()
        {
            var start = Utc(2026, 11, 15, 15);
            var runs = new List<RunSummary>
            {
                Run("a", Utc(2026, 11, 18, 11), 3510, "VERIFIED"),
                Run("b", Utc(2026, 11, 16, 22), 3020, "UNVERIFIED"),
                Run("c", Utc(2026, 11, 15, 15), 2400, "PENDING"), // 월요일 0시 정각은 이번 주
                Run("d", Utc(2026, 11, 15, 14, 59), 5000, "VERIFIED"), // 지난주 일요일 밤
            };
            var w = RecordsText.Summarize(runs, start);
            Assert.AreEqual(3, w.Count);
            Assert.AreEqual(8930, w.DistanceM, "미인증·대기도 거리에 넣음(Figma F-01 3.51 + 3.02 + 2.40 = 8.9)");
            Assert.AreEqual(1, w.Verified);
            Assert.AreEqual("3회", RecordsText.Count(w.Count));
            Assert.AreEqual("8.9km", RecordsText.WeekKm(w.DistanceM));
            Assert.AreEqual(0, RecordsText.Summarize(null, start).Count);

            Assert.IsFalse(RecordsText.WeekNeedsMore(runs, "next", start), "마지막이 지난주면 더 안 받음");
            Assert.IsTrue(RecordsText.WeekNeedsMore(runs.GetRange(0, 3), "next", start), "마지막까지 이번 주고 다음 쪽이 있음");
            Assert.IsFalse(RecordsText.WeekNeedsMore(runs.GetRange(0, 3), null, start), "다음 쪽이 없음");
        }

        [Test]
        public void CardAndEmptyTexts()
        {
            Assert.AreEqual("33:02 · 평균 9'25\"/km", RecordsText.CardDetail(Run("a", Utc(2026, 11, 5), 3510, "VERIFIED", 1982, 565)));
            Assert.AreEqual("00:40", RecordsText.CardDetail(Run("b", Utc(2026, 11, 5), 0, "UNVERIFIED", 40, null)), "페이스를 모르면 시간만");
            Assert.AreEqual("11월에는 기록이 없어요", RecordsText.EmptyMonth("11월", "2026-11"));
            Assert.AreEqual(RecordsText.EmptyTitle, RecordsText.EmptyMonth(RecordsText.AllLabel, null));
            Assert.IsTrue(RunResultText.ChipWarn("UNVERIFIED"));
            Assert.IsFalse(RunResultText.ChipWarn("PENDING"), "인증 대기는 회색(목록·상세 같게)");
            Assert.IsFalse(RunResultText.ChipWarn("VERIFIED"));
        }

        static bool Shown(AppScreen s, string name) => s.Root.Q(name).style.display != DisplayStyle.None;

        [Test]
        public void ListShowsCardsSummaryAndEmptyState()
        {
            var s = new RecordsScreen();
            s.Create(null);
            Assert.AreEqual(3, s.Root.Q("months").childCount);
            Assert.IsTrue(s.Root.Q("months")[0].ClassListContains("rc-month--on"), "처음은 이번 달(Figma F-01)");
            Assert.IsFalse(Shown(s, "scroll") || Shown(s, "empty") || Shown(s, "state"), "받기 전엔 아무것도");

            var start = DateTime.UtcNow.AddDays(-1);
            var recent = new List<RunSummary>
            {
                Run("a", DateTime.UtcNow, 3510, "VERIFIED"),
                Run("b", DateTime.UtcNow.AddHours(-2), 2400, "UNVERIFIED"),
                Run("c", DateTime.UtcNow.AddDays(-9), 3100, "VERIFIED"),
                Run("p", DateTime.UtcNow.AddDays(-10), 2000, "PENDING"),
            };
            s.Show(recent, new RunListPage { Items = recent, NextCursor = "c2" }, start);
            Assert.IsTrue(Shown(s, "scroll"));
            Assert.IsFalse(Shown(s, "empty"));
            Assert.AreEqual(4, s.Root.Q("list").childCount);
            Assert.AreEqual("2회", s.Root.Q<Label>("week-count").text);
            Assert.AreEqual("5.9km", s.Root.Q<Label>("week-km").text);
            Assert.AreEqual("1회", s.Root.Q<Label>("week-verified").text);
            Assert.IsTrue(Shown(s, "more"), "다음 쪽이 있으면 더 보기");
            Assert.IsFalse(Shown(s, "month-empty"));
            var card = s.Root.Q("run-b");
            Assert.AreEqual("2.40km", card.Q<Label>(className: "fr-record-km").text);
            Assert.AreEqual("미인증", card.Q<Label>(className: "fr-chip-text").text);
            Assert.IsTrue(card.Q(className: "fr-chip").ClassListContains("rr-chip--warn"));
            var pending = s.Root.Q("run-p");
            Assert.AreEqual("인증 대기", pending.Q<Label>(className: "fr-chip-text").text);
            Assert.IsFalse(pending.Q(className: "fr-chip").ClassListContains("rr-chip--warn"), "대기는 회색");

            // 기록은 있는데 고른 달엔 없음 → 목록 화면에 안내
            s.Show(recent, new RunListPage(), start);
            Assert.IsTrue(Shown(s, "scroll") && Shown(s, "month-empty"));
            Assert.IsFalse(Shown(s, "more"));

            // 기록이 하나도 없음 → F-02
            s.Show(new List<RunSummary>(), new RunListPage(), start);
            Assert.IsTrue(Shown(s, "empty"));
            Assert.IsFalse(Shown(s, "scroll"));
            Assert.AreEqual(RecordsText.EmptyAction, s.Root.Q<Button>("start").text);
        }

        [Test]
        public void DetailIsTheResultScreenWithBackAndGhost()
        {
            var detail = RunResultScreen.Detail("run_1");
            detail.Create(null);
            Assert.AreEqual(RecordsText.DetailTitle, detail.Root.Q<Label>("title").text);
            Assert.IsTrue(Shown(detail, "back") && Shown(detail, "ghost"));
            Assert.IsFalse(Shown(detail, "close") || Shown(detail, "home") || Shown(detail, "records"), "F-03엔 닫기·홈으로·기록 보기 없음");
            Assert.AreEqual(RecordsText.GhostButton, detail.Root.Q<Button>("ghost").text);
            Assert.IsFalse(detail.Root.Q<Button>("ghost").enabledSelf, "기록을 받기 전엔 넘길 게 없어 꺼 둠");
            detail.Show(new RunDetail { RunId = "run_1", StartedAt = Utc(2026, 11, 3), DistanceM = 3000, DurationS = 1800, Verification = "PENDING" });
            Assert.IsTrue(detail.Root.Q<Button>("ghost").enabledSelf);
            Assert.IsFalse(detail.Root.Q("chip").ClassListContains("rr-chip--warn"), "대기는 회색(목록과 같게)");

            var result = new RunResultScreen("run_1");
            result.Create(null);
            Assert.AreEqual("운동 결과", result.Root.Q<Label>("title").text);
            Assert.IsTrue(Shown(result, "close") && Shown(result, "home") && Shown(result, "records"), "E-12 기록 보기 켬");
            Assert.IsFalse(Shown(result, "back") || Shown(result, "ghost"));
        }

        sealed class TabRoot : AppScreen
        {
            readonly string label;
            public int Shows;
            public TabRoot(string label) => this.label = label;
            protected override string Uxml => "UI/Placeholder";
            protected internal override void OnShow() => Shows++;
            public override string ToString() => label;
        }

        static Navigator Tabs()
        {
            var tabBar = new VisualElement();
            foreach (var n in new[] { "home", "records", "friends", "me" }) tabBar.Add(new Button { name = "tab-" + n });
            return new Navigator(new VisualElement(), tabBar, t => new TabRoot(t.ToString()));
        }

        static RunResultScreen OfflineResult()
        {
            var session = new RunSession(RunGoal.Default(GoalKind.Distance), () => 0);
            session.Start(0f);
            session.Finish(1200f, 9f);
            return RunResultScreen.Offline(session); // 서버를 안 부르는 결과 화면(같은 GoHome·GoRecords)
        }

        // #94 리뷰: 기록 탭에서 시작한 고스트 달리기 → 홈으로 = 홈 탭 첫 화면(홈 탭에 열어 둔 화면이 있어도), 그 화면은 다시 보이지 않음
        [Test]
        public void HomeFromAGhostRunStartedInRecordsIsTheHomeRoot()
        {
            var nav = Tabs();
            nav.ShowTabs(AppTab.Home);
            var oldDetail = new TabRoot("홈 탭 코스 상세");
            nav.Push(oldDetail);                     // 홈 탭에 코스 상세를 열어 둔 채
            nav.SelectTab(AppTab.Records);
            nav.Push(new TabRoot("기록 상세"));
            nav.Push(new TabRoot("코스 목록(고스트)"));
            var result = OfflineResult();
            nav.Push(result);
            int before = oldDetail.Shows;
            result.GoHome();
            Assert.AreEqual(AppTab.Home, nav.CurrentTab);
            Assert.AreEqual("Home", nav.Current.ToString(), "옛 코스 상세가 아니라 홈 첫 화면");
            Assert.IsFalse(nav.CanPop);
            Assert.AreEqual(before, oldDetail.Shows, "정리될 옛 화면은 잠깐도 보이지 않음(요청을 보내지 않게)");
            nav.SelectTab(AppTab.Records);
            Assert.AreEqual("Records", nav.Current.ToString(), "결과까지 쌓였던 기록 탭도 목록으로");
        }

        // #94 리뷰: "더 보기"로 펼친 목록은 다시 보일 때 첫 쪽만 합침(있던 카드는 그 자리에서 바뀌고 새 기록은 맨 위)
        [Test]
        public void ReshowKeepsLoadedPagesAndMergesTheFirstPage()
        {
            var s = new RecordsScreen();
            s.Create(null);
            var start = DateTime.UtcNow.AddDays(-1);
            var loaded = new List<RunSummary>
            {
                Run("a", Utc(2026, 11, 5), 3000, "PENDING"),
                Run("b", Utc(2026, 11, 4), 3000, "VERIFIED"),
                Run("c", Utc(2026, 11, 3), 3000, "VERIFIED"),
            };
            s.Show(loaded, new RunListPage { Items = loaded, NextCursor = "c3" }, start);
            var firstPage = new List<RunSummary> { Run("n", Utc(2026, 11, 6), 2000, "PENDING"), Run("a", Utc(2026, 11, 5), 3000, "VERIFIED") };
            s.Show(firstPage, new RunListPage { Items = firstPage, NextCursor = "c2" }, start, keepLoaded: true);
            var list = s.Root.Q("list");
            Assert.AreEqual(4, list.childCount, "받아 둔 쪽을 버리지 않음");
            Assert.AreEqual("run-n", list[0].name, "새 기록은 맨 위");
            Assert.AreEqual("run-a", list[1].name);
            Assert.AreEqual(RunResultText.Chip("VERIFIED").text, list[1].Q<Label>(className: "fr-chip-text").text, "있던 카드는 새 상태로");
            Assert.AreEqual("run-c", list[3].name);

            s.Show(firstPage, new RunListPage { Items = firstPage, NextCursor = "c2" }, start); // 당겨 새로고침·칩 바꿈
            Assert.AreEqual(2, s.Root.Q("list").childCount, "처음부터");
        }

        [Test]
        public void SeeRecordsGoesToTheListEvenIfAnOldDetailWasOpen()
        {
            var tabBar = new VisualElement();
            foreach (var n in new[] { "home", "records", "friends", "me" }) tabBar.Add(new Button { name = "tab-" + n });
            var nav = new Navigator(new VisualElement(), tabBar, t => new TabRoot(t.ToString()));
            nav.ShowTabs(AppTab.Records);
            nav.Push(new TabRoot("옛 상세"));        // 기록 탭에서 상세를 열어 둔 채
            nav.SelectTab(AppTab.Home);
            var session = new RunSession(RunGoal.Default(GoalKind.Distance), () => 0);
            session.Start(0f);
            session.Finish(1200f, 9f);
            var result = RunResultScreen.Offline(session); // 서버를 안 부르는 결과 화면(같은 GoRecords)
            nav.Push(new TabRoot("코스 상세"));
            nav.Push(result);
            result.GoRecords();
            Assert.AreEqual(AppTab.Records, nav.CurrentTab);
            Assert.AreEqual("Records", nav.Current.ToString(), "옛 상세가 아니라 목록");
            Assert.IsFalse(nav.CanPop);
        }

        [Test]
        public void GhostFromRecordIsPickedInRunSetup()
        {
            var ghost = new RunSummary { RunId = "run_g", StartedAt = Utc(2026, 10, 5, 1), DistanceM = 5130, DurationS = 1880, Verification = "UNVERIFIED" };
            var host = new VisualElement();
            new RunSetupView(host, new CourseDetail { CourseId = "crs_lake_loop", Name = "노을 호수 한 바퀴" },
                             new RunSetupPrefs(new MemoryPrefs()), ghost);
            Assert.IsTrue(host.Q("ghost-switch").ClassListContains("ghost-switch--on"), "고스트가 켜진 채");
            Assert.IsTrue(host.Q("ghost-picked").IsVisible());
            Assert.AreEqual("5.1km · 31:20", host.Q<Label>("ghost-summary").text);

            var list = new CourseListScreen(ghost);
            list.Create(null);
            Assert.AreEqual(CourseText.GhostLead, list.Root.Q<Label>("lead").text);
            Assert.IsTrue(Shown(list, "back"), "기록 탭에 쌓였을 땐 뒤로");
            var home = new CourseListScreen();
            home.Create(null);
            Assert.IsFalse(Shown(home, "back"), "홈 탭 C-01은 Figma대로 뒤로 없음");
        }
    }
}
