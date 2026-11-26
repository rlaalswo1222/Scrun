using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 503 details.reason 읽기와 공통 안내 문구(계획 0077). 인증 화면 문구는 VerifyScreenTests
    public class ApiReasonTests
    {
        internal static ApiException E503(string details) => // 서버가 보내는 503 하나(VerifyScreenTests도 씀)
            new ApiException(ApiFailure.Server, "서버를 잠시 쓸 수 없습니다.", 503, "SERVICE_UNAVAILABLE", "req_1", details);

        [TestCase("{\"reason\":\"BUSY\"}", "BUSY")]
        [TestCase("{\"reason\":\"OCR_TIMEOUT\",\"x\":1}", "OCR_TIMEOUT")]
        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("{}", null)]
        [TestCase("{\"reason\":null}", null)]
        [TestCase("{\"reason\":3}", null)]       // 글자가 아니면 없음으로
        [TestCase("[\"BUSY\"]", null)]           // 객체가 아님
        [TestCase("{\"reason\":", null)]         // 깨진 JSON
        public void Reason_is_read_from_details(string details, string expected) =>
            Assert.AreEqual(expected, E503(details).Reason);

        [Test]
        public void Crowded_is_only_server_503_with_busy_or_db_busy()
        {
            Assert.IsTrue(E503("{\"reason\":\"BUSY\"}").IsServerCrowded);
            Assert.IsTrue(E503("{\"reason\":\"DB_BUSY\"}").IsServerCrowded);
            Assert.IsFalse(E503("{\"reason\":\"OCR_TIMEOUT\"}").IsServerCrowded);
            Assert.IsFalse(E503("{\"reason\":\"REALTIME_BUSY\"}").IsServerCrowded); // 맵 입장은 서버 문구가 더 정확
            Assert.IsFalse(E503(null).IsServerCrowded);
            Assert.IsFalse(new ApiException(ApiFailure.Server, "x", 500, "INTERNAL_ERROR", null, "{\"reason\":\"BUSY\"}").IsServerCrowded);
        }

        [Test]
        public void Common_error_says_crowded_instead_of_server_default()
        {
            var crowded = Feedback.Describe(E503("{\"reason\":\"DB_BUSY\"}"));
            Assert.AreEqual(Feedback.CrowdedText, crowded.body);
            Assert.IsTrue(crowded.canRetry);
            // 다른 503은 서버 문구 그대로(맵 입장 REALTIME_BUSY 등)
            var other = Feedback.Describe(new ApiException(ApiFailure.Server, "맵 인원을 확인하지 못했습니다.", 503,
                                                           "SERVICE_UNAVAILABLE", null, "{\"reason\":\"REALTIME_BUSY\"}"));
            Assert.AreEqual("맵 인원을 확인하지 못했습니다.", other.body);
            Assert.IsTrue(other.canRetry);
        }
    }
}
