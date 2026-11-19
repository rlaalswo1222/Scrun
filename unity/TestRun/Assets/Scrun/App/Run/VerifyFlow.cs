using System;

namespace Scrun.App
{
    // 계기판 인증 화면의 단계(계획 0038, Figma E-02–E-10·I-06). 화면 이름은 Figma 번호
    public enum VerifyStep
    {
        Guide,        // E-02 촬영 안내
        ConfirmLeave, // E-02에서 뒤로: 인증하지 않고 나갈지 묻기(미인증으로 저장됨)
        Camera,       // E-03 카메라(민재 Swift, ADR-0011) 열림
        Confirm,      // E-04 촬영 확인(사진만, 읽은 값은 결과에서 — 11/17 결정)
        Sending,      // E-05 인증 중(사진을 보내는 중, 또는 읽은 값 확인을 보내는 중 — Confirming)
        ReadConfirm,  // E-05b 읽은 값 확인(계획 0045): 사진 + 읽은 거리·시간, 맞아요 / 틀렸어요(다시 찍기)
        Verified,     // E-06 인증 완료
        Unreadable,   // E-07 숫자를 읽지 못함(다시 찍기)
        Mismatch,     // E-08 계기판과 센서가 다름
        Unsupported,  // E-09 이 계기판은 읽을 수 없음
        TimeOver,     // E-10 촬영 시간·인증 마감이 지남
        NoCamera,     // I-06 카메라 권한 거부
        Saving,       // 미인증으로 저장하는 중
        Done,         // 끝(인증 완료든 미인증 저장이든) → 화면을 닫음
    }

    // 서버 오류를 흐름이 아는 만큼만(화면이 ApiException을 이걸로 바꿈)
    // Camera는 서버가 아니라 카메라를 못 연 것(안내·다시 찍기 화면에 문구)
    // Stale은 확인할 값이 이미 없음(CONFIRM_NOT_PENDING: 새 사진을 보냈거나 다른 답을 함) → 다시 찍기로
    public enum VerifyError { None, Network, Closed, Busy, NotFound, Other, Camera, Stale, Timeout } // Timeout: 서버 OCR 시간 초과(503 details.reason OCR_TIMEOUT, 계획 0055)

    // 흐름 판단만(순수 C#, EditMode 테스트). 카메라·서버·시계는 밖에서 결과만 넣는다(RunReadyFlow와 같은 방식)
    public sealed class VerifyFlow
    {
        public VerifyStep Step { get; private set; } = VerifyStep.Guide;
        public VerificationResult Result { get; private set; } // 마지막 서버 답(E-06–E-09 표·남은 횟수)
        public VerifyError Error { get; private set; }         // 마지막 실패(화면 안내용). 다음 동작에서 지움
        public DateTime? Deadline { get; private set; }        // 인증 마감(UTC, 서버가 준 것). E-07·08·09 "남은 시간"
        public string SavingReason { get; private set; }       // 미인증 저장 이유(SKIPPED·TIMEOUT·RETAKE_GIVEN_UP)

        // 다시 찍기로 돌아갈 화면(카메라에서 취소하면 여기로)
        VerifyStep before = VerifyStep.Guide;

        public bool HasPhoto { get; private set; }

        // 읽은 값 확인(맞아요·틀렸어요)을 보내는 중. 화면은 E-05 대신 E-05b를 잠근 채로 둠
        public bool Confirming { get; private set; }

        // 틀렸어요 → 서버가 다시 찍기로 답함: 화면이 바로 카메라(E-03)를 엶(계획 0045)
        public bool Rejected => Step == VerifyStep.Guide && Result != null && Result.Reason == VerifyReason.UserRejected;

        // 서버가 이미 정함(인증 완료·미인증으로 저장됨) — 더 보낼 것 없음
        public bool Saved => Result != null && (Result.Status == VerifyStatus.Verified || Result.Status == VerifyStatus.Unverified);

        // 결과 화면에서 다시 찍을 수 있는지(서버가 아직 정하지 않았고 횟수가 남음)
        public bool CanRetake => Result != null && Result.Status == VerifyStatus.Retake && (Result.RetakesLeft ?? 0) > 0;

