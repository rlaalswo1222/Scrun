using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0054: 주간 목표 현황(H-07)·변경 시트(H-08) 글자·계산과 화면 칸
    public class WeeklyGoalTests
    {
        static WeeklyGoal Goal(int target, int done, int? next = null, params (int target, int done)[] history)
        {
            var g = new WeeklyGoal { TargetCount = target, DoneCount = done, NextTargetCount = next, DaysLeft = 3,
                                     CycleStart = new DateTime(2026, 11, 17), CycleEnd = new DateTime(2026, 11, 23) };
            var start = new DateTime(2026, 11, 17).AddDays(-7 * history.Length);
            foreach (var (t, d) in history)
            {
                g.History.Add(new WeeklyGoalCycle { CycleStart = start, TargetCount = t, DoneCount = d });
                start = start.AddDays(7);
            }
            return g;
        }

        [Test]
        public void CardTextAndDots()
        {
            var g = Goal(3, 2);
            Assert.AreEqual("2", WeeklyGoalText.Done(g));
            Assert.AreEqual("/ 3회", WeeklyGoalText.Of(g));
            Assert.AreEqual((3, 2), (WeeklyGoalText.Dots(g), WeeklyGoalText.Filled(g)));
            Assert.AreEqual(3, WeeklyGoalText.Filled(Goal(3, 5)), "목표를 넘겨도 동그라미는 목표 수까지");
            Assert.AreEqual("5", WeeklyGoalText.Done(Goal(3, 5)), "글자는 실제 횟수");
            Assert.IsNull(WeeklyGoalText.NextLine(g));
            Assert.AreEqual("다음 주부터 주 5회", WeeklyGoalText.NextLine(Goal(3, 2, next: 5)));
            Assert.IsFalse(WeeklyGoalText.HasGoal(new WeeklyGoal()));
            Assert.IsFalse(WeeklyGoalText.HasGoal(null));
        }

        [Test]
        public void BarsAreHistoryThenThisWeek()
        {
            var bars = WeeklyGoalText.Bars(Goal(3, 1, null, (3, 3), (2, 1), (3, 0)));
            Assert.AreEqual(4, bars.Count);
            Assert.AreEqual(new[] { "1", "2", "3", "이번" }, bars.ConvertAll(b => b.Label).ToArray());
            Assert.AreEqual(WeeklyGoalText.BarKind.Achieved, bars[0].Kind);
            Assert.AreEqual(1f, bars[0].Height, 1e-4f);
            Assert.AreEqual(WeeklyGoalText.BarKind.Missed, bars[1].Kind);
            Assert.AreEqual(0.5f, bars[1].Height, 1e-4f, "주기마다 그때 목표로(2회 중 1)");
            Assert.AreEqual(WeeklyGoalText.MinBar, bars[2].Height, 1e-4f, "0회도 조금 보이게");
            Assert.AreEqual(WeeklyGoalText.BarKind.Current, bars[3].Kind);
            Assert.AreEqual(1f / 3f, bars[3].Height, 1e-4f);
            Assert.AreEqual(1f, WeeklyGoalText.Bars(Goal(2, 4))[0].Height, 1e-4f, "넘겨도 가득까지");
            Assert.IsEmpty(WeeklyGoalText.Bars(new WeeklyGoal()), "목표 없음");
        }

        [Test]
        public void SheetStartsFromPendingThenCurrentThenDefault()
        {
            Assert.AreEqual(5, WeeklyGoalText.SheetStart(Goal(3, 0, next: 5)));
            Assert.AreEqual(3, WeeklyGoalText.SheetStart(Goal(3, 0)));
            Assert.AreEqual(WeeklyGoalText.Default, WeeklyGoalText.SheetStart(new WeeklyGoal()));
            Assert.AreEqual(1, WeeklyGoalText.Clamp(0));
            Assert.AreEqual(7, WeeklyGoalText.Clamp(9));
            Assert.AreEqual(WeeklyGoalText.SheetNoteFirst, WeeklyGoalText.SheetNote(new WeeklyGoal()));
            Assert.AreEqual(WeeklyGoalText.SheetNoteChange, WeeklyGoalText.SheetNote(Goal(3, 0)));
        }

        static bool Shown(AppScreen s, string name)
        {
            for (var e = s.Root.Q(name); e != null; e = e.parent)
                if (e.style.display == DisplayStyle.None) return false;
            return true;
        }

        [Test]
        public void StatusScreenShowsWeekBarsAndPending()
        {
            var s = new WeeklyGoalStatusScreen();
            s.Create(null);
            Assert.IsFalse(Shown(s, "content") || Shown(s, "action"), "받기 전엔 숨김");
            s.Show(Goal(3, 2, next: 4, (3, 3), (3, 1)));
            Assert.IsTrue(Shown(s, "week-card") && Shown(s, "bars-card"));
            Assert.IsFalse(Shown(s, "empty"));
            Assert.AreEqual("2", s.Root.Q<Label>("done").text);
            Assert.AreEqual("/ 3회", s.Root.Q<Label>("of").text);
            var dots = s.Root.Q("dots");
            Assert.AreEqual(3, dots.childCount);
            Assert.IsTrue(dots[1].ClassListContains("wg-dot--done"));
            Assert.IsFalse(dots[2].ClassListContains("wg-dot--done"));
            Assert.AreEqual("다음 주부터 주 4회", s.Root.Q<Label>("next").text);
            Assert.AreEqual(3, s.Root.Q("bars").childCount, "지난 2 + 이번");
            Assert.AreEqual(WeeklyGoalText.Change, s.Root.Q<Button>("action").text);

            s.Show(Goal(3, 0));
            Assert.IsFalse(Shown(s, "next"), "예약이 없으면 줄 숨김");
        }

        [Test]
        public void EmptyGoalOffersTheSameSheet()
        {
            var s = new WeeklyGoalStatusScreen();
            s.Create(null);
            s.Show(new WeeklyGoal());
            Assert.IsTrue(Shown(s, "empty"));
            Assert.IsFalse(Shown(s, "week-card") || Shown(s, "bars-card"));
            Assert.AreEqual(WeeklyGoalText.SetFirst, s.Root.Q<Button>("action").text);
            s.OpenSheet();
            Assert.IsTrue(s.SheetOpen);
            Assert.AreEqual(WeeklyGoalText.Default, s.Picked);
            Assert.AreEqual("3", s.Root.Q<Label>("goal-number").text);
            Assert.IsTrue(s.Root.Q("goal-chip-3").ClassListContains("wg-chip--on"));
        }

        [Test]
        public void SheetStepsStayWithinOneToSeven()
        {
            var s = new WeeklyGoalStatusScreen();
            s.Create(null);
            s.Show(Goal(7, 1));
            s.OpenSheet();
            Assert.AreEqual(7, s.Picked);
            Assert.IsFalse(s.Root.Q<Button>("goal-plus").enabledSelf, "7이면 + 꺼짐");
            s.Pick(8);
            Assert.AreEqual(7, s.Picked);
            s.Pick(1);
            Assert.IsFalse(s.Root.Q<Button>("goal-minus").enabledSelf);
            Assert.IsTrue(s.Root.Q("goal-chip-1").ClassListContains("wg-chip--on"));
            Assert.IsFalse(s.Root.Q("goal-chip-7").ClassListContains("wg-chip--on"));
        }

        [Test]
        public void WeeklyGoalReadsCycleAndHistory()
        {
            var saved = System.Threading.SynchronizationContext.Current;
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                // 서버 예시(docs/api/examples/GET_users_me_weekly_goal.json) 모양 + 이력 한 줄
                var http = new FakeHttp
                {
                    Handler = _ => FakeHttp.Ok("{\"targetCount\":3,\"cycleStart\":\"2026-11-06\",\"cycleEnd\":\"2026-11-12\",\"doneCount\":1," +
                                               "\"daysLeft\":7,\"nextTargetCount\":4,\"history\":[{\"cycleStart\":\"2026-10-30\",\"targetCount\":3,\"doneCount\":3}]}")
                };
                var session = new Session(new MemorySecureStore());
                session.SetTokens("access", "refresh");
                var users = new UsersApi(new ApiClient(http, session, () => "http://test"));
                var t = users.PutWeeklyGoalAsync(new WeeklyGoalUpdate { TargetCount = 4 });
                Assert.IsTrue(t.IsCompleted);
                var g = t.GetAwaiter().GetResult();
                Assert.AreEqual(new DateTime(2026, 11, 6), g.CycleStart.Value.Date);
                Assert.AreEqual(new DateTime(2026, 11, 12), g.CycleEnd.Value.Date);
                Assert.AreEqual((3, 1, 4), (g.TargetCount.Value, g.DoneCount, g.NextTargetCount.Value));
                Assert.AreEqual(1, g.History.Count);
                Assert.AreEqual((3, 3), (g.History[0].TargetCount, g.History[0].DoneCount));
                StringAssert.Contains("\"targetCount\":4", http.Sent[0].Body);
            }
            finally
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(saved);
            }
        }
    }
}
