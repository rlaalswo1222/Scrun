using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 로그인·가입 화면의 입력 규칙과 오류 안내(화면 없이 확인하는 부분)
    public class LoginFormTests
    {
        [Test]
        public void 키_몸무게는_서버와_같은_범위만_통과()
        {
            Assert.IsNull(ProfileForm.CheckBody("175", "70", out int h, out int w));
            Assert.AreEqual(175, h);
            Assert.AreEqual(70, w);
            Assert.IsNull(ProfileForm.CheckBody("100", "20", out _, out _), "경계값 포함");
            Assert.IsNull(ProfileForm.CheckBody("230", "250", out _, out _));
            Assert.IsNull(ProfileForm.CheckBody("", "", out _, out _), "아직 안 적음은 오류 아님(버튼만 꺼짐)");
            StringAssert.Contains("키", ProfileForm.CheckBody("99", "70", out _, out _));
            StringAssert.Contains("키", ProfileForm.CheckBody("abc", "70", out _, out _));
            StringAssert.Contains("몸무게", ProfileForm.CheckBody("175", "251", out _, out _));
        }

        [Test]
        public void 닉네임_안내는_서버가_정리한_값과_이유를_알려준다()
        {
            Assert.AreEqual("쓸 수 있어요",
                ProfileForm.DescribeNickname("민재", new NicknameCheck { Nickname = "민재", Available = true }));
            StringAssert.Contains("'AB'",
                ProfileForm.DescribeNickname(" ＡＢ ", new NicknameCheck { Nickname = "AB", Available = true }));
            StringAssert.Contains("이미",
                ProfileForm.DescribeNickname("민재", new NicknameCheck { Nickname = "민재", Available = false, Reason = "DUPLICATED" }));
            StringAssert.Contains("2–12자",
                ProfileForm.DescribeNickname("ㅋㅋ", new NicknameCheck { Nickname = "ㅋㅋ", Available = false, Reason = "INVALID" }));
        }

        [Test]
        public void 다시_시도는_다시_보내면_달라질_수_있을_때만()
        {
            Assert.IsTrue(Feedback.Describe(new ApiException(ApiFailure.Network, "x")).canRetry);
            Assert.IsTrue(Feedback.Describe(new ApiException(ApiFailure.Server, "점검", 503)).canRetry);
            Assert.IsTrue(Feedback.Describe(new ApiException(ApiFailure.BadResponse, "x", 502)).canRetry);
            var dup = Feedback.Describe(new ApiException(ApiFailure.Server, "이미 쓰는 닉네임이에요.", 409, "NICKNAME_DUPLICATED"));
            Assert.IsFalse(dup.canRetry);
            Assert.AreEqual("이미 쓰는 닉네임이에요.", dup.body, "서버 문구를 그대로");
        }
    }
}
