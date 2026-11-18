using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0051: 운동 결과(E-12·E-14)·인증 후 반영(E-11)·레벨업(E-13)·대조(E-15) 글자와 계산, 화면 칸
    public class RunResultTests
    {
        static RunRewards Rewards(int before, int after, int lvBefore, int lvAfter, int? remaining = 6980) => new RunRewards
        {
            XpBeforeM = before, XpAfterM = after, LevelBefore = lvBefore, LevelAfter = lvAfter, LeveledUp = lvAfter > lvBefore,
            OutfitStageAfter = lvAfter, NextLevelRemainingM = remaining,
            WeeklyGoal = new WeeklyGoalReflect { Counted = true, DoneCount = 3, TargetCount = 3 },
        };

        [Test]
        public void DistanceAndUnitFollowVerification()
        {
            Assert.AreEqual("3.02", RunResultText.BigKm(3029), "버림(목록·피드와 같게)");
            Assert.AreEqual("km", RunResultText.Unit("VERIFIED"));
            Assert.AreEqual("km · 센서 값", RunResultText.Unit("UNVERIFIED"), "E-14");
            Assert.AreEqual(("인증 완료", true), RunResultText.Chip("VERIFIED"));
            Assert.AreEqual(("미인증", false), RunResultText.Chip("UNVERIFIED"));
            Assert.AreEqual(("인증 대기", false), RunResultText.Chip("PENDING"));
        }

        [Test]
        public void GoalPaceAndCompanions()
        {
            Assert.AreEqual("3.0km", RunResultText.Goal(new RunGoalBody { Type = "DISTANCE", Value = 3000 }));
            Assert.AreEqual("30분", RunResultText.Goal(new RunGoalBody { Type = "TIME", Value = 1800 }));
            Assert.AreEqual("없음", RunResultText.Goal(new RunGoalBody { Type = "NONE" }));
            Assert.AreEqual("9'30\"", RunResultText.Pace(570));
            Assert.AreEqual("-", RunResultText.Pace(null));

            Assert.IsNull(RunResultText.CompanionLine(new List<Companion>()), "혼자 달리면 줄 숨김");
            var three = new List<Companion>
            {
                new Companion { Nickname = "", Kind = "BOT" },
                new Companion { Nickname = "러너2", Kind = "RUNNER" },
                new Companion { Nickname = "비락식혜", Kind = "FRIEND" },
            };
            Assert.AreEqual("비락식혜 외 2명", RunResultText.CompanionLine(three), "친구 먼저, Figma E-12");
            Assert.AreEqual("러너2", RunResultText.CompanionLine(new List<Companion> { three[1] }));
        }

        [Test]
        public void WeeklyAndLevelLines()
        {
            var r = Rewards(1000, 4020, 1, 2);
            Assert.AreEqual("3 / 3회 달성", RunResultText.Weekly(r.WeeklyGoal));
            Assert.AreEqual("2 / 3회", RunResultText.Weekly(new WeeklyGoalReflect { DoneCount = 2, TargetCount = 3 }));
            Assert.IsNull(RunResultText.Weekly(new WeeklyGoalReflect { DoneCount = 1 }), "주간 목표가 없으면 카드 숨김");
            Assert.AreEqual("Lv.1 → Lv.2 레벨업!", RunResultText.LevelLine(r));
            Assert.AreEqual("Lv.2", RunResultText.LevelLine(Rewards(4000, 5000, 2, 2)));
            Assert.AreEqual("경험치 +3.02km", RunResultText.XpGain(r));
            Assert.AreEqual("다음 Lv.3까지 7.0km", RunResultText.NextLevel(r), "남은 거리는 올림(마이 탭과 같게)");
            Assert.AreEqual("러닝복을 모두 모았어요", RunResultText.NextLevel(Rewards(100000, 101000, 6, 6, null)));
        }

        [Test]
        public void BarFillsWithinTheLevelAndToTheEndOnLevelUp()
        {
            // 레벨 경계 0·3·10·25·50·100 km(H-04, 서버 기본값)
            Assert.AreEqual(0.5f, RunResultText.Bar(1500, 1), 1e-4f);
            Assert.AreEqual(0f, RunResultText.Bar(3000, 2), 1e-4f);
            Assert.AreEqual(1f, RunResultText.Bar(120000, 6), "마지막 레벨은 가득");
            var up = RunResultText.BarRange(Rewards(1500, 4500, 1, 2));
            Assert.AreEqual((0.5f, 1f), up, "레벨업: 지난 레벨 칸을 끝까지 → E-13");
            var same = RunResultText.BarRange(Rewards(3000, 6500, 2, 2));
            Assert.AreEqual(0f, same.from, 1e-4f);
            Assert.AreEqual(0.5f, same.to, 1e-4f);
            // 서버가 임계값을 바꾸면(Lv.2 = 5 km) 그 값으로(progress.levelStartsM)
            var server = new List<int> { 0, 5000, 12000 };
            Assert.AreEqual(0.5f, RunResultText.Bar(2500, 1, server), 1e-4f);
            Assert.AreEqual(0.5f, RunResultText.Bar(8500, 2, server), 1e-4f);
            Assert.AreEqual(1f, RunResultText.Bar(13000, 3, server), "서버 표의 마지막 레벨");
        }

        [Test]
        public void MarksAndImpacts()
        {
            Assert.AreEqual("+1 · 총 3회", RunResultText.Marks(3));
            var p = new Progress
            {
                VerifiedCount = 3,
                Impacts = new List<Impact>
                {
                    new Impact { Name = "스파크", UnlockAtCount = 1, Unlocked = true },
                    new Impact { Name = "불꽃", UnlockAtCount = 5, Unlocked = false },
                },
            };
            Assert.AreEqual("다음 임팩트까지 인증 2회", RunResultText.NextImpact(p));
            p.Impacts[1].Unlocked = true;
            Assert.IsNull(RunResultText.NextImpact(p), "다 열었으면 줄 숨김");
            Assert.AreEqual("새 임팩트 해금: 스파크", RunResultText.ImpactUnlocked(p.Impacts[0]));
        }

        [Test]
        public void CompareRowsAndSeconds()
        {
            var rows = RunResultText.Compare(new RunStatsBody { DistanceM = 3020, DurationS = 1721 },
                                             new RunStatsBody { DistanceM = 3000, DurationS = 1720 });
            Assert.AreEqual(("거리", "3.02km", "3.00km", "0.02km"), rows[0]);
            Assert.AreEqual(("시간", "28:41", "28:40", "1초"), rows[1]);
            Assert.IsEmpty(RunResultText.Compare(new RunStatsBody(), null), "계기판 값이 없으면 표 없음");
            Assert.AreEqual("2분 5초", RunResultText.Seconds(125));
            Assert.AreEqual("3분", RunResultText.Seconds(180));
        }

        [Test]
        public void DownsampleAverages()
        {
            var speeds = new List<float>();
            for (int i = 0; i < 240; i++) speeds.Add(i < 120 ? 6f : 12f);
            var d = RunResultText.Downsample(speeds, 120);
            Assert.AreEqual(120, d.Count);
            Assert.AreEqual(6f, d[0]);
            Assert.AreEqual(12f, d[119]);
            Assert.AreEqual(3, RunResultText.Downsample(new List<float> { 1, 2, 3 }, 120).Count, "적으면 그대로");
        }

        [Test]
        public void ResultScreenShowsVerifiedAndUnverified()
        {
            var s = new RunResultScreen("run_1");
            s.Create(null);
            bool Shown(string n) => s.Root.Q(n).style.display != DisplayStyle.None;
            Assert.IsTrue(Shown("records"), "기록 탭(F-01, 계획 0053)으로");
            var d = new RunDetail
            {
                RunId = "run_1", StartedAt = new DateTime(2026, 11, 3, 22, 40, 0, DateTimeKind.Utc), DistanceM = 3020, DurationS = 1721,
                AvgPaceSPerKm = 570, Verification = "VERIFIED", Shared = true, Goal = new RunGoalBody { Type = "DISTANCE", Value = 3000 },
                Sensor = new RunStatsBody { DistanceM = 3020, DurationS = 1721 }, Console = new RunStatsBody { DistanceM = 3000, DurationS = 1720 },
                SpeedSeries = new SpeedSeriesBody { IntervalS = 10, SpeedsKmh = new List<float> { 8, 9, 10, 9 } },
                Companions = new List<Companion> { new Companion { Nickname = "비락식혜", Kind = "FRIEND" } },
            };
            s.Show(d);
            Assert.AreEqual("3.02", s.Root.Q<Label>("km").text);
            Assert.AreEqual("인증 완료", s.Root.Q<Label>("chip-text").text);
            Assert.AreEqual("3.0km", s.Root.Q<Label>("goal").text);
            Assert.IsTrue(Shown("companions") && Shown("graph-card") && Shown("compare") && Shown("share"));
            Assert.IsFalse(Shown("note"));
            Assert.IsTrue(s.Root.Q("share-switch").ClassListContains("my-switch--on"));

            d.Verification = "UNVERIFIED";
            d.Console = null;
            d.Companions.Clear();
            s.Show(d);
            Assert.AreEqual("km · 센서 값", s.Root.Q<Label>("unit").text, "E-14");
            Assert.AreEqual(RunResultText.UnverifiedNote, s.Root.Q<Label>("note").text);
            Assert.IsFalse(Shown("compare") || Shown("companions"));
        }

        [Test]
        public void OfflineResultUsesThisPhone()
        {
            var session = new RunSession(RunGoal.Default(GoalKind.Distance), () => 0);
            session.Start(0f);
            session.Finish(1200f, 9f);
            var s = RunResultScreen.Offline(session);
            s.Create(null);
            Assert.AreEqual(RunResultText.OfflineNote, s.Root.Q<Label>("note").text);
            Assert.AreEqual(DisplayStyle.None, s.Root.Q("share").style.display.value, "서버에 없어 공유 못 함");
            Assert.AreEqual(DisplayStyle.None, s.Root.Q("compare").style.display.value);
        }

        [Test]
        public void RewardsScreenShowsLevelUp()
        {
            var s = new RunRewardsScreen("run_1");
            s.Create(null);
            s.Show(Rewards(1500, 4520, 1, 2), new Progress { VerifiedCount = 1, Impacts = new List<Impact>() });
            bool Shown(string n) => s.Root.Q(n).style.display != DisplayStyle.None;
            Assert.AreEqual("3 / 3회 달성", s.Root.Q<Label>("weekly-text").text);
            Assert.AreEqual(3, s.Root.Q("weekly-dots").childCount);
            Assert.AreEqual("Lv.1 → Lv.2 레벨업!", s.Root.Q<Label>("level-line").text);
            Assert.IsTrue(Shown("outfit-before"), "레벨업이면 러닝복 전후");
            Assert.AreEqual("Lv.2 기능성 세트", s.Root.Q<Label>("outfit-after-name").text);
            Assert.AreEqual("+1 · 총 1회", s.Root.Q<Label>("marks").text);
            Assert.IsFalse(Shown("levelup"), "E-13은 바가 찬 뒤");
            Assert.AreEqual("기능성 세트", s.Root.Q<Label>("levelup-outfit").text);

            s.Show(Rewards(3000, 6500, 2, 2), new Progress { VerifiedCount = 2 });
            Assert.IsFalse(Shown("outfit-before"), "그대로면 지금 러닝복만");
        }
    }
}
