using System;
using System.Collections.Generic;
using System.Globalization;

namespace Scrun.App
{
    // 고스트러너(계획 0050, BL-24): 내 지난 기록(GET /runs/{id}/ghost의 1초 칸 속도)을 "내가 달린 시간"에 맞춰 다시 재생.
    // 위치는 내가 출발한 자리부터의 거리라 한 바퀴를 넘어도 나와의 앞뒤가 헷갈리지 않는다(같은 출발선, 같은 시계).
    // 내가 멈추면(일시정지·다시 걷기 전) 내 달린 시간이 멈추므로 고스트도 멈춘다. 기록이 끝나면 그 자리에 서 있음(정할 것 3 가)
    public sealed class GhostTrack
    {
        readonly float[] cum;   // cum[i] = i칸 뒤 거리(m), cum[0] = 0
        readonly double interval;

        public float TotalM { get; }
        public double DurationS { get; }

        GhostTrack(float[] cum, double interval)
        {
            this.cum = cum;
            this.interval = interval;
            TotalM = cum[cum.Length - 1];
            DurationS = (cum.Length - 1) * interval;
        }

        // 속도 칸을 거리로 쌓고, 끝이 기록의 공식 거리(인증 완료면 계기판)·공식 시간과 같게 맞춘다(목록·요약에 보인 "5.1km · 31:20"에서 끝나게).
        // 시간도 맞추는 건 칸 수 × 간격이 공식 시간과 다를 수 있어서(못 올린 60초 묶음 등): 칸 모양(빨라지고 느려진 곳)은 두고 길이만 늘이고 줄임.
        // 칸이 없거나 거리가 0이면(옛 기록·시계열 없음) 공식 거리·시간의 일정한 페이스로
        public static GhostTrack From(int distanceM, int durationS, double intervalS, IReadOnlyList<float> speedsKmh)
        {
            if (distanceM <= 0 || durationS <= 0) return null;
            if (speedsKmh == null || speedsKmh.Count == 0 || intervalS <= 0) return Even(distanceM, durationS);
            var cum = new float[speedsKmh.Count + 1];
            for (int i = 0; i < speedsKmh.Count; i++)
                cum[i + 1] = cum[i] + Math.Max(0f, speedsKmh[i]) / 3.6f * (float)intervalS;
            float raw = cum[cum.Length - 1];
            if (raw <= 0f) return Even(distanceM, durationS);
            float scale = distanceM / raw;
            for (int i = 1; i < cum.Length; i++) cum[i] *= scale;
            return new GhostTrack(cum, (double)durationS / speedsKmh.Count);
        }

        static GhostTrack Even(int distanceM, int durationS) => new GhostTrack(new[] { 0f, (float)distanceM }, durationS);

        public bool FinishedAt(double t) => t >= DurationS;

        public float DistanceAt(double t)
        {
            if (t <= 0) return 0f;
            if (t >= DurationS) return TotalM;
            double pos = t / interval;
            int i = (int)pos;
            float f = (float)(pos - i);
            return cum[i] + (cum[i + 1] - cum[i]) * f;
        }

        // 아바타 걸음 속도용(km/h). 끝났으면 0
        public float SpeedKmhAt(double t)
        {
            if (t < 0 || t >= DurationS) return 0f;
            int i = (int)(t / interval);
            return (float)((cum[i + 1] - cum[i]) / interval * 3.6);
        }
    }

    // 이번 달리기의 고스트: 기록 + 내가 출발한 섬 거리(같은 자리에서 같이 출발)
    public sealed class GhostRun
    {
        public GhostTrack Track { get; }
        public float StartIslandM { get; }

        public GhostRun(GhostTrack track, float startIslandM)
        {
            Track = track ?? throw new ArgumentNullException(nameof(track));
            StartIslandM = startIslandM;
        }

        // 섬에 놓을 자리(내 누적 거리와 같은 기준). moving이 아니면(멈춤) 걸음 0
        public GhostPose PoseAt(double elapsedSec, bool moving) =>
            new GhostPose(StartIslandM + Track.DistanceAt(elapsedSec), moving ? Track.SpeedKmhAt(elapsedSec) : 0f);
    }

    public readonly struct GhostPose
    {
        public readonly float IslandDistanceM, SpeedKmh;

        public GhostPose(float islandDistanceM, float speedKmh)
        {
            IslandDistanceM = islandDistanceM;
            SpeedKmh = speedKmh;
        }
    }

    // 고스트 글자(Figma D-01–D-04·D-13). 임시라고 적은 것은 상준 확인
    public static class GhostText
    {
        public const string Name = "어제의 나";                                   // Figma D-13 이름표·간격 줄
        public const string CardTitle = "고스트러너";
        public const string CardLead = "어제의 나와 함께 달려요. 나에게만 보여요.";
        public const string OffNote = "고스트 없이도 입장할 수 있어요";
        public const string PickTitle = "함께 달릴 내 기록";
        public const string PickDone = "선택 완료";
        public const string EmptyTitle = "아직 고스트로 쓸\n기록이 없어요";
        public const string EmptyBody = "첫 러닝을 마치면 어제의 나와 함께 달릴 수 있어요";
        public const string EmptyEnter = "고스트 없이 입장";
        public const string LoadFailed = "고스트 기록을 불러오지 못해 혼자 달려요"; // 임시

        // D-02 요약 둘째 줄 "5.1km · 31:20"
        public static string Summary(RunSummary r) => FriendText.KmShort(r.DistanceM) + " · " + FriendText.Duration(r.DurationS);

        // D-03 줄 아래 "31:20 · 평균 6'08"/km"
        public static string Detail(RunSummary r)
        {
            var pace = FriendText.Pace(r.AvgPaceSPerKm);
            return FriendText.Duration(r.DurationS) + (pace != null ? " · 평균 " + pace : "");
        }

        // D-13 배너. 앞서면 "+42m 앞서요", 뒤처지면 몇 m 뒤인지 분명히(11/18 민재), 고스트가 끝났으면 멈춘 곳 기준(정할 것 3 가)
        public static string Banner(float myM, float ghostM, bool ghostFinished)
        {
            float gap = myM - ghostM;
            if (ghostFinished)
                return gap >= 0f ? $"어제의 나보다 +{Meters(gap)} 더 달렸어요" : $"어제의 나는 {Meters(-gap)} 앞에서 멈췄어요";
            if (Math.Abs(gap) < 1f) return "어제의 나와 나란히 달려요";
            return gap > 0f ? $"어제의 나보다 +{Meters(gap)} 앞서요" : $"어제의 나보다 {Meters(-gap)} 뒤처져요";
        }

        // 42m, 1 km부터 1.2km
        public static string Meters(float m) =>
            m < 1000f ? $"{(int)Math.Floor(m)}m" : (Math.Floor(m / 100.0) / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "km";
    }
}
