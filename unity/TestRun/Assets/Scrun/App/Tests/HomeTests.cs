using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0057(홈 B-01–B-04, 모양은 우주 시안 계획 0062): 상태 고르기·글자·레벨 막대·친구 줄·코스 글자. 서버 호출 없이(Show에 응답을 바로 넣음)
    public class HomeTests
    {
        static readonly RunSummary OneRun = new RunSummary { RunId = "run_1", StartedAt = DateTime.UtcNow, Verification = VerifyStatus.Unverified };

        static HomeData Data(int? target = null, int done = 0, bool runs = true, int friends = 0) => new HomeData
        {
            Nickname = "러너123",
            WeeklyGoal = new WeeklyGoal { TargetCount = target, DoneCount = done },
            Progress = new Progress { Level = 2, XpM = 4400, NextLevelAtM = 10000, NextLevelRemainingM = 5600, OutfitStage = 2,
                                      LevelStartsM = new List<int> { 0, 3000, 10000, 25000, 50000, 100000 } },
            RecentRuns = runs ? new List<RunSummary> { OneRun } : new List<RunSummary>(),
            RunningFriends = Friends(friends),
        };

        static List<RunningFriend> Friends(int n)
        {
            var list = new List<RunningFriend>();
            for (int i = 0; i < n; i++)
                list.Add(new RunningFriend { UserId = "u" + i, Nickname = "비락식혜" + i, CourseId = "crs_lake_loop", CourseName = "노을 호수 한 바퀴", DistanceM = 3200 });
            return list;
        }

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

        // ---------- 화면(우주 시안 B-01–B-04, 계획 0062) ----------
        [Test]
        public void FirstUseShowsFirstTitleAndRingWhenGoalSet()
        {
            var s = Screen(Data(3, 0, runs: false));
            Assert.AreEqual(HomeText.State.First, s.Shown);
            Assert.AreEqual("러너123", Text(s, "name"));
            Assert.AreEqual(HomeText.FirstTitle, Text(s, "title"));
            Assert.AreEqual(HomeText.FirstSub, Text(s, "sub"));
            Assert.IsTrue(Shown(s, "count-row"), "목표가 있으면 첫 이용도 0/3(시안 B-04)");
            Assert.AreEqual("0/3", Text(s, "done"));
            Assert.AreEqual(HomeText.Start, s.Root.Q<Button>("start").text);
            Assert.IsFalse(Shown(s, "set-goal"));

            var noGoal = Screen(Data(runs: false));
            Assert.IsFalse(Shown(noGoal, "count-row"), "목표가 없으면 진행 칸 없음");
        }

        [Test]
        public void NoGoalOffersSetGoalWithShortCard()
        {
            var s = Screen(Data());
            Assert.AreEqual(HomeText.NoGoalTitle, Text(s, "title"));
            Assert.AreEqual(HomeText.NoGoalSub, Text(s, "sub"));
            Assert.IsTrue(Shown(s, "set-goal"));
            Assert.AreEqual(HomeText.SetGoal, s.Root.Q<Button>("set-goal").text);
            Assert.IsTrue(s.Root.Q("top-card").ClassListContains("hs-hero--short"));
            Assert.IsFalse(Shown(s, "count-row"));
        }

        [Test]
        public void ProgressShowsRingAndRemainingTitle()
        {
            var s = Screen(Data(3, 2));
            Assert.IsTrue(Shown(s, "count-row"));
            Assert.AreEqual("2/3", Text(s, "done"));
            Assert.AreEqual("한 번만 더 달리면\n이번 주 목표를 채워요", Text(s, "title"));
            Assert.IsFalse(Shown(s, "sub"), "진행 중은 설명 없음");
            Assert.AreEqual("Lv.2", Text(s, "level-chip"));
            Assert.AreEqual(2f / 3f, HomeText.Ring(new WeeklyGoal { TargetCount = 3, DoneCount = 2 }), 1e-5f);
            Assert.AreEqual("3번 더 달리면\n이번 주 목표를 채워요", HomeText.ProgressTitle(new WeeklyGoal { TargetCount = 5, DoneCount = 2 }));
        }

        [Test]
        public void DoneShowsDoneTitle()
        {
            var s = Screen(Data(3, 4));
            Assert.AreEqual(HomeText.State.Done, s.Shown);
            Assert.AreEqual(HomeText.DoneTitle, Text(s, "title"));
            Assert.AreEqual("4/3", Text(s, "done"));
            Assert.AreEqual(1f, HomeText.Ring(new WeeklyGoal { TargetCount = 3, DoneCount = 4 }), 1e-5f, "넘쳐도 가득");
        }

        // ---------- 친구 ----------
        [Test]
        public void RunningFriendsInOneRowOrAddFriend()
        {
            var none = Screen(Data());
            Assert.IsTrue(Shown(none, "friends-empty"));
            Assert.IsFalse(Shown(none, "friends"));
            Assert.AreEqual(HomeText.NoFriendsSection, Text(none, "friends-title"));

            var two = Screen(Data(friends: 2));
            Assert.IsFalse(Shown(two, "friends-empty"));
            Assert.IsTrue(Shown(two, "friends"));
            Assert.AreEqual(HomeText.FriendsTitle, Text(two, "friends-title"));
            Assert.AreEqual("비락식혜0 · 비락식혜1", Text(two, "friends-names"));
            Assert.AreEqual("2명 달리는 중", Text(two, "friends-count"));
            Assert.IsTrue(Shown(two, "avatar-2"));

            var one = Screen(Data(friends: 1));
            Assert.IsFalse(Shown(one, "avatar-2"), "한 명이면 얼굴 하나");
            Assert.AreEqual("비락식혜0 · 비락식혜1 외 1명", HomeText.FriendNames(Friends(3)));
            var odd = Friends(2);
            odd.Insert(0, null);
            odd[1].Nickname = "";
            Assert.AreEqual("비락식혜1", HomeText.FriendNames(odd), "빈 줄·빈 이름은 빼고");
        }

        [Test]
        public void StartButtonWorksBeforeHomeLoads()
        {
            var s = new HomeScreen();
            s.Create(null);
            Assert.AreEqual(HomeText.Start, s.Root.Q<Button>("start").text);
            Assert.IsTrue(Shown(s, "start"));
            Assert.IsFalse(Shown(s, "top-part") || Shown(s, "bottom-part"), "받기 전엔 공통 칸(스켈레톤·실패)만");
        }

        [Test]
        public void HomeAndCoursesUseSpaceBackground()
        {
            Assert.AreEqual(SpaceArt.HomeRootClass, new HomeScreen().RootClass);
            Assert.AreEqual(SpaceArt.HomeRootClass, new CourseListScreen().RootClass);
            Assert.AreEqual(SpaceArt.CourseDetailRootClass, new CourseDetailScreen("crs").RootClass);
        }

        // ---------- 코스(C-01·C-02 글자) ----------
        [Test]
        public void CourseCardTexts()
        {
            Assert.AreEqual("한 바퀴 6.7km · 3D 루프 · 5구간", CourseText.CardMeta(6671));
            Assert.AreEqual("지금 12명", CourseText.LiveCount(12));
            Assert.AreEqual("6.7", CourseText.KmNumber(6671));
            var friends = Friends(3);
            friends[2].CourseId = "crs_other";
            Assert.AreEqual(2, CourseText.FriendsOn(friends, "crs_lake_loop"));
            Assert.AreEqual(0, CourseText.FriendsOn(null, "crs_lake_loop"));
            var runners = CourseParts.Runners(12);
            Assert.AreEqual(3, runners.childCount, "얼굴 둘 + \"+10\"");
            Assert.AreEqual("+10", ((Label)runners[2]).text);
            Assert.AreEqual(0, CourseParts.Runners(0).childCount);
            Assert.AreEqual(0, CourseParts.SpaceThumb(0).childCount, "아무도 없으면 LIVE 없음");
        }
    }
}
