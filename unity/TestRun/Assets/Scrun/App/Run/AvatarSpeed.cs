using System;
using System.Collections.Generic;

namespace Scrun.App
{
    // 센서 값 → 내 아바타 속도(계획 0034, BL-13). 속도 추정은 걸음 상태가 바뀌는 데 2.5초 넘게 걸려서
    // (SpeedEstimator holdS) 벨트에서 내려와도 아바타가 계속 달리다가 0으로 뚝 떨어졌다. 감지 지연이 없는
    // 누적 걸음 수(SpeedReading.Steps)로 멈춤·출발을 먼저 알아채고, 위치 속도는 부드럽게 바꾼다.
    //   - 멈춤: 걸음이 StopAfterSec 동안 안 늘면 0을 향해 빨리(StopDecel) 줄임
    //   - 출발: 멈춘 뒤 걸음이 2번 이어지면(사이 StopAfterSec 미만) 바로 움직임. 추정이 아직 없으면 RestartKmh
    //     (러닝머신도 다시 켜면 낮은 속도부터). 짧게 멈춰 추정기가 멈추기 전 값을 아직 들고 있으면 FreshAfterSec 뒤부터 믿음
    //   - 걷는 중: 추정을 따라가되 순간 점프 없이(Accel)
    //   - 폰 데이터가 끊기거나 "걸음"이 너무 빠르면(벨트 진동) 멈춤과 같게(안내는 BL-15, 계획 0039 D-15)
    //   - 추정이 말이 안 되면(Unsteady, I-03, 계획 0039): 25 km/h를 넘거나, 믿던 추정에서 5 km/h 넘게 튀어 2초가 안 됨 →
    //     직전 속도 유지, 이상이 5초 넘게 이어지면 서서히 줄임. 2초 넘게 이어진 큰 변화는 진짜 변화로 받아들임
    //     (보폭을 늘린 진짜 변화를 막지 않게). 문턱은 가설: 10/29 폰 추정 기록 244초엔 한 번도 안 걸림(0.5초 변화 95% 0.36 km/h)
    // 걸음 빠르기는 걸음 수가 늘어난 순간들의 시각으로 잰다(정수 걸음 수를 시간 창으로 나누면 짧은 창에서 크게 튐).
    // 매 프레임 한 번 Update. 시각은 센서 시각(SpeedReading.Time)을 쓴다.
    public sealed class AvatarSpeed
    {
        public const float StopAfterSec = 1.2f;        // 2 km/h 걷기도 걸음 사이 0.8초 안팎(10/29 기록) → 조금 길게(가설, 실기기에서 조정)
        public const float RestartKmh = 3f;            // 추정을 믿기 전 다시 출발 속도(계획 0034 정할 것 1)
        public const float AccelKmhPerSec = 4f;        // 평소 속도 변화 한도(3 → 10 km/h에 약 1.8초)
        public const float StopDecelKmhPerSec = 15f;   // 멈출 때(10 → 0 km/h에 약 0.7초)
        public const float MaxStepRateSpm = 215f;      // 이보다 빠른 "걸음"은 벨트 모터 진동(계획 0024 11/11, 가만히 둔 폰이 222 spm)
        // 출발은 걸음 두세 개로 판단해 프레임 시각 오차에 흔들림(222 spm 진동이 210으로 보일 수 있음) → 더 엄격하게.
        // 서 있다가 바로 200 spm 넘게 뛰는 사람은 드묾(가설)
        public const float StartMaxStepRateSpm = 200f;
        public const double RateWindowSec = 2.0;
        // 짧게(추정기 holdS 2.5초 안) 멈췄다 다시 걸으면 추정기는 멈추기 전 속도를 아직 낸다 → 새 걸음이 반영될 때까지(smoothS 2초 + 여유, 가설)
        public const double FreshAfterSec = 3.0;
        public const float MaxPlausibleKmh = 25f;      // 러닝머신 최고 속도 근처. 넘으면 이상
        public const float JumpKmh = 5f;               // 믿던 추정에서 이만큼 넘게 튀면 확인
        public const double JumpConfirmSec = 2.0;      // 큰 변화가 이만큼 이어지면 진짜로 받아들임
        public const double UnsteadyHoldSec = 5.0;     // 이상이 이만큼 넘게 이어지면 직전 속도에서 서서히 줄임
        public const float UnsteadyDecelKmhPerSec = 1f;
        const int StartSteps = 2;                       // 한 번 툭 친 것으로 출발하지 않게
        const int MinRateEvents = 4;                    // 걷는 중 빠르기 판단엔 걸음 사이 3개 이상

