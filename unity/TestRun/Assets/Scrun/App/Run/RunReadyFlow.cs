using System;

namespace Scrun.App
{
    // 러닝 설정 뒤 달리기 시작까지(계획 0035, BL-10·11, Figma D-07·D-08·D-09·I-01)
    public enum ReadyStep
    {
        Checking,     // D-07 센서 확인(흐림)
        SensorFailed, // I-01 폰 센서를 못 읽음
        Waiting,      // D-08 걸음 기다림(흐림)
        Countdown,    // D-09 흐림 풀림, 3·2·1
        Started,      // 달리기 시작(StartRunning)
        Cancelled,    // X·뒤로 → D-01 러닝 설정(정한 것 3)
        NeedsCalibration, // 서버에도 쓸 수 있는 보정이 없음 → 맵을 나가 보정 안내(A-11, 코스 상세가 받음)
    }

    // 보정값 줄. 불러오기 결과(CalibrationLoad)·서버 오류에서 정한다(ReadyCalibration, 계획 0035 리뷰)
    public enum CalibrationState
    {
        Ready,            // 센서에 보정이 있음
        Loading,          // 서버에서 불러오는 중(맵 입장 직후)
        RetryLater,       // 일시 실패(연결 안 됨·5xx) → 그 줄에서 다시 시도
        NeedsCalibration, // 서버에도 없거나 이 앱이 못 읽는 옛 모양 → 맵을 나가 보정 안내
        Failed,           // 다시 보내도 같은 실패(4xx) → 넘어갈 수 없음, X로 나감
        Cancelled,        // 불러오는 사이 로그인이 바뀜 → 로그인 쪽이 화면을 옮기므로 아무것도 안 함
    }

    public static class ReadyCalibration
    {
        public static CalibrationState From(CalibrationLoad r) => r switch
        {
            CalibrationLoad.Loaded => CalibrationState.Ready,
            CalibrationLoad.Cancelled => CalibrationState.Cancelled,
            _ => CalibrationState.NeedsCalibration, // NotOnServer·Unreadable
        };

        public static CalibrationState From(ApiException e) =>
            e.Failure == ApiFailure.NotLoggedIn ? CalibrationState.Cancelled
            : e.IsTransient ? CalibrationState.RetryLater
            : CalibrationState.Failed;
    }

    // 한 프레임의 입력. 화면(RunReadyView)이 Native.Speed·Headphones·Permissions·실시간 방에서 모아 넘긴다
    public struct ReadyInputs
    {
        public SensorFlow Phone;
        public SensorFlow AirPods;
        public HeadphoneStatus Headphones;
        public bool HeadphoneMotionGranted;
        public CalibrationState Calibration;
        public RunTogetherStatus? Network; // 실시간 방이 없으면 null(혼자 달림)
        // 걷고 있음 = 내 아바타가 움직이는 판단과 같은 것(AvatarSpeed.Stepping, 계획 0034): 누적 걸음 수로 감지 지연 없이,
        // 걸음 2번이 이어져야 출발, 200·215 spm 넘는 "걸음"은 벨트 진동으로 버림, 1.2초 안 늘면 멈춤(PR #49 리뷰 3번)
        public bool Stepping;
    }

    // 화면 없이 단계만 정한다 → 테스트(RunReadyFlowTests). 시각은 초(RealtimeClock.Now 등), 밖에서 넣는다.
    // 막는 것은 폰 센서뿐: 이어폰·네트워크는 안 되면 "없이 진행"으로 보여 주고 넘어간다(BL-10 ②).
    // 보정값은 입장할 때 서버에 있다고 확인됐으므로(409 CALIBRATION_REQUIRED) 센서에 다 들어올 때까지 기다린다.
    // 그래도 쓸 수 없으면(서버 보정이 옛 모양 등) D-07에 가두지 않고 보정 안내로 보낸다. 넘어갈 수 없는 경우(4xx)엔 X로 D-01(11/16 리뷰).
    public sealed class RunReadyFlow
    {
        // 폰 센서가 이 시간 동안 이어서 Ok가 아니면 I-01(요청 100 Hz라 보통 1초 안, 가설 → 아이폰에서 잼).
        // D-07에 들어온 때가 아니라 Ok가 아니게 된 때부터 잰다(보정이 늦게 와 D-07이 길어져도 한 프레임 Waiting으로 I-01이 되지 않게, 리뷰)
        public const double CheckTimeoutSec = 5.0;
        // 다 정상이면 이만큼 보여 준 뒤 저절로 D-08(정한 것 1). "준비 완료"는 기다리지 않고 바로
        public const double AutoAdvanceSec = 1.5;
        public const int CountdownFrom = 3;
        // 걷기 시작·멈춤은 Gait(속도 추정 걸음 상태)로 보지 않는다: 바뀌는 데 2.5초 넘게 걸려 3초 카운트다운 안에 멈춤을 못 잡고(11/16 에디터 확인),
        // 돌아온 D-08에서 아직 Walking인 Gait로 바로 다시 세어 깜박였음(/code-review). 아바타와 같은 판단(Stepping)을 쓴다

