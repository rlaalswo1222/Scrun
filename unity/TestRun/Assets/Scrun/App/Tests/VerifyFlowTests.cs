using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 계기판 인증 흐름(계획 0038, Figma E-02–E-10·I-06). 카메라·서버·시계는 결과만 넣는다
    public class VerifyFlowTests
    {
        static readonly DateTime T0 = new DateTime(2026, 11, 17, 1, 0, 0, DateTimeKind.Utc);

        static VerificationResult R(string status, string reason = null, int? left = null, int deadlineSec = 600) => new VerificationResult
        {
            Status = status, Reason = reason, RetakesLeft = left,
            RetakeDeadline = status == VerifyStatus.Retake ? T0.AddSeconds(deadlineSec) : (DateTime?)null,
            Sensor = new RunStatsBody { DistanceM = 3000, DurationS = 1000 },
            Comparison = new List<ComparisonRow>(),
        };

        static VerifyFlow AtConfirm()
        {
            var f = new VerifyFlow();
            Assert.IsTrue(f.OpenCamera(T0));
            f.CameraDone(CameraOutcome.Taken);
            Assert.AreEqual(VerifyStep.Confirm, f.Step);
            return f;
        }

        static VerifyFlow Answered(VerificationResult r)
        {
            var f = AtConfirm();
            Assert.IsTrue(f.Send());
            f.Answer(r);
            return f;
        }

        [Test]
        public void Guide_Camera_Confirm_Send_Verified_Done()
        {
            var f = new VerifyFlow();
            Assert.AreEqual(VerifyStep.Guide, f.Step);
            Assert.IsTrue(f.OpenCamera(T0));
            Assert.AreEqual(VerifyStep.Camera, f.Step);
            f.CameraDone(CameraOutcome.Taken);
            Assert.IsTrue(f.HasPhoto);
            Assert.IsTrue(f.Send());
            Assert.AreEqual(VerifyStep.Sending, f.Step);
            f.Answer(R(VerifyStatus.Verified));
            Assert.AreEqual(VerifyStep.Verified, f.Step);
            Assert.IsFalse(f.HasPhoto);
            f.Finish();
            Assert.AreEqual(VerifyStep.Done, f.Step);
        }

        [TestCase(null, VerifyStep.Unreadable)]
        [TestCase("MISMATCH", VerifyStep.Mismatch)]
        [TestCase("UNSUPPORTED_CONSOLE", VerifyStep.Unsupported)]
        public void Retake_answers_go_to_their_screen_and_can_retake(string reason, VerifyStep expected)
        {
            var f = Answered(R(VerifyStatus.Retake, reason, left: 2));
            Assert.AreEqual(expected, f.Step);
            Assert.IsTrue(f.CanRetake);
            Assert.AreEqual(600, f.SecondsLeft(T0));
            Assert.IsTrue(f.OpenCamera(T0.AddSeconds(30)));
            f.CameraDone(CameraOutcome.Cancelled); // 다시 찍기에서 X → 그 결과 화면으로
            Assert.AreEqual(expected, f.Step);
        }

        [TestCase("MISMATCH", VerifyStep.Mismatch)]
        [TestCase("UNSUPPORTED_CONSOLE", VerifyStep.Unsupported)]
        public void Give_up_saves_retake_given_up_then_done(string reason, VerifyStep screen)
        {
            var f = Answered(R(VerifyStatus.Retake, reason, left: 1));
            Assert.IsTrue(f.SaveUnverified());
            Assert.AreEqual("RETAKE_GIVEN_UP", f.SavingReason);
            Assert.AreEqual(VerifyStep.Saving, f.Step);
            f.Answer(R(VerifyStatus.Unverified, reason));
            Assert.AreEqual(VerifyStep.Done, f.Step);
        }

        [Test]
        public void Retakes_used_up_is_already_saved_no_retake()
        {
            var f = Answered(R(VerifyStatus.Unverified, "MISMATCH"));
            Assert.AreEqual(VerifyStep.Mismatch, f.Step);
            Assert.IsFalse(f.CanRetake);
            Assert.IsFalse(f.OpenCamera(T0));
            Assert.IsFalse(f.SaveUnverified()); // 보낼 것 없이 끝
            Assert.AreEqual(VerifyStep.Done, f.Step);
        }

        [Test]
        public void Camera_time_over_goes_E10_then_saves_timeout()
        {
            var f = new VerifyFlow();
            f.OpenCamera(T0);
            f.CameraDone(CameraOutcome.TimedOut);
            Assert.AreEqual(VerifyStep.TimeOver, f.Step);
            Assert.IsTrue(f.SaveUnverified());
            Assert.AreEqual("TIMEOUT", f.SavingReason);
            f.Answer(R(VerifyStatus.Unverified, "TIMEOUT"));
            Assert.AreEqual(VerifyStep.Done, f.Step); // E-10으로 다시 돌아가지 않음
        }

        [Test]
        public void Deadline_passes_on_retake_screen_goes_E10()
        {
            var f = Answered(R(VerifyStatus.Retake, null, left: 3, deadlineSec: 60));
            f.Tick(T0.AddSeconds(59));
            Assert.AreEqual(VerifyStep.Unreadable, f.Step);
            f.Tick(T0.AddSeconds(60));
            Assert.AreEqual(VerifyStep.TimeOver, f.Step);
            Assert.IsTrue(f.SaveUnverified());
            Assert.AreEqual("TIMEOUT", f.SavingReason);
        }

        [Test]
        public void Retake_after_deadline_goes_E10_instead_of_camera()
        {
            var f = Answered(R(VerifyStatus.Retake, "MISMATCH", left: 2, deadlineSec: 10));
            Assert.IsFalse(f.OpenCamera(T0.AddSeconds(11)));
            Assert.AreEqual(VerifyStep.TimeOver, f.Step);
        }

        [Test]
        public void Server_closed_goes_E10()
        {
            var f = AtConfirm();
            f.Send();
            f.Failed(VerifyError.Closed);
            Assert.AreEqual(VerifyStep.TimeOver, f.Step);
            Assert.IsFalse(f.HasPhoto);
        }

        [TestCase(VerifyError.Network)]
        [TestCase(VerifyError.Busy)]
        [TestCase(VerifyError.Other)]
        public void Send_failure_keeps_photo_on_E04(VerifyError e)
        {
            var f = AtConfirm();
            f.Send();
            f.Failed(e);
            Assert.AreEqual(VerifyStep.Confirm, f.Step);
            Assert.IsTrue(f.HasPhoto);
            Assert.AreEqual(e, f.Error);
            Assert.IsTrue(f.Send()); // 같은 사진 다시 보내기
            Assert.AreEqual(VerifyError.None, f.Error);
        }

        [Test]
        public void Save_failure_returns_to_screen_that_saved()
        {
            var f = Answered(R(VerifyStatus.Retake, "MISMATCH", left: 2));
            f.SaveUnverified();
            f.Failed(VerifyError.Network);
            Assert.AreEqual(VerifyStep.Mismatch, f.Step);
            Assert.IsTrue(f.SaveUnverified());
        }

        [Test]
        public void No_camera_permission_goes_I06_and_saves_skipped()
        {
            var f = new VerifyFlow();
            f.OpenCamera(T0);
            f.CameraDone(CameraOutcome.NoPermission);
            Assert.AreEqual(VerifyStep.NoCamera, f.Step);
            Assert.IsTrue(f.OpenCamera(T0) == false); // I-06에서 카메라 바로 열지 않음(설정 앱 → 돌아와서 뒤로)
            f.Back();
            Assert.AreEqual(VerifyStep.Guide, f.Step);
            f.OpenCamera(T0);
            f.CameraDone(CameraOutcome.NoPermission);
            Assert.IsTrue(f.SaveUnverified());
            Assert.AreEqual("SKIPPED", f.SavingReason);
        }

        [Test]
        public void Back_on_guide_asks_before_leaving_unverified()
        {
            var f = new VerifyFlow();
            Assert.IsTrue(f.Back());
            Assert.AreEqual(VerifyStep.ConfirmLeave, f.Step);
            f.Back(); // 계속 인증
            Assert.AreEqual(VerifyStep.Guide, f.Step);
            f.Back();
            Assert.IsTrue(f.SaveUnverified());
            Assert.AreEqual("SKIPPED", f.SavingReason);
        }

        [Test]
        public void Back_on_confirm_drops_photo_and_returns_to_retake_screen()
        {
            var f = Answered(R(VerifyStatus.Retake, null, left: 2));
            f.OpenCamera(T0);
            f.CameraDone(CameraOutcome.Taken);
            Assert.AreEqual(VerifyStep.Confirm, f.Step);
            f.Back();
            Assert.AreEqual(VerifyStep.Unreadable, f.Step);
            Assert.IsFalse(f.HasPhoto);
        }

        [Test]
        public void Camera_unavailable_returns_with_error()
        {
            var f = new VerifyFlow();
            f.OpenCamera(T0);
            f.CameraDone(CameraOutcome.Unavailable);
            Assert.AreEqual(VerifyStep.Guide, f.Step);
            Assert.AreEqual(VerifyError.Camera, f.Error);
        }

        [Test]
        public void Camera_unavailable_on_retake_returns_to_retake_screen_with_error()
        {
            var f = Answered(R(VerifyStatus.Retake, "MISMATCH", left: 2));
            Assert.IsTrue(f.OpenCamera(T0));
            f.CameraDone(CameraOutcome.Unavailable);
            Assert.AreEqual(VerifyStep.Mismatch, f.Step);
            Assert.AreEqual(VerifyError.Camera, f.Error);
            Assert.IsTrue(f.OpenCamera(T0)); // 다시 누르면 문구가 지워지고 카메라로
            Assert.AreEqual(VerifyError.None, f.Error);
        }

        [Test]
        public void No_permission_on_retake_saves_as_given_up_to_keep_comparison()
        {
            var f = Answered(R(VerifyStatus.Retake, "MISMATCH", left: 2));
            Assert.IsTrue(f.OpenCamera(T0));
            f.CameraDone(CameraOutcome.NoPermission);
            Assert.AreEqual(VerifyStep.NoCamera, f.Step);
            Assert.IsTrue(f.SaveUnverified());
            Assert.AreEqual("RETAKE_GIVEN_UP", f.SavingReason); // SKIPPED면 서버가 대조 표를 남기지 않음
        }

        [Test]
        public void Answers_outside_sending_are_ignored()
        {
            var f = new VerifyFlow();
            f.Answer(R(VerifyStatus.Verified));
            Assert.AreEqual(VerifyStep.Guide, f.Step);
            Assert.IsFalse(f.Send());
        }
    }
}