        public float SpeedKmh { get; private set; }
        public bool Stepping { get; private set; }
        public bool Unsteady { get; private set; } // 추정이 이상해 직전 속도로 버티는 중(I-03 띠)

        // 반영 지연(BL-13 ③): 멈춤 = 마지막 걸음 → 아바타 0(걸음이 끊겨 멈춘 경우만), 출발 = 첫 걸음 → 아바타 움직임
        public event Action<AvatarDelay> DelayMeasured;

        readonly Queue<(double t, int steps)> events = new Queue<(double, int)>(); // 걸음 수가 는 순간들(최근 2초)
        double lastTime = double.NaN;
        int lastSteps = -1;
        double lastStepAt = double.NegativeInfinity;
        int startEvents;                 // 멈춘 뒤 이어진 걸음 순간 수
        double firstStepAfterStop = double.NaN;
        double stopFrom = double.NaN;    // 멈춤 지연을 재는 중: 마지막 걸음 시각
        double startFrom = double.NaN;   // 출발 지연을 재는 중: 첫 걸음 시각
        float held = RestartKmh;         // 추정을 안 쓸 때의 속도: 출발 직후엔 RestartKmh, 추정을 받은 뒤엔 마지막 추정
        bool sawNoEstimate = true;       // 멈춘 뒤 추정기가 "없음"을 낸 적 있음 = 그 뒤 추정은 새 걸음 것
        double trustFrom = double.NegativeInfinity; // 이 시각부터 추정을 믿음
        bool heldIsEstimate;             // held가 받아들인 추정(출발 직후 RestartKmh가 아님) → 튐 판단의 기준이 됨
        double jumpSince = double.NaN;   // 큰 변화가 시작된 시각
        float jumpLevel;                 // 그 큰 변화의 새 수준(이 근처로 2초 이어져야 진짜 변화)
        double unsteadySince = double.NaN;

        public void Reset()
        {
            SpeedKmh = 0f;
            ResetTracking();
        }

        // 걸음 셈과 재던 지연만 새로. 속도는 그대로 두고 Stepping이 꺼지니 다음 프레임부터 부드럽게 줄어듦
        void ResetTracking()
        {
            Stepping = false;
            events.Clear();
            lastTime = double.NaN;
            lastSteps = -1;
            lastStepAt = double.NegativeInfinity;
            startEvents = 0;
            firstStepAfterStop = stopFrom = startFrom = double.NaN;
            held = RestartKmh;
            sawNoEstimate = true;
            trustFrom = double.NegativeInfinity;
            ClearUnsteady();
        }

        void ClearUnsteady()
        {
            Unsteady = false;
            heldIsEstimate = false;
            jumpSince = unsteadySince = double.NaN;
        }

        // 추정 하나를 받아들일지(I-03). 받아들이면 그 값, 아니면 직전 속도(오래 이상하면 서서히 줄인 값)
        float Plausible(double now, float est, float dt)
        {
            bool tooFast = est > MaxPlausibleKmh;
            bool jump = heldIsEstimate && Math.Abs(est - held) > JumpKmh;
            if (jump && !tooFast)
            {
                // 같은 새 수준이 이어질 때만 셈(14로 튀었다 2로 떨어지는 흔들림을 2초 뒤 받아들이지 않게, 리뷰)
                if (double.IsNaN(jumpSince) || Math.Abs(est - jumpLevel) > JumpKmh)
                {
                    jumpSince = now;
                    jumpLevel = est;
                }
                if (now - jumpSince >= JumpConfirmSec) jump = false; // 2초 넘게 이어짐 → 진짜 변화
            }
            else jumpSince = double.NaN;

            if (!tooFast && !jump)
            {
                Unsteady = false;
                unsteadySince = double.NaN;
                heldIsEstimate = true;
                return held = est;
            }
            Unsteady = true;
            if (double.IsNaN(unsteadySince)) unsteadySince = now;
            if (now - unsteadySince > UnsteadyHoldSec) held = Math.Max(0f, held - UnsteadyDecelKmhPerSec * dt);
            return held;
        }

