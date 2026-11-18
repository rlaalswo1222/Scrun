using System;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계획 0049(마이 탭 H-01·H-04·H-09·H-10·H-11): 글자, 화면 이름 칸, 프로필 저장 조건, 요청 모양
    public class MyTests
    {
        SynchronizationContext saved;
        FakeHttp http;
        UsersApi users;

        [SetUp]
        public void SetUp()
        {
            saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            http = new FakeHttp();
            var session = new Session(new MemorySecureStore());
            session.SetTokens("a", "r");
            users = new UsersApi(new ApiClient(http, session, () => "http://test"));
        }

        [TearDown]
        public void TearDown() => SynchronizationContext.SetSynchronizationContext(saved);

        static Progress Lv2 => new Progress { Level = 2, XpM = 4420, NextLevelAtM = 10000, NextLevelRemainingM = 5580, OutfitStage = 2, VerifiedCount = 3 };

        // ---- 글자 ----

        [Test]
        public void TotalsRoundDownAndRemainingRoundsUp()
        {
            Assert.AreEqual("4.4km", MyText.Km(4420));
            Assert.AreEqual("2.9km", MyText.Km(2999), "3.0km로 보이면 Lv.2가 된 것처럼 보임");
            Assert.AreEqual("0.0km", MyText.Km(-5));
            Assert.AreEqual("다음 레벨까지 5.6km", MyText.NextLevel(5580), "남은 거리는 올림");
            Assert.AreEqual("다음 레벨까지 0.1km", MyText.NextLevel(50), "50 m 남았는데 0.0km면 이미 된 것처럼 보임");
            Assert.AreEqual("러닝복을 모두 모았어요", MyText.NextLevel(null));
            Assert.AreEqual("누적 4.4km · 다음 레벨까지 5.6km", MyText.LevelLead(Lv2));
        }

        [Test]
        public void LevelRowsFollowTheBacklogThresholds()
        {
            Assert.AreEqual("Lv.1 · 기본 러닝복 (0km)", MyText.LevelRow(1));
            Assert.AreEqual("Lv.2 · 기능성 세트 (3km)", MyText.LevelRow(2));
            Assert.AreEqual("Lv.6 · 엘리트 세트 (100km)", MyText.LevelRow(6));
            Assert.AreEqual("엘리트 세트", MyText.Outfit(9), "Lv.6 뒤는 마지막 세트");
            Assert.AreEqual("인증 마크 3개", MyText.Marks(3));
        }

        [Test]
        public void CalibrationDateInPhoneTimezone()
        {
            var seoul = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
            var at = new DateTime(2026, 10, 31, 16, 30, 0, DateTimeKind.Utc); // 서울 11/01(일) 01:30
            Assert.AreEqual("마지막 보정 11.01", MyText.LastCalibration(at, seoul));
            Assert.AreEqual("11.01 (일)", MyText.DateWithDay(at, seoul));
            Assert.AreEqual("아직 보정하지 않았어요", MyText.LastCalibration(null));
        }

        [Test]
        public void HeadphoneLine()
        {
            Assert.AreEqual("AirPods Pro 연결됨", MyText.Headphones(HeadphoneStatus.Connected, "AirPods Pro"));
            Assert.AreEqual("연결 안 됨", MyText.Headphones(HeadphoneStatus.NotConnected, null));
        }

        // ---- 화면 ----

        [Test]
        public void ScreensHaveEveryNamedElementAndCallNothingOnCreate()
        {
            var me = new Me { UserId = "u1", Nickname = "러너123", HeightCm = 170, WeightKg = 65 };
            AppScreen[] screens = { new MyScreen(), new ProfileEditScreen(me), new LevelScreen(Lv2), new RecalibrateScreen(), new SettingsScreen() };
            foreach (var s in screens) s.Create(null);
            Assert.That(http.Sent, Is.Empty, "서버는 보일 때(OnShow) 부름");
        }

        [Test]
        public void LevelScreenMarksTheCurrentSetAndLocksTheRest()
        {
            var s = new LevelScreen(Lv2);
            s.Create(null);
            Assert.IsTrue(s.Root.Q("level-2").ClassListContains("my-row--current"));
            Assert.IsFalse(s.Root.Q("level-1").ClassListContains("my-row--locked"));
            Assert.IsTrue(s.Root.Q("level-3").ClassListContains("my-row--locked"));
        }

        [Test]
        public void ProfileSaveTurnsOnOnlyWhenSomethingChanged()
        {
            var me = new Me { UserId = "u1", Nickname = "러너123", HeightCm = 170, WeightKg = 65 };
            var s = new ProfileEditScreen(me);
            s.Create(null);
            var submit = s.Root.Q<Button>("submit");
            Assert.IsFalse(submit.enabledSelf, "아무것도 안 바꿈");
            s.Root.Q<TextField>("height").value = "171";
            s.Refresh();
            Assert.IsTrue(submit.enabledSelf, "키만 바꿈(닉네임은 그대로라 확인 안 함)");
            s.Root.Q<TextField>("height").value = "90";
            s.Refresh();
            Assert.IsFalse(submit.enabledSelf, "범위 밖");
            s.Root.Q<TextField>("height").value = "170";
            s.Root.Q<TextField>("nickname").value = "새러너";
            s.Refresh();
            Assert.IsFalse(submit.enabledSelf, "새 닉네임은 서버 확인 전엔 저장 못 함");
        }

        [Test]
        public void EmptyBodyFieldSaysWhySaveIsOff()
        {
            Assert.IsNull(ProfileForm.BodyMessage("", "65", show: false), "칸을 벗어나기 전엔 조용히");
            Assert.AreEqual("키와 몸무게를 모두 적어 주세요", ProfileForm.BodyMessage("", "65", show: true));
            StringAssert.StartsWith("키는", ProfileForm.BodyMessage("90", "65", show: true));
            Assert.IsNull(ProfileForm.BodyMessage("170", "65", show: true));
            Assert.IsFalse(ProfileForm.BodyFilled("", "65"));
        }

        [Test]
        public void FeedSwitchIsLockedUntilSettingsLoad()
        {
            var s = new SettingsScreen();
            s.Create(null);
            var sw = s.Root.Q("feed-share").Q("switch");
            Assert.IsTrue(sw.ClassListContains("my-switch--locked"), "서버 값을 받기 전엔 켜짐·꺼짐을 보이지 않음");
            Assert.IsFalse(sw.ClassListContains("my-switch--on"));
        }

        // ---- 요청 모양 ----

        [Test]
        public void UpdateMeSendsOnlyChangedFields()
        {
            http.Handler = _ => new HttpResponse
            {
                Status = 200,
                Body = "{\"resultType\":\"SUCCESS\",\"success\":{\"data\":{\"userId\":\"u1\",\"nickname\":\"러너123\",\"heightCm\":171,\"weightKg\":65}},\"error\":null,\"meta\":{}}"
            };
            var t = users.UpdateMeAsync(new MeUpdate { HeightCm = 171 });
            Assert.IsTrue(t.IsCompleted);
            var me = t.GetAwaiter().GetResult();
            Assert.AreEqual("PATCH", http.Sent[0].Method);
            StringAssert.EndsWith("/v1/users/me", http.Sent[0].Url);
            Assert.That(http.Sent[0].Body, Does.Contain("\"heightCm\":171"));
            Assert.That(http.Sent[0].Body, Does.Not.Contain("nickname"));
            Assert.That(http.Sent[0].Body, Does.Not.Contain("weightKg"));
            Assert.AreEqual(171, me.HeightCm);
        }

        [Test]
        public void SettingsAndProgressRequests()
        {
            http.Handler = r => new HttpResponse
            {
                Status = 200,
                Body = r.Url.EndsWith("/progress")
                    ? "{\"resultType\":\"SUCCESS\",\"success\":{\"data\":{\"level\":2,\"xpM\":4420,\"nextLevelAtM\":10000,\"nextLevelRemainingM\":5580,\"outfitStage\":2,\"verifiedCount\":3,\"impacts\":[],\"selectedImpactId\":null}},\"error\":null,\"meta\":{}}"
                    : "{\"resultType\":\"SUCCESS\",\"success\":{\"data\":{\"feedShareDefault\":false}},\"error\":null,\"meta\":{}}"
            };
            var s = users.UpdateSettingsAsync(false).GetAwaiter().GetResult();
            Assert.AreEqual("PATCH", http.Sent[0].Method);
            Assert.That(http.Sent[0].Body, Does.Contain("\"feedShareDefault\":false"));
            Assert.IsFalse(s.FeedShareDefault);
            var p = users.ProgressAsync().GetAwaiter().GetResult();
            Assert.AreEqual((2, 4420, 5580, 3), (p.Level, p.XpM, p.NextLevelRemainingM.Value, p.VerifiedCount));
        }
    }
}
