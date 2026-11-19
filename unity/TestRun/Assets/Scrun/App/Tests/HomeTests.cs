using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0057(홈 B-01–B-04): 상태 고르기·글자·레벨 막대·코스 카드·친구 칸. 서버 호출 없이(Show에 응답을 바로 넣음)
    public class HomeTests
    {
        static readonly RunSummary OneRun = new RunSummary { RunId = "run_1", StartedAt = DateTime.UtcNow, Verification = VerifyStatus.Unverified };

        static HomeData Data(int? target = null, int done = 0, bool runs = true, int friends = 0, params Course[] courses) => new HomeData
        {
            Nickname = "러너123",
            WeeklyGoal = new WeeklyGoal { TargetCount = target, DoneCount = done },
            Progress = new Progress { Level = 2, XpM = 4400, NextLevelAtM = 10000, NextLevelRemainingM = 5600, OutfitStage = 2,
                                      LevelStartsM = new List<int> { 0, 3000, 10000, 25000, 50000, 100000 } },
            RecentRuns = runs ? new List<RunSummary> { OneRun } : new List<RunSummary>(),
            RunningFriends = Friends(friends),
            Courses = new List<Course>(courses),
        };

        static List<RunningFriend> Friends(int n)
        {
            var list = new List<RunningFriend>();
            for (int i = 0; i < n; i++)
                list.Add(new RunningFriend { UserId = "u" + i, Nickname = "비락식혜" + i, CourseId = "crs_lake_loop", CourseName = "노을 호수 한 바퀴", DistanceM = 3200 });
            return list;
        }

        static Course C(string id, int runners, string type = "LOOP_3D") =>
            new Course { CourseId = id, Name = id, Type = type, LengthM = 3000, RunnersNow = runners };

        static HomeScreen Screen(HomeData h)
        {
            var s = new HomeScreen();
            s.Create(null);
            s.Show(h);
            return s;
        }

        static string Text(HomeScreen s, string name) => s.Root.Q<Label>(name).text;
        static bool Shown(HomeScreen s, string name) => s.Root.Q(name).IsVisible();

        // ---------- 상태 고르기(정할 것 4 가: 끝낸 러닝 0개 = 첫 이용, 목표가 있어도) ----------
        [Test]
        public void PicksStateFromRunsAndGoal()
        {
            Assert.AreEqual(HomeText.State.First, HomeText.Pick(Data(runs: false)));
            Assert.AreEqual(HomeText.State.First, HomeText.Pick(Data(target: 3, runs: false)), "온보딩에서 목표를 정했어도 기록이 없으면 B-04");
            Assert.AreEqual(HomeText.State.NoGoal, HomeText.Pick(Data()));
            Assert.AreEqual(HomeText.State.Progress, HomeText.Pick(Data(3, 2)));
            Assert.AreEqual(HomeText.State.Done, HomeText.Pick(Data(3, 3)));
            Assert.AreEqual(HomeText.State.Done, HomeText.Pick(Data(3, 4)));
            Assert.AreEqual(HomeText.State.First, HomeText.Pick(null));
        }

        [Test]
        public void RemainingLineFollowsFigma()
        {
            Assert.AreEqual("한 번만 더 달리면 달성!", HomeText.RemainingLine(new WeeklyGoal { TargetCount = 3, DoneCount = 2 }));
            Assert.AreEqual("2번 더 달리면 달성!", HomeText.RemainingLine(new WeeklyGoal { TargetCount = 3, DoneCount = 1 }));
        }

        // E-01·오프라인 결과의 평균 페이스는 서버(runs._pace, 반올림)와 같게(리뷰)
        [Test]
        public void AvgPaceRoundsLikeServer()
        {
            Assert.AreEqual(331, RunResultText.AvgPace(1850, 612)); // 330.8
            Assert.AreEqual(330, RunResultText.AvgPace(1000, 330));
            Assert.IsNull(RunResultText.AvgPace(0, 100));
        }

        // ---------- 레벨 줄 ----------
        [Test]
        public void LevelLineAndFill()
        {
            var p = Data().Progress;
            Assert.AreEqual("기능성 세트 · 다음 레벨까지 5.6km", HomeText.LevelLine(p));
            Assert.AreEqual((4400 - 3000) / 7000f, HomeText.LevelFill(p), 1e-4);
            Assert.AreEqual(1f, HomeText.LevelFill(new Progress { Level = 6, XpM = 120000, NextLevelAtM = null, OutfitStage = 6 }), "마지막 레벨은 가득");
            Assert.AreEqual(0f, HomeText.LevelFill(null));
        }

        // ---------- 화면 ----------
        [Test]
        public void FirstUseShowsRunningCharacterAndFirstButton()
        {
            var s = Screen(Data(runs: false));
            Assert.AreEqual(HomeText.State.First, s.Shown);
            Assert.AreEqual("러너123 님", Text(s, "greeting"));
            Assert.AreEqual(HomeText.FirstKicker, Text(s, "kicker"));
            Assert.AreEqual(HomeText.FirstTitle, Text(s, "title"));
            Assert.AreEqual(HomeText.FirstSub, Text(s, "sub"));
            Assert.AreEqual(HomeText.StartFirst, s.Root.Q<Button>("start").text);
            Assert.IsTrue(s.Root.Q("character").ClassListContains("hm-character--run"));
            Assert.IsFalse(Shown(s, "count-row") || Shown(s, "set-goal"));
        }

        [Test]
        public void NoGoalOffersSetGoal()
        {
            var s = Screen(Data());
            Assert.AreEqual(HomeText.NoGoalTitle, Text(s, "title"));
            Assert.IsTrue(Shown(s, "set-goal"));
            Assert.AreEqual(HomeText.Continue, s.Root.Q<Button>("start").text);
        }

        [Test]
        public void ProgressShowsCountAndDots()
        {
            var s = Screen(Data(3, 2));
            Assert.IsTrue(Shown(s, "count-row"));
            Assert.IsFalse(Shown(s, "title"));
            Assert.AreEqual("2", Text(s, "done"));
            Assert.AreEqual("/ 3회", Text(s, "of"));
            var dots = s.Root.Q("dots");
            Assert.AreEqual(3, dots.childCount);
            Assert.IsTrue(dots[1].ClassListContains("wg-dot--done"));
            Assert.IsFalse(dots[2].ClassListContains("wg-dot--done"));
            Assert.AreEqual("한 번만 더 달리면 달성!", Text(s, "sub"));
            Assert.AreEqual("Lv.2", Text(s, "level-chip"));
        }

        [Test]
        public void DoneShowsStretchingCharacter()
        {
            var s = Screen(Data(3, 3));
            Assert.AreEqual(HomeText.DoneLine, Text(s, "sub"));
            Assert.IsTrue(s.Root.Q("character").ClassListContains("hm-character--stretch"));
        }

        // ---------- 코스·친구 ----------
        [Test]
        public void ShowsTwoRunnableCoursesInServerOrder()
        {
            var s = Screen(Data(courses: new[] { C("crs_video", 9, "VIDEO"), C("crs_b", 5), C("crs_a", 0), C("crs_c", 0) }));
            var cards = s.Root.Q("courses");
            Assert.AreEqual(2, cards.childCount, "실사 영상은 빼고 앞의 두 개");
            Assert.AreEqual("home-course-crs_b", cards[0].name);
            Assert.AreEqual("home-course-crs_a", cards[1].name);
            Assert.AreEqual("3.0km · 5명 달리는 중", HomeText.CourseMeta(C("x", 5)));
            Assert.AreEqual("3.0km · 0명 달리는 중", HomeText.CourseMeta(C("x", 0)));

            var one = Screen(Data(courses: new[] { C("crs_a", 0) }));
            Assert.AreEqual(2, one.Root.Q("courses").childCount, "하나면 카드 + 빈 반 칸");
        }

        [Test]
        public void FriendsOrEmptyCard()
        {
            var none = Screen(Data());
            Assert.IsTrue(Shown(none, "friends-empty"), "정할 것 1 가");
            Assert.AreEqual(0, none.Root.Q("friends").childCount);

            var two = Screen(Data(friends: 2));
            Assert.IsFalse(Shown(two, "friends-empty"));
            Assert.AreEqual(2, two.Root.Q("friends").childCount);
        }

        [Test]
        public void StartButtonWorksBeforeHomeLoads()
        {
            var s = new HomeScreen();
            s.Create(null);
            Assert.AreEqual(HomeText.StartPlain, s.Root.Q<Button>("start").text);
            Assert.IsTrue(Shown(s, "start"));
            Assert.IsFalse(Shown(s, "top-part") || Shown(s, "bottom-part"), "받기 전엔 공통 칸(스켈레톤·실패)만");
        }
    }
}
