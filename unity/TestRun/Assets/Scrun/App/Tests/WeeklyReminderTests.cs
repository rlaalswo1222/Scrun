using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Scrun.App.Tests
{
    // 주간 목표 리마인더 예약 계산(BL-46, 계획 0079): 못 채운 주기의 마지막 이틀 오후 7시(폰 시계), 다음 주기 것도 미리
    public class WeeklyReminderTests
    {
        // 주기 11/20–11/26(마지막 날 포함, 서버가 사용자 시간대 날짜로 줌)
        static WeeklyGoal Goal(int target, int done, int? next = null) => new WeeklyGoal
        {
            TargetCount = target, DoneCount = done, NextTargetCount = next,
            CycleStart = new DateTime(2026, 11, 20), CycleEnd = new DateTime(2026, 11, 26),
        };

        static DateTime At(int month, int day, int hour, int minute = 0) => new DateTime(2026, month, day, hour, minute, 0);

        [Test]
        public void Not_done_gets_the_last_two_days_at_7pm_and_next_cycle_too()
        {
            var plan = WeeklyReminder.Plan(Goal(3, 1), At(11, 24, 9));
            CollectionAssert.AreEqual(new[] { At(11, 25, 19), At(11, 26, 19), At(12, 2, 19), At(12, 3, 19) }, plan.Select(n => n.At).ToArray());
            Assert.AreEqual(WeeklyReminder.DayBefore(2), plan[0].Body);
            Assert.AreEqual(WeeklyReminder.LastDay(2), plan[1].Body);
            Assert.AreEqual(WeeklyReminder.DayBefore(3), plan[2].Body, "다음 주기는 아직 0회라 목표 그대로");
            CollectionAssert.AllItemsAreUnique(plan.Select(n => n.Id));
            Assert.IsTrue(plan.All(n => WeeklyReminder.AllIds.Contains(n.Id)), "지울 때 쓰는 고정 Id 안에서만");
        }

        // 시각은 시간대 없는 폰 시계 날짜·시각(달력 트리거): 어느 시간대 날짜로 받든 그 날짜 19:00 그대로, UTC로 바꾸지 않음
        [Test]
        public void Times_are_wall_clock_dates_without_timezone()
        {
            var g = Goal(3, 1);
            g.CycleEnd = DateTime.SpecifyKind(new DateTime(2026, 11, 26), DateTimeKind.Utc); // JSON이 Utc로 읽혀도
            var plan = WeeklyReminder.Plan(g, At(11, 24, 9));
            Assert.AreEqual(At(11, 26, 19), new DateTime(plan[1].At.Ticks));
            Assert.AreEqual(19, plan[1].At.Hour);
        }

        [Test]
        public void Done_cycle_skips_to_the_next_one()
        {
            var plan = WeeklyReminder.Plan(Goal(3, 3), At(11, 24, 9));
            CollectionAssert.AreEqual(new[] { At(12, 2, 19), At(12, 3, 19) }, plan.Select(n => n.At).ToArray());
            Assert.IsEmpty(WeeklyReminder.Plan(Goal(3, 5), At(12, 4, 9)), "다음 주기도 지남");
        }

        [Test]
        public void Times_already_past_are_dropped()
        {
            var plan = WeeklyReminder.Plan(Goal(3, 0), At(11, 26, 19, 30)); // 마지막 날 저녁: 이번 주기는 다 지남
            Assert.AreEqual(At(12, 2, 19), plan.First().At);
            plan = WeeklyReminder.Plan(Goal(3, 0), At(11, 26, 12)); // 마지막 날 낮: 오늘 7시만 남음
            Assert.AreEqual(At(11, 26, 19), plan.First().At);
            Assert.AreEqual(WeeklyReminder.LastDay(3), plan.First().Body);
            Assert.IsFalse(WeeklyReminder.Plan(Goal(3, 0), At(11, 26, 19)).Any(n => n.At == At(11, 26, 19)), "딱 그 시각도 지난 것");
        }

        [Test]
        public void Changed_target_applies_from_the_next_cycle()
        {
            var plan = WeeklyReminder.Plan(Goal(3, 1, next: 5), At(11, 24, 9));
            Assert.AreEqual(WeeklyReminder.DayBefore(2), plan[0].Body);
            Assert.AreEqual(WeeklyReminder.DayBefore(5), plan[2].Body);
        }

        [Test]
        public void No_goal_means_nothing()
        {
            Assert.IsEmpty(WeeklyReminder.Plan(new WeeklyGoal(), At(11, 24, 9)));
            Assert.IsEmpty(WeeklyReminder.Plan(null, At(11, 24, 9)));
        }

        [Test]
        public void Turning_off_clears_the_schedule()
        {
            var fake = FakeNotifications.Instance;
            const string key = "scrun.reminder.weekly";
            string saved = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null; // 에디터에 켜 둔 값을 지우지 않게
            try
            {
                WeeklyReminder.SetEnabled(true);
                WeeklyReminder.Reschedule(new WeeklyGoal
                {
                    TargetCount = 3, DoneCount = 0, CycleStart = DateTime.Today, CycleEnd = DateTime.Today.AddDays(6),
                });
                Assert.IsNotEmpty(fake.Pending);
                WeeklyReminder.SetEnabled(false);
                Assert.IsEmpty(fake.Pending);
            }
            finally
            {
                if (saved == null) PlayerPrefs.DeleteKey(key);
                else PlayerPrefs.SetString(key, saved);
                PlayerPrefs.Save();
                WeeklyReminder.Forget(); // 받은 목표·예약을 다음 테스트에 남기지 않음
            }
        }
    }
}
