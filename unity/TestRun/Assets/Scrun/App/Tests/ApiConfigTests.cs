using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 에디터 서버 주소 정리(ApiConfig.Normalize): 한글 이름 컴퓨터에서 localhost로는 실시간 연결이 안 붙어 127.0.0.1로 바꾼다
    public class ApiConfigTests
    {
        [TestCase("http://localhost:8000", "http://127.0.0.1:8000")]
        [TestCase("http://LocalHost:8001/", "http://127.0.0.1:8001")]
        [TestCase("  http://localhost:8001  ", "http://127.0.0.1:8001")]
        [TestCase("http://localhost:8000/api", "http://127.0.0.1:8000/api")]
        [TestCase("http://localhost:8000/?debug=1", "http://127.0.0.1:8000/?debug=1")]
        [TestCase("http://127.0.0.1:8001", "http://127.0.0.1:8001")]
        [TestCase("https://161-118-211-155.sslip.io/", "https://161-118-211-155.sslip.io")]
        [TestCase("http://192.168.0.10:8000", "http://192.168.0.10:8000")]
        public void Normalize(string input, string expected) => Assert.AreEqual(expected, ApiConfig.Normalize(input));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void EmptyIsLocalServer(string input) => Assert.AreEqual(ApiConfig.LocalServer, ApiConfig.Normalize(input));

        [Test]
        public void LocalServerIsNotLocalhost() => StringAssert.DoesNotContain("localhost", ApiConfig.LocalServer);
    }
}
