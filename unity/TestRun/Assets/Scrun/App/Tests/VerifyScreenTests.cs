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
            foreach (var n in new[] { "leave", "confirm", "sending", "result" }) Assert.IsFalse(Shown(n), n);
            Assert.AreEqual("계기판 인증", s.Root.Q<Label>("nav-title").text);
        }

        [TestCase(3000, "3.00km")]
        [TestCase(3120, "3.12km")]
        [TestCase(520, "0.52km")]
        public void Km(int m, string text) => Assert.AreEqual(text, VerifyScreen.FormatKm(m));

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
