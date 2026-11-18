using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0044(S7 친구): 코드 정리·오류 문구, 숫자·시각 글자, 친구 API 응답 읽기, 러너 카드 친구 배지
    public class FriendsTests
    {
        static readonly TimeZoneInfo Seoul = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
        static DateTime Utc(int y, int mo, int d, int h, int mi) => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);

        // ---- 코드 ----

        [Test]
        public void CodeIsNormalizedLikeTheServer()
        {
            Assert.AreEqual("K7Q2M9XA", FriendText.NormalizeCode("k7q2m9xa"));
            Assert.AreEqual("K7Q2M9XA", FriendText.NormalizeCode(" K7Q2-M9XA "));
            Assert.IsNull(FriendText.NormalizeCode("K7Q2M9X"), "7글자");
            Assert.IsNull(FriendText.NormalizeCode("K7Q2M9XAB"), "9글자");
            Assert.IsNull(FriendText.NormalizeCode("B0A4C812"), "0은 안 쓰는 글자");
            Assert.IsNull(FriendText.NormalizeCode("SCR-7K2Q"), "옛 모양");
            Assert.IsNull(FriendText.NormalizeCode(null));
        }

        [Test]
        public void CodeIsPickedFromAPastedShareMessage()
        {
            var msg = FriendText.ShareMessage("K7Q2M9XA");
            Assert.That(msg.Length, Is.LessThanOrEqualTo(FriendText.InputMax));
            Assert.AreEqual("K7Q2M9XA", FriendText.NormalizeCode(msg));
            Assert.IsNull(FriendText.NormalizeCode("K7Q2M9XA 또는 ZZZZZZZZ"), "후보가 둘이면 고르지 않음");
            Assert.IsNull(FriendText.NormalizeCode("안녕하세요 반가워요"));
        }

        [Test]
        public void InputErrorBeforeSending()
        {
            Assert.IsNull(FriendText.InputError("k7q2m9xa"));
            Assert.AreEqual("8자리 코드를 입력해 주세요.", FriendText.InputError("K7Q2"));
            Assert.AreEqual("8자리 코드를 입력해 주세요.", FriendText.InputError(""));
            Assert.AreEqual("존재하지 않는 코드예요. 다시 확인해 주세요.", FriendText.InputError("B0A4C812"), "8글자인데 안 쓰는 글자 → 없는 코드");
        }

        [Test]
        public void AddErrorsAreSplitByCase()
        {
            Assert.AreEqual("존재하지 않는 코드예요. 다시 확인해 주세요.", FriendText.AddError("FRIEND_CODE_NOT_FOUND"), "Figma G-08");
            var cases = new[] { "SELF_FRIEND_CODE", "ALREADY_FRIEND", "RATE_LIMITED" }.Select(c => FriendText.AddError(c)).ToList();
            Assert.That(cases, Has.None.Null);
            Assert.AreEqual(cases.Count, cases.Distinct().Count(), "케이스마다 다른 문구");
            StringAssert.Contains("상대", FriendText.AddError("FRIEND_LIMIT_EXCEEDED", "FRIEND"));
            StringAssert.Contains("50명까지", FriendText.AddError("FRIEND_LIMIT_EXCEEDED", "ME"));
            Assert.IsNull(FriendText.AddError("INTERNAL_ERROR"), "모르는 코드는 공통 안내창");
        }

        // ---- 숫자·시각 ----

        [Test]
        public void NumbersMatchFigma()
        {
            Assert.AreEqual("3.2km", FriendText.KmShort(3299), "버림");
            Assert.AreEqual("0.0km", FriendText.KmShort(-5));
            Assert.AreEqual("3.51km", FriendText.Km(3519));
            Assert.AreEqual("33:02", FriendText.Duration(1982));
            Assert.AreEqual("1:05:09", FriendText.Duration(3909));
            Assert.AreEqual("9'25\"/km", FriendText.Pace(565));
            Assert.IsNull(FriendText.Pace(null));
            var run = new FriendRun { DistanceM = 3510, DurationS = 1982, AvgPaceSPerKm = 565 };
            Assert.AreEqual("3.51km  ·  33:02", FriendText.RunStats(run));
            Assert.AreEqual("33:02 · 평균 9'25\"/km", FriendText.RunDetail(run));
            Assert.AreEqual("33:02", FriendText.RunDetail(new FriendRun { DurationS = 1982 }));
        }

        [Test]
        public void AgoAndRunDate()
        {
            var now = Utc(2026, 11, 20, 12, 0);
            Assert.AreEqual("방금 전", FriendText.Ago(now.AddSeconds(-30), now));
            Assert.AreEqual("20분 전", FriendText.Ago(now.AddMinutes(-20), now));
            Assert.AreEqual("1시간 전", FriendText.Ago(now.AddMinutes(-61), now));
            Assert.AreEqual("3일 전", FriendText.Ago(now.AddDays(-3), now));
            Assert.AreEqual("11.05", FriendText.Ago(Utc(2026, 11, 5, 11, 10), now, Seoul), "일주일 넘으면 날짜");
            Assert.AreEqual("11.05 (목) 오후 8:10", FriendText.RunDate(Utc(2026, 11, 5, 11, 10), Seoul));
            Assert.AreEqual("11.06 (금) 오전 12:05", FriendText.RunDate(Utc(2026, 11, 5, 15, 5), Seoul), "자정은 오전 12시");
        }

        [Test]
        public void StatusAndRunningLines()
        {
            var now = Utc(2026, 11, 20, 12, 0);
            Assert.AreEqual("3.2km 달리는 중", FriendText.StatusLine(new Friend { RunningNow = true, RunningDistanceM = 3200 }, now));
            Assert.AreEqual("달리는 중", FriendText.StatusLine(new Friend { RunningNow = true }, now), "옛 앱이라 거리를 모름");
            Assert.AreEqual("20분 전 러닝", FriendText.StatusLine(new Friend { LastRunAt = now.AddMinutes(-20) }, now));
            Assert.AreEqual("아직 러닝 기록이 없어요", FriendText.StatusLine(new Friend(), now));
            var f = new RunningFriend { CourseName = "노을 호수 한 바퀴", DistanceM = 3200, PaceSPerKm = 350 };
            Assert.AreEqual("3.2km 달리는 중 · 노을 호수 한 바퀴", FriendText.RunningLine(f));
            Assert.AreEqual("노을 호수 한 바퀴 · 3.2km · 5'50\"/km", FriendText.RunTogetherLine(f));
            Assert.AreEqual("3.2km", FriendText.RunTogetherLine(new RunningFriend { DistanceM = 3200 }));
        }

        [Test]
        public void VerificationChipHidesPending()
        {
            Assert.AreEqual(("인증 완료", true), FriendText.VerificationChip("VERIFIED"));
            Assert.AreEqual(("미인증", false), FriendText.VerificationChip("UNVERIFIED"));
            Assert.IsNull(FriendText.VerificationChip("PENDING"));
        }

        // ---- API ----

        SynchronizationContext saved;
        FakeHttp http;
        FriendsApi api;

        [SetUp]
        public void SetUp()
        {
            saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            http = new FakeHttp();
            var session = new Session(new MemorySecureStore());
            session.SetTokens("a", "r");
            api = new FriendsApi(new ApiClient(http, session, () => "http://test"));
            FriendDirectory.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SynchronizationContext.SetSynchronizationContext(saved);
            FriendDirectory.Clear();
        }

        static T Run<T>(Task<T> t)
        {
            Assert.IsTrue(t.IsCompleted);
            return t.GetAwaiter().GetResult();
        }

        [Test]
        public void ReadsFriendsFeedAndProfile()
        {
            http.Handler = r => r.Url.EndsWith("/v1/friends")
                ? FakeHttp.Ok("[{\"userId\":\"u1\",\"nickname\":\"비락식혜\",\"level\":null,\"verifiedCount\":5,\"runningNow\":true," +
                              "\"runningDistanceM\":3200,\"lastRunAt\":\"2026-11-20T11:40:00Z\"}]")
                : r.Url.Contains("/v1/feed")
                    ? FakeHttp.Ok("{\"items\":[{\"runId\":\"r1\",\"startedAt\":\"2026-11-20T11:00:00Z\",\"endedAt\":\"2026-11-20T11:33:02Z\"," +
                                  "\"courseId\":\"crs_lake_loop\",\"courseName\":\"노을 호수\",\"distanceM\":3510,\"durationS\":1982," +
                                  "\"avgPaceSPerKm\":565,\"verification\":\"UNVERIFIED\",\"feedId\":\"r1\",\"userId\":\"u1\",\"nickname\":\"비락식혜\"}]," +
                                  "\"nextCursor\":\"c2\"}")
                    : FakeHttp.Ok("{\"userId\":\"u1\",\"nickname\":\"비락식혜\",\"level\":3,\"verifiedCount\":5,\"runningNow\":false," +
                                  "\"outfitStage\":2,\"recentRuns\":[]}");
            var list = Run(api.ListAsync());
            Assert.AreEqual(1, list.Count);
            Assert.IsNull(list[0].Level);
            Assert.AreEqual(3200, list[0].RunningDistanceM);
            Assert.AreEqual(Utc(2026, 11, 20, 11, 40), list[0].LastRunAt.Value.ToUniversalTime());

            var feed = Run(api.FeedAsync("a b"));
            Assert.That(http.Sent.Last().Url, Does.EndWith("/v1/feed?cursor=a%20b"));
            Assert.AreEqual("UNVERIFIED", feed.Items[0].Verification);
            Assert.AreEqual("c2", feed.NextCursor);

            var p = Run(api.ProfileAsync("u1"));
            Assert.AreEqual(3, p.Level);
            Assert.AreEqual(0, p.RecentRuns.Count);
        }

        [Test]
        public void AddFailureCarriesWho()
        {
            http.Handler = _ => new HttpResponse
            {
                Status = 409,
                Body = "{\"resultType\":\"FAIL\",\"success\":null,\"error\":{\"code\":\"FRIEND_LIMIT_EXCEEDED\",\"message\":\"x\"," +
                       "\"details\":{\"who\":\"FRIEND\"}},\"meta\":{\"requestId\":\"req_f\"}}"
            };
            var t = api.AddAsync("K7Q2M9XA");
            Assert.IsTrue(t.IsCompleted);
            var e = Assert.Throws<ApiException>(() => t.GetAwaiter().GetResult());
            Assert.AreEqual("FRIEND_LIMIT_EXCEEDED", e.Code);
            StringAssert.Contains("\"who\":\"FRIEND\"", e.DetailsJson);
            Assert.That(http.Sent[0].Body, Does.Contain("\"friendCode\":\"K7Q2M9XA\""));
        }

        // ---- 화면 ----

        [Test]
        public void ScreensHaveEveryNamedElement()
        {
            // UXML에 이름이 없으면 Q가 바로 예외. 서버는 보일 때(OnShow) 부르므로 만들기만으로는 안 부름
            AppScreen[] screens = { new FriendsScreen(), new FriendAddScreen(), new FriendAddedScreen(new Friend { Nickname = "비락식혜" }),
                                    new FriendProfileScreen("u1"), new FriendManageScreen() };
            foreach (var s in screens) s.Create(null);
            Assert.That(http.Sent, Is.Empty);
            StringAssert.Contains("비락식혜 님과", screens[2].Root.Q<Label>("title").text);
        }

        [Test]
        public void AddScreenSwitchesToTypingAndEnablesButtonFor8Chars()
        {
            var s = new FriendAddScreen();
            s.Create(null);
            var input = s.Root.Q<TextField>("code");
            var submit = s.Root.Q<Button>("submit");
            Assert.AreEqual("친구를 추가해요", s.Root.Q<Label>("title").text);
            Assert.IsTrue(s.Root.Q("my-code-card").IsVisible());
            Assert.IsTrue(submit.ClassListContains("btn-soft"));

            input.value = "k7q2";
            s.Refresh();
            Assert.AreEqual("코드를 입력해 주세요", s.Root.Q<Label>("title").text, "G-06");
            Assert.IsFalse(s.Root.Q("my-code-card").IsVisible());
            Assert.IsFalse(submit.ClassListContains("btn-primary"));

            input.value = "k7q2-m9xa";
            s.Refresh();
            Assert.IsTrue(submit.ClassListContains("btn-primary"), "8글자 모양이면 진한 버튼");
        }

        [Test]
        public void SheetsBuildAndClose()
        {
            var s = new FriendProfileScreen("u1");
            s.Create(null);
            var run = FriendParts.RunTogether(s, new RunningFriend { UserId = "u1", Nickname = "비락식혜", CourseId = "crs_lake_loop", DistanceM = 3200 });
            Assert.IsTrue(run.Open);
            Assert.IsNotNull(s.Root.Q("run-together-go"));
            s.Root.Q<Button>("run-together-close");
            run.Close();
            Assert.IsFalse(run.Open);
            var del = FriendParts.ConfirmDelete(s, "u1", "비락식혜", 7, null);
            Assert.IsNotNull(s.Root.Q("delete-confirm"));
            StringAssert.Contains("친구 7명 중 한 명", string.Join("|", s.Root.Query<Label>().ToList().Select(l => l.text)));
            del.Close();
            Assert.That(http.Sent, Is.Empty, "시트를 열고 닫기만 하면 서버를 안 부름");
        }

        // ---- 러너 카드 ----

        [Test]
        public void RunnerCardShowsFriendBadge()
        {
            double now = 0;
            var room = new RunTogetherRoom(() => now);
            room.Apply("{\"type\":\"welcome\",\"you\":\"me\",\"courseLengthM\":1000,\"runners\":[" +
                       "{\"userId\":\"u1\",\"nickname\":\"비락식혜\",\"d\":100,\"v\":9,\"running\":true}]}");
            Assert.IsNull(RunnerTagsView.CardText(room.Runners[0], 88f, 1000f, now).badge);
            FriendDirectory.Set(new[] { new Friend { UserId = "u1" } });
            Assert.IsTrue(FriendDirectory.Contains("u1"));
            Assert.AreEqual("친구", RunnerTagsView.CardText(room.Runners[0], 88f, 1000f, now, FriendDirectory.Contains("u1")).badge);
            FriendDirectory.Remove("u1");
            Assert.IsFalse(FriendDirectory.Contains("u1"));
        }

        // ---- 같이 달리기(계획 0047) ----

        [Test]
        public void RunTogetherPostsCourseAndReadsNearFriend()
        {
            http.Handler = _ => new HttpResponse
            {
                Status = 201,
                Body = "{\"resultType\":\"SUCCESS\",\"success\":{\"data\":{\"mapInstanceId\":\"map_crs_lake_loop\"," +
                       "\"courseId\":\"crs_lake_loop\",\"entryPositionM\":982.1,\"realtime\":null," +
                       "\"nearFriend\":{\"userId\":\"u2\",\"behindM\":17.9}}},\"error\":null,\"meta\":{}}"
            };
            var t = api.RunTogetherAsync("u2", "crs_lake_loop");
            Assert.IsTrue(t.IsCompleted);
            var e = t.GetAwaiter().GetResult();
            Assert.AreEqual("POST", http.Sent[0].Method);
            StringAssert.EndsWith("/v1/friends/u2/run-together", http.Sent[0].Url);
            Assert.That(http.Sent[0].Body, Does.Contain("\"courseId\":\"crs_lake_loop\""));
            Assert.AreEqual(982.1f, e.EntryPositionM, 0.01f);
            Assert.AreEqual("u2", e.NearFriend.UserId);
            Assert.AreEqual(17.9f, e.NearFriend.BehindM, 0.01f);
        }

        static RemoteRunner Runner(string id, float d, bool bot = false, float lap = 6670f)
        {
            var r = new RemoteRunner(id, id, lap, bot);
            r.Sample(d, 0f, true, 0);
            return r;
        }

        [Test]
        public void StartsJustBehindTheFriendsCurrentPosition()
        {
            var runners = new[] { Runner("x", 300f), Runner("u2", 1000f) };
            Assert.AreEqual(985f, NearFriendStart.Target(runners, "u2", 15f, 6670f, 0).Value, 0.01f);
        }

        [Test]
        public void StartWrapsPastTheStartLine()
        {
            Assert.AreEqual(6665f, NearFriendStart.Target(new[] { Runner("u2", 5f) }, "u2", 10f, 6670f, 0).Value, 0.01f);
        }

        [Test]
        public void NoTargetWhenTheFriendHasLeft()
        {
            Assert.IsNull(NearFriendStart.Target(new[] { Runner("x", 300f) }, "u2", 15f, 6670f, 0), "친구가 방에 없음 → 그 자리에서");
            Assert.IsNull(NearFriendStart.Target(new[] { Runner("u2", 300f, bot: true) }, "u2", 15f, 6670f, 0), "봇은 친구가 아님");
            Assert.IsNull(NearFriendStart.Target(null, "u2", 15f, 6670f, 0), "실시간 방 없음");
            Assert.IsNull(NearFriendStart.Target(new[] { Runner("u2", 300f) }, "u2", 15f, 0f, 0), "섬 코스 길이를 아직 모름");
        }

        [Test]
        public void RunTogetherNoticesNameTheFriend()
        {
            StringAssert.Contains("바닷바람", FriendText.StartBehindFriend("바닷바람"));
            StringAssert.Contains("바닷바람", FriendText.FriendFinished("바닷바람"));
            StringAssert.Contains("바닷바람", FriendText.CannotRunTogether("바닷바람"));
            Assert.AreNotEqual(FriendText.FriendFinished("바닷바람"), FriendText.CannotRunTogether("바닷바람"), "친구가 아니게 된 것과 끝낸 것은 다른 말");
        }
    }
}