        double? phoneBadSince;
        double? readySince;
        double countdownAt;

        public ReadyStep Step { get; private set; } = ReadyStep.Checking;

        // D-09에 보일 숫자(3·2·1). 카운트다운이 아니면 0
        public int CountdownNumber { get; private set; }

        public void Begin()
        {
            Step = ReadyStep.Checking;
            phoneBadSince = null;
            readySince = null;
            CountdownNumber = 0;
        }

        // 폰 센서가 정상이고 보정값이 들어와 D-08로 갈 수 있음
        public static bool CanProceed(in ReadyInputs i) =>
            i.Phone == SensorFlow.Ok && i.Calibration == CalibrationState.Ready;

        // 흐린 맵을 깔고 보여 주는 단계(D-07·D-08·I-01). D-09는 맵이 보임
        public static bool Blurred(ReadyStep s) =>
            s == ReadyStep.Checking || s == ReadyStep.Waiting || s == ReadyStep.SensorFailed;

        // 매 프레임. 바뀐 단계를 돌려준다
        public ReadyStep Update(double now, in ReadyInputs i)
        {
            switch (Step)
            {
                case ReadyStep.Checking:
                    if (PhoneFailed(now, i))
                    {
                        Fail();
                        break;
                    }
                    if (i.Calibration == CalibrationState.NeedsCalibration)
                    {
                        Step = ReadyStep.NeedsCalibration;
                        break;
                    }
                    if (!CanProceed(i))
                    {
                        readySince = null;
                        break;
                    }
                    readySince ??= now;
                    if (now - readySince.Value >= AutoAdvanceSec) Step = ReadyStep.Waiting;
                    break;

                case ReadyStep.Waiting:
                    if (PhoneFailed(now, i))
                    {
                        Fail();
                        break;
                    }
                    if (i.Stepping) StartCountdown(now);
                    break;

                case ReadyStep.Countdown:
                    if (PhoneFailed(now, i))
                    {
                        Fail();
                        break;
                    }
                    if (!i.Stepping) // 벨트에서 내려옴 → 다시 기다림(정한 것 4)
                    {
                        Step = ReadyStep.Waiting;
                        CountdownNumber = 0;
                        break;
                    }
                    double left = CountdownFrom - (now - countdownAt);
                    if (left <= 0)
                    {
                        Step = ReadyStep.Started;
                        CountdownNumber = 0;
                    }
                    else CountdownNumber = (int)Math.Ceiling(left);
                    break;
            }
            return Step;
        }

        // "준비 완료": 다 정상이면 1.5초를 기다리지 않고 바로
        public bool Skip(in ReadyInputs i)
        {
            if (Step != ReadyStep.Checking || !CanProceed(i)) return false;
            Step = ReadyStep.Waiting;
            return true;
        }

        // I-01 "다시 시도" → D-07부터(화면이 센서를 껐다 켬)
        public void Retry()
        {
            if (Step == ReadyStep.SensorFailed) Begin();
        }

        // 폰 센서를 못 씀: 끊김(Lost)은 바로, 그 밖에 Ok가 아닌 상태(켜지는 중·꺼짐)는 이어서 CheckTimeoutSec.
        // D-08·D-09도 같다: 센서가 꺼지면(Off, Lost가 되지 않음) 걸어도 카운트다운이 안 돼 조용히 기다리기만 했음(#49 2차 리뷰)
        bool PhoneFailed(double now, in ReadyInputs i)
        {
            if (i.Phone == SensorFlow.Ok)
            {
                phoneBadSince = null;
                return false;
            }
            phoneBadSince ??= now;
            return i.Phone == SensorFlow.Lost || now - phoneBadSince.Value >= CheckTimeoutSec;
        }

        // 보정값 줄의 칩을 눌러 다시 불러올 수 있음
        public static bool CanRetryCalibration(CalibrationState c) => c == CalibrationState.RetryLater;

