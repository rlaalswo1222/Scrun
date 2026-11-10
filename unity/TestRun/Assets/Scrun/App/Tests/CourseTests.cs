using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 코스 화면(계획 0022): 서버 응답 읽기, 코스 ID → 섬 도로망 코스 ID, 문구
    public class CourseTests
    {

        SynchronizationContext savedContext;
        FakeHttp http;
        CoursesApi courses;

        [SetUp]
        public void SetUp()
        {
            savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            http = new FakeHttp();
            var session = new Session(new MemorySecureStore());
            session.SetTokens("access", "refresh");
            courses = new CoursesApi(new ApiClient(http, session, () => "http://test"));
        }

        [TearDown]
        public void TearDown() => SynchronizationContext.SetSynchronizationContext(savedContext);

        static HttpResponse Ok(string data) => new HttpResponse
        {
            Status = 200,
            Body = "{\"resultType\":\"SUCCESS\",\"success\":{\"data\":" + data + "},\"error\":null,\"meta\":{\"requestId\":\"req_ok\"}}"
        };

        static HttpResponse Fail(int status, string code) => new HttpResponse
        {
            Status = status,
            Body = "{\"resultType\":\"FAIL\",\"success\":null,\"error\":{\"code\":\"" + code +
                   "\",\"message\":\"m\",\"details\":null},\"meta\":{\"requestId\":\"req_fail\"}}"
        };

        static T Run<T>(Task<T> t)
        {
            Assert.IsTrue(t.IsCompleted, "가짜 응답이면 바로 끝나야 함");
            return t.GetAwaiter().GetResult();
        }

        // ---------- 서버 응답(docs/api/examples의 모양) ----------
        [Test]
        public void ListReadsServerCourses()
        {
            http.Handler = _ => Ok("[{\"courseId\":\"crs_lake_loop\",\"name\":\"노을 호수 한 바퀴\",\"type\":\"LOOP_3D\"," +
                                   "\"lengthM\":6671,\"runnersNow\":3,\"thumbnailUrl\":null}]");
            var list = Run(courses.ListAsync());
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("crs_lake_loop", list[0].CourseId);
            Assert.AreEqual(6671, list[0].LengthM);
            Assert.AreEqual(3, list[0].RunnersNow);
            Assert.IsNull(list[0].ThumbnailUrl);
            Assert.AreEqual("GET", http.Sent[0].Method);
            StringAssert.EndsWith("/v1/courses", http.Sent[0].Url);
            Assert.AreEqual("Bearer access", http.Sent[0].Headers["Authorization"]);
        }

        [Test]
        public void DetailReadsExtraFields()
        {
            http.Handler = _ => Ok("{\"courseId\":\"crs_coast_run\",\"name\":\"해안 절벽 순환\",\"type\":\"LOOP_3D\",\"lengthM\":8776," +
                                   "\"runnersNow\":0,\"thumbnailUrl\":null,\"description\":\"d\",\"mood\":\"m\",\"maxRunnersPerMap\":20}");
            var c = Run(courses.GetAsync("crs_coast_run"));
            Assert.AreEqual(20, c.MaxRunnersPerMap);
            Assert.AreEqual("m", c.Mood);
            StringAssert.EndsWith("/v1/courses/crs_coast_run", http.Sent[0].Url);
        }

        [Test]
        public void EnterPostsAndReadsEntry()
        {
            http.Handler = _ => new HttpResponse
            {
                Status = 201,
                Body = "{\"resultType\":\"SUCCESS\",\"success\":{\"data\":{\"mapInstanceId\":\"map_crs_lake_loop\"," +
                       "\"courseId\":\"crs_lake_loop\",\"entryPositionM\":1574.3,\"realtime\":null}},\"error\":null,\"meta\":{}}"
            };
            var e = Run(courses.EnterAsync("crs_lake_loop"));
            Assert.AreEqual("POST", http.Sent[0].Method);
            StringAssert.EndsWith("/v1/courses/crs_lake_loop/entries", http.Sent[0].Url);
            Assert.AreEqual("map_crs_lake_loop", e.MapInstanceId);
            Assert.AreEqual(1574.3f, e.EntryPositionM, 0.01f);
            Assert.IsNull(e.Realtime);
        }

        [TestCase(409, "COURSE_FULL")]
        [TestCase(409, "CALIBRATION_REQUIRED")]
        [TestCase(404, "COURSE_NOT_FOUND")]
        public void EnterFailureKeepsCode(int status, string code)
        {
            http.Handler = _ => Fail(status, code);
            var ex = Assert.Throws<ApiException>(() => Run(courses.EnterAsync("crs_lake_loop")));
            Assert.AreEqual(ApiFailure.Server, ex.Failure);
            Assert.AreEqual(code, ex.Code);
            Assert.IsNotNull(CourseText.Failure(code), "화면이 직접 안내하는 실패");
        }

        // ---------- 앱 → 섬 씬 ----------
        [TestCase("crs_lake_loop", "lake_loop")]
        [TestCase("crs_island_tour", "island_tour")]
        public void RouteIdDropsPrefix(string courseId, string routeId) =>
            Assert.AreEqual(routeId, MapLaunch.RouteIdOf(courseId));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("lake_loop")]
        [TestCase("crs_")]
        public void RouteIdRejectsOtherShapes(string courseId) =>
            Assert.Throws<ArgumentException>(() => MapLaunch.RouteIdOf(courseId));

        [Test]
        public void LaunchIsTakenOnce()
        {
            MapLaunch.Store(new MapEntry { CourseId = "crs_coast_run", EntryPositionM = 120.5f });
            Assert.IsTrue(MapLaunch.HasPending);
            var r = MapLaunch.Take();
            Assert.AreEqual("coast_run", r.RouteId);
            Assert.AreEqual(120.5f, r.EntryPositionM, 0.001f);
            Assert.IsNull(MapLaunch.Take(), "두 번 적용되지 않게");
        }

        [Test]
        public void ClearDropsPending()
        {
            MapLaunch.Store(new MapEntry { CourseId = "crs_lake_loop", EntryPositionM = 1f });
            MapLaunch.Clear();
            Assert.IsFalse(MapLaunch.HasPending);
        }

        // ---------- 문구 ----------
        [TestCase(6671, "6.7km")]
        [TestCase(10884, "10.9km")]
        [TestCase(3000, "3.0km")]
        [TestCase(1050, "1.1km")] // 0.05는 올림(float 나눗셈이면 1.0)
        [TestCase(1049, "1.0km")]
        public void KmHasOneDecimal(int m, string text) => Assert.AreEqual(text, CourseText.Km(m));

        [TestCase("노을 호수 한 바퀴", "노")]
        [TestCase("🏝️ 섬 일주", "🏝️")] // 이모지(서로게이트 쌍 + 변형 선택자)를 반쪽만 자르지 않음
        [TestCase("", "")]
        [TestCase(null, "")]
        public void InitialIsOneTextElement(string name, string expected) => Assert.AreEqual(expected, CourseText.Initial(name));

        [Test]
        public void RunnersText()
        {
            Assert.AreEqual("지금 달리는 사람 없음", CourseText.RunningNow(0));
            Assert.AreEqual("지금 3명 달리는 중", CourseText.RunningNow(3));
        }

        [Test]
        public void OtherFailuresUseCommonNotice() => Assert.IsNull(CourseText.Failure("RATE_LIMITED"));

        [Test]
        public void LapAndMapLabel()
        {
            Assert.AreEqual("한 바퀴 6.7km", CourseText.Lap(6671));
            Assert.AreEqual("한 바퀴 3.0km · 호수 한 바퀴", CourseText.MapLabel(3000, "호수 한 바퀴"));
            Assert.AreEqual("0명", CourseText.PeopleCount(-1));
        }

        // ---------- 목록 거르기(C-01 칩) ----------
        [Test]
        public void FilterDropsVideoAndKeepsOrder()
        {
            var all = new List<Course>
            {
                new Course { CourseId = "crs_a", Type = "LOOP_3D", RunnersNow = 0 },
                new Course { CourseId = "crs_v", Type = CourseText.VideoType, RunnersNow = 5 },
                null,
                new Course { CourseId = "crs_b", Type = "LOOP_3D", RunnersNow = 2 },
            };
            CollectionAssert.AreEqual(new[] { "crs_a", "crs_b" },
                                      CourseListScreen.Filter(all, false).ConvertAll(c => c.CourseId));
            CollectionAssert.AreEqual(new[] { "crs_b" },
                                      CourseListScreen.Filter(all, true).ConvertAll(c => c.CourseId));
            Assert.IsEmpty(CourseListScreen.Filter(null, false));
        }
    }
}
