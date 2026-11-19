using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 계기판 인증 화면·사진 올리기 조각(계획 0038)
    public class VerifyScreenTests
    {
        [Test]
        public void Uxml_has_every_named_element_and_opens_on_guide()
        {
            var s = new VerifyScreen("run_1", new RunStatsBody { DistanceM = 3000, DurationS = 1000 }, null);
            s.Create(null); // UXML에 이름이 없으면 Q가 바로 예외
            bool Shown(string n) => s.Root.Q(n).style.display != DisplayStyle.None;
            Assert.IsTrue(Shown("guide"));
            foreach (var n in new[] { "leave", "confirm", "sending", "read", "result" }) Assert.IsFalse(Shown(n), n);
            // E-05b(계획 0045) 모양: 지환이 연결할 이름들
            foreach (var n in new[] { "read-photo", "read-distance", "read-time", "read-error", "read-yes", "read-no" }) Assert.IsNotNull(s.Root.Q(n), n);
            Assert.AreEqual("계기판 인증", s.Root.Q<Label>("nav-title").text);
        }

        // 계획 0055: 서버 OCR 시간 초과(503 details.reason)만 Timeout, 일꾼이 다 찬 503은 그대로 바빠요
        [Test]
        public void Ocr_timeout_is_told_apart_from_busy()
        {
            ApiException E(string details) => new ApiException(ApiFailure.Server, "x", 503, "SERVICE_UNAVAILABLE", null, details);
            Assert.IsTrue(VerifyScreen.IsOcrTimeout(E("{\"reason\":\"OCR_TIMEOUT\"}")));
            Assert.IsFalse(VerifyScreen.IsOcrTimeout(E(null)));
            Assert.IsFalse(VerifyScreen.IsOcrTimeout(E("{\"reason\":\"OTHER\"}")));
        }

        [Test]
        public void Photos_go_zero_ev_first()
        {
            CameraPhoto P(float ev, string path) => new CameraPhoto { ExposureBias = ev, Path = path };
            var order = VerifyPhotos.SendOrder(new[] { P(-3f, "a"), P(0f, "b"), P(-1.5f, "c"), P(0f, "d") });
            CollectionAssert.AreEqual(new[] { "b", "d", "c", "a" }, order.Select(p => p.Path).ToArray());
        }

        [TestCase(3000, "3.00km")]
        [TestCase(3120, "3.12km")]
        [TestCase(520, "0.52km")]
        [TestCase(3129, "3.12km")] // HUD·결과와 같게 10 m 아래 버림
        public void Km(int m, string text) => Assert.AreEqual(text, VerifyScreen.FormatKm(m));

        [Test]
        public void Km_uses_dot_in_comma_locale()
        {
            var saved = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try { Assert.AreEqual("3.12km", VerifyScreen.FormatKm(3120)); }
            finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
        }

        [Test]
        public void Jpeg_size_is_read_from_header_and_small_photos_pass_untouched()
        {
            var tex = new UnityEngine.Texture2D(64, 48, UnityEngine.TextureFormat.RGB24, false);
            byte[] jpg;
            try { jpg = UnityEngine.ImageConversion.EncodeToJPG(tex, 80); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            Assert.AreEqual((64, 48), VerifyPhotos.JpegSize(jpg));
            Assert.AreSame(jpg, VerifyPhotos.Shrink(jpg)); // 긴 변 2048 이하 → 디코딩·재압축 없이 그대로
            Assert.IsNull(VerifyPhotos.JpegSize(new byte[] { 1, 2, 3, 4 }));
            Assert.IsNull(VerifyPhotos.JpegSize(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));
        }

        [Test]
        public void Large_photo_is_shrunk_to_max_side()
        {
            var tex = new UnityEngine.Texture2D(3000, 1500, UnityEngine.TextureFormat.RGB24, false);
            byte[] jpg;
            try { jpg = UnityEngine.ImageConversion.EncodeToJPG(tex, 50); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            Assert.AreEqual((VerifyPhotos.MaxSide, VerifyPhotos.MaxSide / 2), VerifyPhotos.JpegSize(VerifyPhotos.Shrink(jpg)));
        }

        [TestCase(1720, "28:40")]
        [TestCase(59, "0:59")]
        [TestCase(3725, "1:02:05")]
        public void Time(int s, string text) => Assert.AreEqual(text, VerifyScreen.FormatTime(s));

        [Test]
        public void File_names_carry_fake_ocr_only_in_editor_choice()
        {
            var sensor = new RunStatsBody { DistanceM = 3000, DurationS = 1000 };
            Assert.AreEqual("console_0_ev0.0.jpg", VerifyPhotos.FileName(0, 0f, sensor, null));
            Assert.AreEqual("console_2_ev-3.0.jpg", VerifyPhotos.FileName(2, -3f, sensor, FakeOcrResult.Match)); // 첫 장만
            Assert.AreEqual("console_0_ev0.0_d3000_t1040.jpg", VerifyPhotos.FileName(0, 0f, sensor, FakeOcrResult.Match));
            Assert.AreEqual("console_0_ev0.0_d3900_t1040.jpg", VerifyPhotos.FileName(0, 0f, sensor, FakeOcrResult.Mismatch));
            Assert.AreEqual("console_0_ev0.0_blur.jpg", VerifyPhotos.FileName(0, 0f, sensor, FakeOcrResult.Unreadable));
            Assert.AreEqual("console_0_ev0.0_unsupported.jpg", VerifyPhotos.FileName(0, 0f, sensor, FakeOcrResult.Unsupported));
        }

        [Test]
        public void Multipart_repeats_field_with_each_file()
        {
            var body = MultipartBody.Files("photos", new List<MultipartBody.FilePart>
            {
                new MultipartBody.FilePart("a.jpg", new byte[] { 0xFF, 0xD8, 0xFF }),
                new MultipartBody.FilePart("b\"\r\n.jpg", new byte[] { 1, 2 }),
            }, "B");
            Assert.AreEqual("multipart/form-data; boundary=B", body.ContentType);
            string text = Encoding.GetEncoding("ISO-8859-1").GetString(body.Bytes);
            Assert.AreEqual(2, text.Split("name=\"photos\"").Length - 1);
            StringAssert.Contains("filename=\"a.jpg\"", text);
            StringAssert.Contains("filename=\"b.jpg\"", text); // 따옴표·줄바꿈은 빠짐
            StringAssert.EndsWith("--B--\r\n", text);
            // 사진 바이트가 그대로 들어감
            var bytes = body.Bytes.ToList();
            int at = text.IndexOf("\r\n\r\n") + 4;
            Assert.AreEqual(new byte[] { 0xFF, 0xD8, 0xFF }, bytes.GetRange(at, 3).ToArray());
        }

        [Test]
        public void Confirm_read_posts_attempt_and_answer()
        {
            var http = new FakeHttp();
            http.Handler = r => FakeHttp.Ok("{\"status\":\"RETAKE_REQUIRED\",\"reason\":\"USER_REJECTED\",\"retakesLeft\":3," +
                                            "\"console\":null,\"sensor\":{\"distanceM\":3000,\"durationS\":1000},\"comparison\":[]," +
                                            "\"retakeDeadline\":\"2026-11-17T01:10:00Z\",\"attemptId\":null}");
            var session = new Session(new MemorySecureStore());
            session.SetTokens("acc", "ref");
            var api = new RunsApi(new ApiClient(http, session, () => "http://x"));
            var saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            VerificationResult r;
            try
            {
                r = api.ConfirmReadAsync("run_1", "att_9", false).GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(saved);
            }
            var sent = http.Sent.Single();
            Assert.AreEqual("http://x/v1/runs/run_1/verification/confirm", sent.Url);
            Assert.AreEqual("POST", sent.Method);
            StringAssert.Contains("\"attemptId\":\"att_9\"", sent.Body);
            StringAssert.Contains("\"correct\":false", sent.Body);
            Assert.AreEqual("Bearer acc", sent.Headers["Authorization"]);
            Assert.AreEqual((VerifyStatus.Retake, VerifyReason.UserRejected, 3), (r.Status, r.Reason, r.RetakesLeft.Value));
        }

        [Test]
        public void Photo_upload_is_multipart_with_auth_and_parses_retake()
        {
            var http = new FakeHttp();
            http.Handler = r => FakeHttp.Ok("{\"status\":\"RETAKE_REQUIRED\",\"reason\":\"MISMATCH\",\"retakesLeft\":2," +
                                            "\"console\":{\"distanceM\":3600,\"durationS\":1000},\"sensor\":{\"distanceM\":3000,\"durationS\":1000}," +
                                            "\"comparison\":[{\"item\":\"distance\",\"console\":3600,\"sensor\":3000,\"diffRatio\":0.1667,\"ok\":false}]," +
                                            "\"retakeDeadline\":\"2026-11-17T01:10:00Z\"}");
            var session = new Session(new MemorySecureStore());
            session.SetTokens("acc", "ref");
            var api = new RunsApi(new ApiClient(http, session, () => "http://x"));
            var saved = SynchronizationContext.Current; // await 뒤가 같은 줄에서 이어지게(ApiClientTests와 같게)
            SynchronizationContext.SetSynchronizationContext(null);
            VerificationResult r;
            try
            {
                r = api.VerifyPhotoAsync("run_1", new List<MultipartBody.FilePart> { new MultipartBody.FilePart("a.jpg", new byte[] { 0xFF, 0xD8 }) })
                       .GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(saved);
            }
            var sent = http.Sent.Single();
            Assert.AreEqual("http://x/v1/runs/run_1/verification/photo", sent.Url);
            StringAssert.StartsWith("multipart/form-data; boundary=", sent.ContentType);
            Assert.IsNull(sent.Body);
            Assert.AreEqual("Bearer acc", sent.Headers["Authorization"]);
            Assert.AreEqual(RunsApi.PhotoUploadTimeoutSeconds, sent.TimeoutSeconds);
            Assert.AreEqual(("RETAKE_REQUIRED", "MISMATCH", 2, 3600), (r.Status, r.Reason, r.RetakesLeft.Value, r.Console.DistanceM));
            Assert.IsFalse(r.Comparison[0].Ok);
            Assert.IsTrue(r.RetakeDeadline.HasValue);
        }
    }
}
