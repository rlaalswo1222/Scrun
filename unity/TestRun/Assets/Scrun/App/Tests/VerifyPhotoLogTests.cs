using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 개발 빌드 인증 사진 남기기(계획 0085): 보낸 바이트 그대로·meta.json·출시 빌드는 안 씀·실패해도 인증은 계속
    public class VerifyPhotoLogTests
    {
        string root;
        static readonly DateTime T0 = new DateTime(2026, 11, 27, 1, 41, 37, DateTimeKind.Utc);

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "scrun-verify-log-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        static List<MultipartBody.FilePart> Parts() => new List<MultipartBody.FilePart>
        {
            new MultipartBody.FilePart("console_0_ev0.0.jpg", new byte[] { 1, 2, 3 }),
            new MultipartBody.FilePart("console_1_ev-1.5.jpg", new byte[] { 4, 5 }),
        };

        static readonly List<float> Evs = new List<float> { 0f, -1.5f };   // Parts()와 같은 순서(SendOrder)

        static string Meta(VerifyPhotoLog log) => File.ReadAllText(Path.Combine(log.Dir, "meta.json")); // 들여쓴 JSON("키": 값)

        [Test]
        public void SavesSentBytesAndMeta()
        {
            var log = VerifyPhotoLog.Begin("01M7SC12ABCDEF", Parts(), Evs, new RunStatsBody { DistanceM = 141, DurationS = 66 },
                                           T0, root, enabled: true);
            Assert.NotNull(log);
            Assert.AreEqual("20261127-014137_01M7SC12", Path.GetFileName(log.Dir));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(log.Dir, "console_0_ev0.0.jpg")));
            CollectionAssert.AreEqual(new byte[] { 4, 5 }, File.ReadAllBytes(Path.Combine(log.Dir, "console_1_ev-1.5.jpg")));
            var m = Meta(log);
            StringAssert.Contains("\"distance_m\": 141", m);
            StringAssert.Contains("\"file\": \"console_1_ev-1.5.jpg\"", m);
            StringAssert.Contains("\"bytes\": 3", m);
            StringAssert.Contains("\"ev\": -1.5", m);
            StringAssert.Contains("\"confirmed\": null", m);
        }

        [Test]
        public void RecordsAnswersAndConfirmation()
        {
            var log = VerifyPhotoLog.Begin("run1", Parts(), Evs, null, T0, root, enabled: true);
            log.Answer(new VerificationResult
            {
                Status = "CONFIRM_REQUIRED",
                Console = new RunStatsBody { DistanceM = 130, DurationS = 60 },
                AttemptId = "a1",
            }, T0.AddSeconds(4));
            log.Confirm(true);
            log.Answer(new VerificationResult { Status = "VERIFIED" }, T0.AddSeconds(6));
            var m = Meta(log);
            StringAssert.Contains("\"confirmed\": true", m);            // "맞아요" → 읽은 130 m·60초가 정답
            StringAssert.Contains("\"distance_m\": 130", m);
            StringAssert.Contains("\"status\": \"CONFIRM_REQUIRED\"", m);
            StringAssert.Contains("\"status\": \"VERIFIED\"", m);
            StringAssert.Contains("\"sensor\": null", m);
        }

        [Test]
        public void RecordsSendFailure()
        {
            var log = VerifyPhotoLog.Begin("run1", Parts(), Evs, null, T0, root, enabled: true);
            log.Failed("Unavailable: OCR_TIMEOUT", T0.AddSeconds(15));
            StringAssert.Contains("\"error\": \"Unavailable: OCR_TIMEOUT\"", Meta(log));
        }

        [Test]
        public void WritesNothingWhenNotEnabled()   // 출시 빌드·에디터
        {
            Assert.IsNull(VerifyPhotoLog.Begin("run1", Parts(), Evs, null, T0, root, enabled: false));
            Assert.IsFalse(Directory.Exists(root));
            Assert.IsNull(VerifyPhotoLog.Begin("run1", new List<MultipartBody.FilePart>(), Evs, null, T0, root, enabled: true));
        }

        [Test]
        public void SecondSendInSameSecondGetsOwnFolder()
        {
            var a = VerifyPhotoLog.Begin("run1", Parts(), Evs, null, T0, root, enabled: true);
            var b = VerifyPhotoLog.Begin("run1", Parts(), Evs, null, T0, root, enabled: true);
            Assert.AreNotEqual(a.Dir, b.Dir);
        }

        [Test]
        public void SaveFailureDoesNotThrow()   // 저장 실패는 인증을 막지 않음
        {
            Directory.CreateDirectory(Path.GetDirectoryName(root));
            File.WriteAllText(root, "파일이라 폴더를 못 만듦");
            try
            {
                Assert.IsNull(VerifyPhotoLog.Begin("run1", Parts(), Evs, null, T0, root, enabled: true));
            }
            finally
            {
                File.Delete(root);
            }
        }
    }
}
