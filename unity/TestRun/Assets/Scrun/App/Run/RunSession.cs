using System;
using System.Collections.Generic;

namespace Scrun.App
{
    // 한 번의 달리기 상태(계획 0037). 카운트다운이 끝날 때 Start(지환 BL-11).
    //   달리는 중 ─ 일시정지(D-17) ─ 계속 달리기 → 걸음 다시 잡히면 달리는 중(Figma D-17 노트 "걸음 재감지 후 재개")
    //   └ 끝(D-18 종료하고 인증 · D-19 마무리하기)
    // 시간은 달린 시간만(일시정지·다시 걷기 전 시간은 빠짐). 거리는 섬 아바타의 누적 거리 차이(일시정지 동안 아바타는 서 있음).
    // 속도 시계열은 달린 시간 1초마다 한 칸(BL-12 ⑥, 고스트 재생·인증 대조 근거). 일시정지 구간은 빼고 이어 붙인다(정한 것 2).
    public enum RunPhase
    {
        NotStarted,
        Running,
        Paused,   // D-17·D-18이 떠 있음
        Resuming, // "계속 달리기"를 눌렀고 걸음이 다시 잡히길 기다림(시간 멈춤)
        Finished,
    }

    public sealed class RunSession
    {
        public const double SampleIntervalSec = 1.0;
        // "계속 달리기" 뒤 이 속도 이상이면 걸음이 다시 잡힌 것(아바타는 걸음 2개가 이어지면 3 km/h로 출발, AvatarSpeed).
        // 0보다 조금 큰 값은 멈추며 줄어드는 중이거나 흔들림이라 시간을 다시 흘리지 않음(리뷰)
        public const float ResumeKmh = 1f;

        readonly Func<double> clock;
        readonly List<float> samples = new List<float>();
        double runningSince;  // 지금 달리기 구간이 시작된 clock 값
        double runningBefore; // 앞 구간들의 달린 시간 합
        float startDistance;
        float? finishedDistance;
        bool goalAnnounced;
        float lastKmh; // 마지막으로 받은 아바타 속도(멈출 때 밀린 칸을 채움)

        public RunSession(RunGoal goal, Func<double> clock = null)
        {
            Goal = goal;
            this.clock = clock ?? RealtimeClock.Now;
        }

        public RunGoal Goal { get; }
        public RunPhase Phase { get; private set; } = RunPhase.NotStarted;
        public bool Started => Phase != RunPhase.NotStarted;
        public bool Finished => Phase == RunPhase.Finished;

        // 서버 기록(계획 0035). clientRunId는 이 달리기마다 하나(POST /runs를 다시 보내도 기록 하나)
        public string ClientRunId { get; } = Guid.NewGuid().ToString("N");
        public DateTime? StartedAtUtc { get; private set; }
        public DateTime? EndedAtUtc { get; private set; }
        public string RunId { get; internal set; } // 서버가 준 ID. 못 받았으면 null(끝낼 때 StartBody로 다시 보냄)

        // 달린 시간 1초마다의 아바타 속도(km/h). 서버에 올리는 것(RunRecordUploader)
        public IReadOnlyList<float> Samples => samples;

        public void Start(float playerDistanceM)
        {
            if (Started) return;
            Phase = RunPhase.Running;
            runningSince = clock();
            StartedAtUtc = DateTime.UtcNow;
            startDistance = playerDistanceM;
        }

        // POST /v1/runs 본문. 시작 전엔 null
        public RunStartBody StartBody(string mapInstanceId) => StartedAtUtc == null ? null : new RunStartBody
        {
            ClientRunId = ClientRunId,
            MapInstanceId = mapInstanceId,
            Goal = Goal.ToRequest(),
            StartedAt = StartedAtUtc.Value,
        };

        // 달린 시간(초). 일시정지·다시 걷기 전은 빠짐
        public double ElapsedSec => Phase == RunPhase.Running ? runningBefore + Math.Max(0, clock() - runningSince) : runningBefore;

        public float DistanceM(float playerDistanceM) =>
            finishedDistance ?? (Started ? Math.Max(0f, playerDistanceM - startDistance) : 0f);