        // X·뒤로 가기. 이미 시작했으면 아무것도 안 함
        public bool Cancel()
        {
            if (Step == ReadyStep.Started || Step == ReadyStep.Cancelled || Step == ReadyStep.NeedsCalibration) return false;
            Step = ReadyStep.Cancelled;
            CountdownNumber = 0;
            return true;
        }

        void StartCountdown(double now)
        {
            Step = ReadyStep.Countdown;
            countdownAt = now;
            CountdownNumber = CountdownFrom;
        }

        void Fail()
        {
            Step = ReadyStep.SensorFailed;
            readySince = null;
            CountdownNumber = 0;
        }
    }

    // D-07 줄 칩 모양
    public enum RowTone
    {
        Ok,       // 초록(Chip/Success): 정상·연결됨
        Pending,  // 회색: 확인 중·불러오는 중·연결 중
        Fallback, // 회색: 없이 진행(막지 않음)
        Failed,   // 주황: 다시 시도
    }

    public readonly struct ReadyRow
    {
        public readonly string Sub;  // 줄 아래 작은 글자
        public readonly string Chip;
        public readonly RowTone Tone;

        public ReadyRow(string sub, string chip, RowTone tone)
        {
            Sub = sub;
            Chip = chip;
            Tone = tone;
        }
    }

    // D-07·D-08·D-09·I-01 글자(Figma 문구 그대로, 없는 상태 문구는 지환 임시 → 상준이 다듬음)
    public static class RunReadyText
    {
        public const string CountdownChip = "달림이 감지됐어요!";

        public static ReadyRow Phone(SensorFlow f) => f switch
        {
            SensorFlow.Ok => new ReadyRow("걸음·발 충격 측정", "정상", RowTone.Ok),
            SensorFlow.Lost => new ReadyRow("걸음·발 충격 측정", "안 들어와요", RowTone.Failed),
            _ => new ReadyRow("걸음·발 충격 측정", "확인 중", RowTone.Pending),
        };

        // 에어팟은 막지 않는다: 연결 + 헤드폰 모션 허용 + 데이터가 들어와야 "연결됨", 그 밖에는 폰만으로
        public static ReadyRow Earphone(HeadphoneStatus h, bool motionGranted, SensorFlow flow)
        {
            const string Fallback = "폰 센서로만 속도를 재요";
            if (h != HeadphoneStatus.Connected || !motionGranted)
                return new ReadyRow(Fallback, "이어폰 없이 진행", RowTone.Fallback);
            return flow switch
            {
                SensorFlow.Ok => new ReadyRow("헤드폰 모션 사용", "연결됨", RowTone.Ok),
                SensorFlow.Lost => new ReadyRow(Fallback, "이어폰 없이 진행", RowTone.Fallback),
                _ => new ReadyRow("헤드폰 모션 사용", "확인 중", RowTone.Pending),
            };
        }

        public static ReadyRow Calibration(CalibrationState s) => s switch
        {
            CalibrationState.Ready => new ReadyRow("내 걸음에 맞춘 속도 공식", "정상", RowTone.Ok),
            CalibrationState.Loading => new ReadyRow("내 걸음에 맞춘 속도 공식", "불러오는 중", RowTone.Pending),
            CalibrationState.RetryLater => new ReadyRow("서버에서 못 불러왔어요", "다시 시도", RowTone.Failed),
            CalibrationState.NeedsCalibration => new ReadyRow("다시 보정이 필요해요", "다시 보정", RowTone.Failed),
            CalibrationState.Failed => new ReadyRow("보정값을 읽을 수 없어요", "불러올 수 없어요", RowTone.Failed),
            _ => new ReadyRow("내 걸음에 맞춘 속도 공식", "불러오는 중", RowTone.Pending), // Cancelled: 로그인 화면으로 가는 중
        };

        // 실시간 방(같이 뛰기). 안 되면 혼자 달린다(막지 않음)
        public static ReadyRow Network(RunTogetherStatus? s) => s switch
        {
            RunTogetherStatus.Connected => new ReadyRow("공동 맵 연결", "정상", RowTone.Ok),
            RunTogetherStatus.Connecting or RunTogetherStatus.Reconnecting => new ReadyRow("공동 맵 연결", "연결 중", RowTone.Pending),
            _ => new ReadyRow("다른 러너 없이 달려요", "혼자 달려요", RowTone.Fallback),
        };

        // D-08 왼쪽 위 칩: "노을 호수 한 바퀴", "목표 3.0km"
        public static string GoalChip(RunGoal g) => $"목표 {g.ValueText}{g.UnitText}";
    }
}
