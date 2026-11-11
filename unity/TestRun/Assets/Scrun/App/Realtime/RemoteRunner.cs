using System;

namespace Scrun.App
{
    // 같은 맵의 다른 러너 한 명(실시간 방에서 받은 것). 위치는 받은 d에서 v × 지난 시간만큼 앞으로 내다보고,
    // 새 값이 오면 지금 보이는 자리와의 차이를 CorrectSec에 걸쳐 줄여 순간이동 없이 맞춘다(계획 0029, 가설: 지연 버퍼보다 낫다).
    // 시각은 앱 시계(초, RealtimeClock.Now). 서버 시각과 맞출 필요 없음(받은 순간 기준).
    public sealed class RemoteRunner
    {
        public const double CorrectSec = 0.5;  // 새 값으로 맞추는 시간
        public const float SnapM = 25f;        // 차이가 이보다 크면 바로 옮김(오래 끊겼다 붙음 등)
        public const double MaxPredictSec = 30; // 새 값이 이만큼 안 오면 더 내다보지 않음(서버가 끊긴 사람을 30초 남김)

        readonly float lap;
        double anchorD, anchorT; // 마지막으로 받은 값과 받은 시각
        float lastD = float.NaN, lastV = float.NaN;
        double correction, correctionT;

        public string UserId { get; }
        public string Nickname { get; }
        public float SpeedKmh { get; private set; }
        public bool Running { get; private set; }

        public RemoteRunner(string userId, string nickname, float lapM)
        {
            UserId = userId ?? throw new ArgumentNullException(nameof(userId));
            Nickname = string.IsNullOrEmpty(nickname) ? "러너" : nickname;
            lap = lapM;
        }

        public void Sample(float d, float v, bool running, double now)
        {
            if (float.IsNaN(d) || float.IsNaN(v)) return;
            Running = running;
            // 같은 값이 다시 오면(서버가 끊긴 사람의 마지막 값을 계속 보냄) 기준을 옮기지 않고 계속 내다봄
            if (d == lastD && v == lastV) return;
            bool first = float.IsNaN(lastD);
            float shown = first ? 0f : PositionAt(now);
            lastD = d;
            lastV = v;
            anchorD = d;
            anchorT = now;
            SpeedKmh = v;
            if (first)
            {
                correction = 0;
                return;
            }
            float diff = LoopMath.Delta(LoopMath.Wrap(d, lap), shown, lap);
            correction = Math.Abs(diff) > SnapM ? 0 : diff;
            correctionT = now;
        }

        public float PositionAt(double now)
        {
            double dt = Math.Min(Math.Max(0, now - anchorT), MaxPredictSec);
            double predicted = anchorD + SpeedKmh / 3.6 * dt;
            double left = 1 - (now - correctionT) / CorrectSec;
            if (left > 0) predicted += correction * Math.Min(1, left);
            return LoopMath.Wrap((float)predicted, lap);
        }
    }
}