        // D-17 "일시정지": 시간 멈춤(아바타는 화면이 RunLink.Moving으로 세움)
        public bool Pause()
        {
            if (Phase == RunPhase.Running)
            {
                // 화면 틱이 밀렸으면(앱이 잠깐 멈춤 등) 멈추기 전까지의 칸을 마지막 속도로 채움 → 달린 시간과 칸 수가 맞음
                FillSamples(lastKmh);
                runningBefore += Math.Max(0, clock() - runningSince);
                Phase = RunPhase.Paused;
                return true;
            }
            if (Phase == RunPhase.Resuming)
            {
                Phase = RunPhase.Paused;
                return true;
            }
            return false;
        }

        // "계속 달리기": 걸음이 다시 잡히면(Update에 moving) 시간이 다시 흐름
        public bool Resume()
        {
            if (Phase != RunPhase.Paused) return false;
            Phase = RunPhase.Resuming;
            return true;
        }

        // 화면이 자주(0.2초마다) 부른다. 아바타 속도(km/h): 다시 걷기 판단과 시계열 칸 채우기
        public void Update(float avatarKmh)
        {
            lastKmh = avatarKmh;
            if (Phase == RunPhase.Resuming && avatarKmh >= ResumeKmh)
            {
                Phase = RunPhase.Running;
                runningSince = clock();
            }
            if (Phase != RunPhase.Running) return;
            FillSamples(avatarKmh);
        }

        void FillSamples(float kmh)
        {
            int due = (int)Math.Floor(ElapsedSec / SampleIntervalSec);
            while (samples.Count < due) samples.Add(Math.Max(0f, kmh));
        }

        // 목표에 처음 닿은 순간 한 번만 true(D-19). 끝났거나 일시정지 중이면 false
        public bool CheckGoalReached(float playerDistanceM)
        {
            if (goalAnnounced || Phase != RunPhase.Running || Progress(playerDistanceM) < 1f) return false;
            goalAnnounced = true;
            return true;
        }

        // 끝(D-18 "종료하고 인증", D-19 "마무리하기"). 거리·시간을 굳히고 서버에 보낼 본문을 돌려줌. 두 번째부터는 null
        public RunFinishBody Finish(float playerDistanceM, float avatarKmh)
        {
            if (!Started || Finished) return null;
            if (Phase == RunPhase.Running)
            {
                FillSamples(avatarKmh);
                runningBefore += Math.Max(0, clock() - runningSince);
            }
            finishedDistance = Math.Max(0f, playerDistanceM - startDistance);
            Phase = RunPhase.Finished;
            EndedAtUtc = DateTime.UtcNow;
            return FinishBody;
        }

        // 끝난 뒤 서버에 보낼 본문(다시 보낼 때도 같은 값 → 서버가 처음 결과를 돌려줌)
        public RunFinishBody FinishBody => !Finished ? null : new RunFinishBody
        {
            EndedAt = EndedAtUtc.Value,
            Sensor = new RunStatsBody
            {
                DistanceM = (int)Math.Round(finishedDistance.Value),
                DurationS = (int)Math.Floor(runningBefore),
            },
        };

        // 0에서 1. 거리 목표는 m, 시간 목표는 초로 비교
        public float Progress(float playerDistanceM)
        {
            double done, target;
            if (Goal.Kind == GoalKind.Distance)
            {
                done = DistanceM(playerDistanceM);
                target = Goal.Units * 100.0; // 0.1 km → m
            }
            else
            {
                done = ElapsedSec;
                target = Goal.Units * 60.0;  // 분 → 초
            }
            return target <= 0 ? 0f : (float)Math.Min(1.0, done / target);
        }

        // HUD 글자: 거리 "1.24", 시간 "12:05"(한 시간 넘으면 "1:02:05")
        public static string DistanceText(float m) =>
            (Math.Floor(m / 10.0) / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        public static string TimeText(double sec)
        {
            long s = (long)Math.Floor(Math.Max(0, sec));
            long h = s / 3600, mm = s % 3600 / 60, ss = s % 60;
            return h > 0 ? $"{h}:{mm:00}:{ss:00}" : $"{mm:00}:{ss:00}";
        }
    }
}
