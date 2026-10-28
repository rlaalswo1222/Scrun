using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Scrun.SpeedLab
{
    // 폰 가속도(안드로이드 = ScrunAccel.java 고속 수집, 에디터 = 가짜 신호)를 받아
    // 균일 간격(Fs)으로 보간해 Sample 이벤트로 흘려보낸다. 원본 샘플은 기록 폴더 acc.csv에 남긴다.
    public class TreadmillSensor : MonoBehaviour
    {
        public event Action<double, float, float, float> Sample;   // (시각 초, x, y, z) 균일 간격

        public int Fs { get; private set; }          // 0이면 아직 측정 중
        public float RawRate { get; private set; }   // 실제 들어오는 속도(초당 샘플)
        public long Dropped { get; private set; }
        public string Info { get; private set; } = "";
        public double Now { get; private set; }
        public string SessionDir { get; private set; }

        // 에디터 가짜 신호: 벨트 속도와 사람이 달리는지
        public float FakeBeltKmh;
        public bool FakeRunning = true;

        AndroidJavaClass java;
        StreamWriter raw;
        double nextT = -1, lastT, rateT0;
        Vector3 lastV;
        int rateN;
        double[] warm = new double[4 * 4096];
        int warmN;
        float flushT;

        // 가짜 신호 상태
        double fakeT, fakeStepT, fakeStepAmp;
        System.Random rng = new System.Random(1);

        void Awake() => NewSession();   // SpeedLab.Start가 기록 폴더를 쓰므로 먼저

        void Start()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
#if UNITY_ANDROID && !UNITY_EDITOR
            java = new AndroidJavaClass("com.scrun.sensor.ScrunAccel");
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = player.GetStatic<AndroidJavaObject>("currentActivity"))
                java.CallStatic<bool>("start", act);
            Info = java.CallStatic<string>("info");
#else
            Info = "에디터 가짜 신호";
#endif
        }

        public void NewSession()
        {
            raw?.Dispose();
            SessionDir = Path.Combine(Application.persistentDataPath, "SpeedLab", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(SessionDir);
            raw = new StreamWriter(Path.Combine(SessionDir, "acc.csv"), false, new System.Text.UTF8Encoding(false), 1 << 16);
            raw.WriteLine("t,x,y,z");
        }

        void Update()
        {
            double[] d;
#if UNITY_ANDROID && !UNITY_EDITOR
            d = java.CallStatic<double[]>("drain");
            Dropped = java.CallStatic<long>("dropped");
#else
            d = Fake(Time.unscaledTimeAsDouble);
#endif
            for (int i = 0; i + 3 < d.Length; i += 4)
                Push(d[i], (float)d[i + 1], (float)d[i + 2], (float)d[i + 3]);

            flushT += Time.unscaledDeltaTime;
            if (flushT > 1f) { flushT = 0f; raw.Flush(); }
        }

        void Push(double t, float x, float y, float z)
        {
            raw.Write(t.ToString("0.00000", CultureInfo.InvariantCulture));
            raw.Write(','); raw.Write(x.ToString("0.0000", CultureInfo.InvariantCulture));
            raw.Write(','); raw.Write(y.ToString("0.0000", CultureInfo.InvariantCulture));
            raw.Write(','); raw.Write(z.ToString("0.0000", CultureInfo.InvariantCulture));
            raw.WriteLine();

            // 들어오는 속도 (2초마다 갱신)
            if (rateN == 0) rateT0 = t;
            rateN++;
            if (t - rateT0 >= 2.0) { RawRate = (float)((rateN - 1) / (t - rateT0)); rateN = 0; }

            var v = new Vector3(x, y, z);
            if (Fs == 0)
            {
                // 처음 1.5초로 속도를 재고 균일 간격을 정한다
                if (warmN < warm.Length / 4) { warm[warmN * 4] = t; warmN++; }
                if (warmN > 10 && t - warm[0] >= 1.5)
                {
                    double r = (warmN - 1) / (t - warm[0]);
                    Fs = r >= 360 ? 400 : r >= 180 ? 200 : 100;
                    nextT = t;
                }
                lastT = t; lastV = v;
                return;
            }
            if (t - lastT > 0.1) nextT = t;   // 끊김: 이어 붙이지 않고 새로 시작
            double dt = 1.0 / Fs;
            while (nextT <= t)
            {
                float a = t > lastT ? (float)((nextT - lastT) / (t - lastT)) : 1f;
                var u = Vector3.LerpUnclamped(lastV, v, Mathf.Clamp01(a));
                Now = nextT;
                Sample?.Invoke(nextT, u.x, u.y, u.z);
                nextT += dt;
            }
            lastT = t; lastV = v;
        }

        // 에디터용: 420Hz 근처(흔들리는 간격), 롤러 1.6·v Hz + 2배음, 모터 4.3·v Hz, 걸음 충격
        double[] Fake(double now)
        {
            if (fakeT == 0) fakeT = now;
            int n = Math.Max(0, (int)((now - fakeT) * 420));
            var d = new double[n * 4];
            for (int i = 0; i < n; i++)
            {
                fakeT += 1.0 / 420 * (0.9 + 0.2 * rng.NextDouble());
                double t = fakeT, v = FakeBeltKmh;
                double ax = 0.15 * Math.Sin(2 * Math.PI * 1.6 * v * t) + 0.08 * Math.Sin(2 * Math.PI * 3.2 * v * t);
                double ay = 9.81 + 0.1 * Math.Sin(2 * Math.PI * 4.3 * v * t + 1);
                double az = 0;
                if (FakeRunning && v >= 4)
                {
                    double cad = 150 + 4 * (v - 8);   // 분당 걸음
                    if (t >= fakeStepT) { fakeStepT = t + 60 / cad * (0.97 + 0.06 * rng.NextDouble()); fakeStepAmp = 2.5 + rng.NextDouble(); }
                    double since = t - (fakeStepT - 60 / cad);
                    double burst = fakeStepAmp * Math.Exp(-since * 40) * Math.Sin(2 * Math.PI * 28 * since);
                    az += burst; ay += 0.6 * burst + 0.8 * Math.Sin(2 * Math.PI * cad / 60 * t);
                }
                ax += 0.05 * (rng.NextDouble() - 0.5); ay += 0.05 * (rng.NextDouble() - 0.5); az += 0.05 * (rng.NextDouble() - 0.5);
                d[i * 4] = t; d[i * 4 + 1] = ax; d[i * 4 + 2] = ay; d[i * 4 + 3] = az;
            }
            return d;
        }

        void OnDestroy()
        {
            raw?.Dispose();
#if UNITY_ANDROID && !UNITY_EDITOR
            java?.CallStatic("stop");
#endif
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) raw?.Flush();
        }
    }
}