        // E-02 "카메라 열기", E-04·07·08·09 "다시 촬영"
        public bool OpenCamera(DateTime nowUtc)
        {
            if (Step != VerifyStep.Guide && Step != VerifyStep.Confirm && !(IsRetakeScreen && CanRetake)) return false;
            if (PastDeadline(nowUtc)) return ToTimeOver();
            before = Step == VerifyStep.Confirm ? before : Step;
            Error = VerifyError.None;
            Step = VerifyStep.Camera;
            return true;
        }

        public void CameraDone(CameraOutcome outcome)
        {
            if (Step != VerifyStep.Camera) return;
            switch (outcome)
            {
                case CameraOutcome.Taken:
                    HasPhoto = true;
                    Step = VerifyStep.Confirm;
                    break;
                case CameraOutcome.TimedOut: // E-03 남은 시간(0:45) 끝 → E-10
                    ToTimeOver();
                    break;
                case CameraOutcome.NoPermission:
                    Step = VerifyStep.NoCamera;
                    break;
                case CameraOutcome.Unavailable:
                    Error = VerifyError.Camera;
                    Step = before;
                    break;
                default: // 취소(X) → 열기 전 화면
                    Step = before;
                    break;
            }
        }

        // E-04 "이 사진으로 인증"
        public bool Send()
        {
            if (Step != VerifyStep.Confirm || !HasPhoto) return false;
            Error = VerifyError.None;
            Step = VerifyStep.Sending;
            return true;
        }

        public void Answer(VerificationResult r)
        {
            if (Step != VerifyStep.Sending && Step != VerifyStep.Saving) return;
            bool saving = Step == VerifyStep.Saving;
            Result = r;
            Confirming = false;
            // 값 확인은 마감에 유예가 붙어 옴(서버 CONFIRM_GRACE). 그 밖의 다시 찍기는 인증 마감
            if (r.RetakeDeadline.HasValue) Deadline = r.RetakeDeadline.Value.ToUniversalTime();
            if (!saving && r.Status == VerifyStatus.Confirm)
            {
                Step = VerifyStep.ReadConfirm; // 사진은 맞아요·틀렸어요까지 들고 있음(E-05b에 보여 줌)
                return;
            }
            HasPhoto = false; // 보낸 사진은 버림(화면이 Discard)
            Step = saving ? VerifyStep.Done : StepFor(r); // 미인증 저장이 끝나면 화면을 닫음
            if (Rejected) before = VerifyStep.Guide; // 다시 찍다 취소하면 안내로
        }

        // E-05b "맞아요, 인증하기"(true)·"틀렸어요, 다시 찍기"(false). 값은 고칠 수 없음(BL-17 ④)
        public bool AnswerRead(bool correct)
        {
            if (Step != VerifyStep.ReadConfirm || Result?.AttemptId == null) return false;
            Error = VerifyError.None;
            Confirming = true;
            Step = VerifyStep.Sending;
            return true;
        }

        public void Failed(VerifyError e)
        {
            if (Step != VerifyStep.Sending && Step != VerifyStep.Saving) return;
            Error = e;
            bool confirming = Confirming;
            Confirming = false;
            if (e == VerifyError.Closed) // 마감이 지남(VERIFICATION_CLOSED)
            {
                HasPhoto = false;
                Step = VerifyStep.TimeOver;
                return;
            }
            if (e == VerifyError.Timeout && !confirming) // 이 사진은 읽는 데 너무 오래 걸림 → 같은 사진을 다시 보내지 말고 다시 찍기(횟수 안 씀, 계획 0055 리뷰)
            {
                HasPhoto = false;
                before = VerifyStep.Guide;
                Step = VerifyStep.Guide;
                return;
            }
            if (e == VerifyError.Stale && confirming) // 확인할 값이 이미 없음 → 다시 찍기
            {
                HasPhoto = false;
                before = VerifyStep.Guide;
                Step = VerifyStep.Guide;
                return;
            }
            // 사진을 보내다 실패 → E-04로(같은 사진을 다시 보낼 수 있게). 확인을 보내다 실패 → E-05b에 그대로(다시 누름).
            // 저장하다 실패 → 저장을 누른 화면으로
            Step = Step == VerifyStep.Sending ? (confirming ? VerifyStep.ReadConfirm : VerifyStep.Confirm) : savingFrom;
        }

        VerifyStep savingFrom = VerifyStep.Guide;