        public float Update(in SpeedReading r, bool phoneOk)
        {
            double now = r.Time;
            if (!double.IsNaN(lastTime) && now < lastTime) ResetTracking(); // 센서를 다시 켬(시각이 0부터)
            float dt = double.IsNaN(lastTime) ? 0f : (float)(now - lastTime);
            lastTime = now;

            CountSteps(now, r.Steps);
            bool was = Stepping;
            bool recent = now - lastStepAt < StopAfterSec;
            if (Stepping)
                Stepping = phoneOk && recent && Rate() <= MaxStepRateSpm;
            else
                Stepping = phoneOk && recent && startEvents >= StartSteps && StartRate() <= StartMaxStepRateSpm;

            if (was && !Stepping)
            {
                // 사람이 멈춘 경우(걸음이 끊김)만 멈춤 지연으로 잰다. 폰 끊김·진동으로 꺼진 건 사람 반응이 아님
                stopFrom = phoneOk && !recent ? lastStepAt : double.NaN;
                startEvents = 0;
                firstStepAfterStop = double.NaN;
                startFrom = double.NaN;
                sawNoEstimate = false;
            }
            if (!was && Stepping)
            {
                startFrom = firstStepAfterStop;
                stopFrom = double.NaN;
                held = RestartKmh;
                ClearUnsteady();
                trustFrom = sawNoEstimate ? double.NegativeInfinity : now + FreshAfterSec;
            }
            if (!Stepping && !r.HasSpeed) sawNoEstimate = true;

            float target;
            if (!Stepping)
            {
                target = 0f;
                Unsteady = false;
            }
            else if (r.HasSpeed && now >= trustFrom) target = Plausible(now, r.SpeedKmh, dt);
            else if (!r.HasSpeed && Unsteady)
            {
                // 추정이 잠깐 빔(걸음 상태가 바뀌는 중 등): 이상은 아님 → 띠를 내리고 마지막으로 믿던 속도로(리뷰)
                Unsteady = false;
                jumpSince = unsteadySince = double.NaN;
                target = held;
            }
            else target = held; // 출발 직후·추정을 아직 못 믿을 때는 낮은 속도, 달리는 중 추정이 잠깐 비면 마지막 추정

            float down = Stepping ? AccelKmhPerSec : StopDecelKmhPerSec;
            SpeedKmh = target >= SpeedKmh
                ? Math.Min(target, SpeedKmh + AccelKmhPerSec * dt)
                : Math.Max(target, SpeedKmh - down * dt);

            if (!double.IsNaN(stopFrom) && SpeedKmh <= 0f)
            {
                DelayMeasured?.Invoke(new AvatarDelay(AvatarDelayKind.Stop, now - stopFrom, now));
                stopFrom = double.NaN;
            }
            if (!double.IsNaN(startFrom) && SpeedKmh > 0f)
            {
                DelayMeasured?.Invoke(new AvatarDelay(AvatarDelayKind.Start, now - startFrom, now));
                startFrom = double.NaN;
            }
            return SpeedKmh;
        }

        void CountSteps(double now, int steps)
        {
            if (lastSteps >= 0 && steps < lastSteps) // 걸음 수가 줄면 새로 셈
            {
                events.Clear();
                lastSteps = -1;
                startEvents = 0;
                firstStepAfterStop = double.NaN;
            }
            if (lastSteps >= 0 && steps > lastSteps)
            {
                if (!Stepping)
                {
                    // 띄엄띄엄 툭 친 걸음은 이어 세지 않음(서 있는 동안 폰이 두 번 흔들려도 출발하지 않게)
                    if (now - lastStepAt >= StopAfterSec) startEvents = 0;
                    if (startEvents == 0) firstStepAfterStop = now;
                    startEvents++; // 한 프레임에 여러 걸음이 와도 한 번(빠르기를 부풀리지 않게)
                }
                lastStepAt = now;
                events.Enqueue((now, steps));
            }
            lastSteps = steps;
            while (events.Count > 0 && now - events.Peek().t > RateWindowSec) events.Dequeue();
        }

        // 멈춘 뒤 이어진 걸음의 빠르기(분당): 첫 걸음 순간 → 마지막 걸음 순간
        float StartRate()
        {
            double span = lastStepAt - firstStepAfterStop;
            if (double.IsNaN(span) || span <= 0) return float.PositiveInfinity;
            return (float)((startEvents - 1) / span * 60.0);
        }

        // 최근 2초 걸음 빠르기(분당): 창 안 첫 걸음 순간 → 마지막 걸음 순간. 걸음 사이가 3개 미만이면 판단 안 함(0)
        float Rate()
        {
            if (events.Count < MinRateEvents) return 0f;
            var first = events.Peek();
            double span = lastStepAt - first.t;
            if (span <= 0) return 0f;
            return (float)((lastSteps - first.steps) / span * 60.0);
        }
    }

    public enum AvatarDelayKind { Stop, Start }

    public readonly struct AvatarDelay
    {
        public readonly AvatarDelayKind Kind;
        public readonly double Sec;  // 걸음 → 아바타 반영까지
        public readonly double At;   // 센서 시각
        public AvatarDelay(AvatarDelayKind kind, double sec, double at)
        {
            Kind = kind;
            Sec = sec;
            At = at;
        }
    }
}