        // 미인증으로 저장: E-07·08·09(그만둠), E-10(시간 초과), I-06·나가기 확인(촬영 없이).
        // 다시 찍으려다 I-06(권한 거부)에서 저장하면 그만둔 것 — 서버가 마지막 시도의 대조 표를 남김(SKIPPED면 잃음)
        public bool SaveUnverified()
        {
            string reason;
            if (IsRetakeScreen || Step == VerifyStep.NoCamera && Result != null) reason = "RETAKE_GIVEN_UP";
            else if (Step == VerifyStep.TimeOver) reason = "TIMEOUT";
            else if (Step == VerifyStep.NoCamera || Step == VerifyStep.ConfirmLeave) reason = "SKIPPED";
            else return false;
            if (Saved)
            {
                Step = VerifyStep.Done; // 서버가 이미 정함(다시 찍기를 다 씀·마감으로 닫힘) → 보낼 것 없음
                return false;
            }
            savingFrom = Step;
            SavingReason = reason;
            Error = VerifyError.None;
            Step = VerifyStep.Saving;
            return true;
        }

        // E-06 "다음", 서버가 이미 정한 결과 화면(다시 찍기를 다 쓴 E-08·09)의 "확인"
        public void Finish()
        {
            if (Saved) Step = VerifyStep.Done;
        }

        // 뒤로(Esc·화면 뒤로 버튼). 처리했으면 true
        public bool Back()
        {
            switch (Step)
            {
                case VerifyStep.Guide: Step = VerifyStep.ConfirmLeave; return true;
                case VerifyStep.ConfirmLeave: Step = VerifyStep.Guide; return true;
                case VerifyStep.Confirm: // 이 사진은 버리고 안내(또는 다시 찍기 화면)로
                    HasPhoto = false;
                    Step = before;
                    return true;
                case VerifyStep.NoCamera: Step = before; return true;
                default: return true; // 보내는 중·값 확인·결과 화면에선 뒤로로 빠지지 않음(버튼으로만)
            }
        }

        // 시계(화면이 매초). 다시 찍기·값 확인 화면에서 마감이 지나면 E-10
        public void Tick(DateTime nowUtc)
        {
            if ((IsRetakeScreen && CanRetake || Step == VerifyStep.Confirm || Step == VerifyStep.Guide ||
                 Step == VerifyStep.ReadConfirm) && PastDeadline(nowUtc))
                ToTimeOver();
        }

        // 남은 시간(초, 마감을 모르면 null)
        public int? SecondsLeft(DateTime nowUtc) =>
            Deadline.HasValue ? Math.Max(0, (int)Math.Ceiling((Deadline.Value - nowUtc).TotalSeconds)) : (int?)null;

        bool IsRetakeScreen => Step == VerifyStep.Unreadable || Step == VerifyStep.Mismatch || Step == VerifyStep.Unsupported;

        bool PastDeadline(DateTime nowUtc) => Deadline.HasValue && nowUtc >= Deadline.Value;

        bool ToTimeOver()
        {
            HasPhoto = false;
            Step = VerifyStep.TimeOver;
            return false;
        }

        static VerifyStep StepFor(VerificationResult r)
        {
            if (r.Status == VerifyStatus.Verified) return VerifyStep.Verified;
            switch (r.Reason)
            {
                case VerifyReason.Mismatch: return VerifyStep.Mismatch;
                case VerifyReason.Unsupported: return VerifyStep.Unsupported;
                case VerifyReason.Timeout when r.Status == VerifyStatus.Unverified: return VerifyStep.TimeOver;
            }
            if (r.Status == VerifyStatus.Retake && r.Reason == VerifyReason.UserRejected) return VerifyStep.Guide; // 바로 카메라
            if (r.Status == VerifyStatus.Retake) return VerifyStep.Unreadable;
            return VerifyStep.Done; // 미인증으로 저장됨(촬영 없이·그만둠)
        }
    }

    // 서버 값 이름(문자열을 곳곳에 쓰지 않게)
    public static class VerifyStatus
    {
        public const string Verified = "VERIFIED", Unverified = "UNVERIFIED", Retake = "RETAKE_REQUIRED", Confirm = "CONFIRM_REQUIRED",
                            Pending = "PENDING"; // 기록 상태: 인증 대기(종료 뒤 마감 전)
    }

    public static class VerifyReason
    {
        public const string Mismatch = "MISMATCH", Unsupported = "UNSUPPORTED_CONSOLE", Timeout = "TIMEOUT", UserRejected = "USER_REJECTED";
    }
}
