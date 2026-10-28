using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Scrun.SpeedLab
{
    // 러닝머신 속도 실험: 걸음 진동(StepSpeed)·모터 주파수(FreqSpeed) 추정, 보정, 기록.
    // 나중에 RunnerController에 그대로 꽂을 수 있게 ISpeedSource.
    // 기록 폴더(persistentDataPath/SpeedLab/<시각>/): acc.csv 원본, est.csv 추정값, events.csv 실제 속도·보정
    [RequireComponent(typeof(TreadmillSensor))]
    public class SpeedLab : MonoBehaviour, ISpeedSource
    {
        public enum Method { Fused, Step, Freq }

        [Serializable]
        public class CalPoint
        {
            public float kmh, cad, q, impact;
            public int fs;
            public float binHz;
            public float[] peak;
        }

        [Serializable]
        class CalFile { public List<CalPoint> points = new List<CalPoint>(); }

        public const float CountdownS = 5f, CollectS = 20f, Tick = 0.25f;
        public const int History = 360;   // 90초

        public TreadmillSensor Sensor { get; private set; }
        public StepSpeed Steps { get; private set; }
        public FreqSpeed Freq { get; private set; }

        public float SpeedKmh => Shown;
        public Method Show = Method.Fused;
        public float StepWindow = 4f;
        public float FreqConfMin = 0.55f;   // 우리 러닝머신 ≥ 0.75, 옆 기계·벨트 정지 ≈ 0.4 (10/28 헬스장 기록)

        public float TrueKmh { get; private set; } = 8f;
        public float StepKmh, Cad, Q, Impact, FreqKmh, FreqConf, Fused;
        public bool StepOk, FreqOk;
        public float Shown => Show == Method.Step ? StepKmh : Show == Method.Freq ? FreqKmh : Fused;

        public List<CalPoint> Points => cal.points;
        public StepSpeed.Model StepModel { get; private set; }
        public StepSpeed.ImpactModel ImpModel { get; private set; }
        public StepSpeed.ImpCadModel ImpCadModel { get; private set; }
        public float ImpKmh, ImpCadKmh;   // 두 모델의 순간값(기록·비교용)
        public List<FreqSpeed.Line> Lines => Freq != null ? Freq.Lines : NoLines;
        static readonly List<FreqSpeed.Line> NoLines = new List<FreqSpeed.Line>();

        // 보정 진행: 0 = 없음, 1 = 준비(카운트다운), 2 = 수집
        public int CalState { get; private set; }
        public float CalLeft { get; private set; }
        public string Message = "";

        // 그래프용 기록 (Tick마다 하나)
        public readonly float[] HTrue = new float[History], HStep = new float[History], HFreq = new float[History], HFused = new float[History];
        public int HHead;
        public event Action<float[]> SpectrumColumn;   // 폭포 그림용

        CalFile cal = new CalFile();
        string calPath;
        StreamWriter est, events;
        float tick;
        readonly float[] lastSteps = new float[3];
        int lastStepN;
        float[] calSum;
        int calSumN;
        AudioSource beep;
        AudioClip beepShort, beepLong, beepChange;

        void Awake()
        {
            Sensor = GetComponent<TreadmillSensor>();
            Sensor.Sample += OnSample;
            calPath = Path.Combine(Application.persistentDataPath, "speedlab_calib.json");
            if (File.Exists(calPath))
            {
                try { cal = JsonUtility.FromJson<CalFile>(File.ReadAllText(calPath)) ?? new CalFile(); }
                catch (Exception) { cal = new CalFile(); }
            }
            beep = gameObject.AddComponent<AudioSource>();
            beepShort = Tone(880, 0.12f); beepLong = Tone(1320, 0.5f); beepChange = Tone(587, 0.35f);
        }

        void Start() => OpenLogs();

        static AudioClip Tone(float hz, float len)
        {
            int n = (int)(44100 * len);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = 0.5f * Mathf.Sin(2 * Mathf.PI * hz * i / 44100f) * Mathf.Min(1f, (n - i) / 2000f);
            var c = AudioClip.Create("beep", n, 1, 44100, false);
            c.SetData(d, 0);
            return c;
        }

        void OpenLogs()
        {
            est?.Dispose(); events?.Dispose();
            est = new StreamWriter(Path.Combine(Sensor.SessionDir, "est.csv"), false, new System.Text.UTF8Encoding(false));
            est.WriteLine("t,true_kmh,step_kmh,cad,q,freq_kmh,freq_conf,fused_kmh,shown,impact,imp_kmh,impcad_kmh");
            events = new StreamWriter(Path.Combine(Sensor.SessionDir, "events.csv"), false, new System.Text.UTF8Encoding(false));
            events.WriteLine("t,event,value");
            Log("true_kmh", TrueKmh);
        }

        public void NewSession()
        {
            Sensor.NewSession();
            OpenLogs();
            Message = "새 기록: " + Path.GetFileName(Sensor.SessionDir);
        }

        void Log(string ev, float v)
        {
            events?.WriteLine(Sensor.Now.ToString("0.000", CultureInfo.InvariantCulture) + "," + ev + "," + v.ToString("0.###", CultureInfo.InvariantCulture));
            events?.Flush();
        }

        public void SetTrue(float kmh)
        {
            TrueKmh = Mathf.Clamp(Mathf.Round(kmh * 10f) / 10f, 0f, 25f);
            Log("true_kmh", TrueKmh);
        }

        void OnSample(double t, float x, float y, float z)
        {
            if (Steps == null)
            {
                Steps = new StepSpeed(Sensor.Fs);
                Freq = new FreqSpeed(Sensor.Fs);
                Refit();
            }
            Steps.Add(x, y, z);
            Freq.Add(x, y, z);
        }

        void Update()
        {
#if UNITY_EDITOR
            Sensor.FakeBeltKmh = TrueKmh;
#endif
            if (Steps == null) return;

            if (Freq.Fresh)
            {
                Freq.Fresh = false;
                (FreqKmh, FreqConf) = Freq.Estimate();
                FreqOk = Lines.Count > 0 && FreqConf >= FreqConfMin;
                if (!FreqOk) FreqKmh = Lines.Count > 0 ? FreqKmh : 0;
                SpectrumColumn?.Invoke(Freq.Peak);
                if (CalState == 2)
                {
                    calSum ??= new float[Freq.Bins];
                    for (int k = 0; k < Freq.Bins; k++) calSum[k] += Freq.Peak[k];
                    calSumN++;
                }
            }

            tick += Time.unscaledDeltaTime;
            if (tick >= Tick)
            {
                tick -= Tick;
                var (c, q, _) = Steps.Cadence(StepWindow);
                Cad = (float)c; Q = (float)q;
                Impact = (float)Steps.Impact(StepWindow);
                // 충격 + 걸음 수(보정 4구간 이상) → 충격만 → 걸음 수 × 보폭 순으로 씀. 10/28 PC 재계산에서 충격 + 걸음 수가 평균 오차 가장 작음
                StepOk = q >= 0.3 && c > 0 && (ImpCadModel.ok || ImpModel.ok || StepModel.ok);
                ImpKmh = StepOk && ImpModel.ok ? (float)StepSpeed.Speed(ImpModel, Impact) : 0f;
                ImpCadKmh = StepOk && ImpCadModel.ok ? (float)StepSpeed.Speed(ImpCadModel, Impact, c) : 0f;
                float raw = !StepOk ? 0f : ImpCadModel.ok ? ImpCadKmh : ImpModel.ok ? ImpKmh : (float)StepSpeed.Speed(StepModel, c);
                lastSteps[lastStepN++ % 3] = raw;
                StepKmh = StepOk ? Median3() : 0f;
                // 달리는 중(걸음이 잡힘)이면 걸음·충격: 발 충격이 모터 진동을 1000배 덮어 주파수가 틀림(10/28 기록)
                // 사람이 없을 때(벨트만)는 주파수가 ±0.05 km/h
                Fused = StepOk ? StepKmh : FreqOk ? FreqKmh : 0f;

                HTrue[HHead] = TrueKmh; HStep[HHead] = StepOk ? StepKmh : float.NaN;
                HFreq[HHead] = FreqOk ? FreqKmh : float.NaN; HFused[HHead] = Fused;
                HHead = (HHead + 1) % History;

                var ic = CultureInfo.InvariantCulture;
                est.WriteLine(string.Join(",", Sensor.Now.ToString("0.000", ic), TrueKmh.ToString("0.0", ic),
                    StepKmh.ToString("0.00", ic), Cad.ToString("0.0", ic), Q.ToString("0.00", ic),
                    FreqKmh.ToString("0.00", ic), FreqConf.ToString("0.00", ic), Fused.ToString("0.00", ic), Show.ToString(), Impact.ToString("0.000", ic),
                    ImpKmh.ToString("0.00", ic), ImpCadKmh.ToString("0.00", ic)));
                if (HHead % 8 == 0) est.Flush();
            }

            UpdateCalibration();
            UpdateGuide();
        }

        float Median3()
        {
            int n = Mathf.Min(lastStepN, 3);
            var a = new List<float>();
            for (int i = 0; i < n; i++) if (lastSteps[i] > 0) a.Add(lastSteps[i]);
            if (a.Count == 0) return 0;
            a.Sort();
            return a[a.Count / 2];
        }

        // ── 보정 ─────────────────────────────────────

        public void StartCalibration()
        {
            if (Steps == null) { Message = "센서 준비 중"; return; }
            if (Guide != GuideMode.None) return;
            if (CalState != 0) { CalState = 0; Message = "보정 취소"; Log("calib_cancel", TrueKmh); return; }
            CalState = 1; CalLeft = CountdownS; Message = "";
            Log("calib_ready", TrueKmh);
        }

        void UpdateCalibration()
        {
            if (CalState == 0) return;
            float before = CalLeft;
            CalLeft -= Time.unscaledDeltaTime;
            if (CalState == 1)
            {
                if (Mathf.Ceil(CalLeft) != Mathf.Ceil(before)) beep.PlayOneShot(beepShort);
                if (CalLeft <= 0) { CalState = 2; CalLeft = CollectS; calSum = null; calSumN = 0; Log("calib_start", TrueKmh); }
                return;
            }
            if (CalLeft > 0) return;
            CalState = 0;
            beep.PlayOneShot(beepLong);
            var p = MakePoint(TrueKmh, CollectS, true);
            cal.points.RemoveAll(o => Mathf.Abs(o.kmh - p.kmh) < 0.05f);
            cal.points.Add(p);
            cal.points.Sort((a, b) => a.kmh.CompareTo(b.kmh));
            Log("calib_done", TrueKmh);
            Save();
            Refit();
            Message = $"{p.kmh:0.0} km/h 보정: 걸음 {(p.q >= 0.3f ? $"{p.cad:0} spm" : "없음(벨트만)")}, 주파수 줄 {Lines.Count}개";
        }

        // 최근 seconds초로 보정점 하나. withPeak = 주파수 스펙트럼도(벨트만 보정용)
        CalPoint MakePoint(float kmh, float seconds, bool withPeak)
        {
            var (c, q, _) = Steps.Cadence(seconds);
            var p = new CalPoint { kmh = kmh, cad = (float)c, q = (float)q, impact = (float)Steps.Impact(seconds), fs = Sensor.Fs, binHz = Freq.BinHz };
            if (withPeak && calSum != null && calSumN > 0)
            {
                p.peak = new float[calSum.Length];
                for (int k = 0; k < calSum.Length; k++) p.peak[k] = calSum[k] / calSumN;
            }
            return p;
        }

        // ── 안내 모드 (계획 0003, ADR-0005) ─────────────
        // 앱이 "N km/h로 맞추세요" → 맞추는 시간 → 기록 → 다음 속도. 실제 속도 버튼을 누르지 않는다.
        // 보정: 끝나면 달리기 보정점을 새 것으로 바꿈(벨트만 점은 둠). 검증·걷기: 기록과 events만 남김(PC에서 오차·걷기 모델, 계획 0004)
        // events: guide_mode(1 보정, 2 검증, 3 걷기), guide_set(맞추라고 안내), guide_start·guide_end(기록 구간), guide_done·guide_cancel

        public enum GuideMode { None, Calib, Test, Walk }
        public static readonly float[] CalibPlan = { 7, 8, 9, 10, 9.5f, 8.5f, 7.5f };
        public static readonly float[] TestPlan = { 7, 8, 9, 10, 9, 8, 7, 10, 9, 8, 7, 8, 9, 10 }; // 올림·내림 피라미드 두 번: 같은 속도를 양방향에서
        public static readonly float[] WalkPlan = { 1, 2, 3, 4, 5, 6, 5, 4, 3, 2, 1 };
        // 검증은 3 km/h씩 뛰는 구간이 있어 맞추는 시간을 더 줌
        const float SegS = 20f, SettleS = 5f, TestSettleS = 10f, ShortSegS = 5f, ShortSettleS = 3f;

        public bool GuideShort;   // 짧게: 화면·기록 확인용
        public GuideMode Guide { get; private set; }
        public int GuideStep { get; private set; }
        public bool GuideSettling { get; private set; }
        public float GuideLeft { get; private set; }
        public float[] GuidePlan => Guide == GuideMode.Test ? TestPlan : Guide == GuideMode.Walk ? WalkPlan : CalibPlan;
        public float GuideSegS => GuideShort ? ShortSegS : SegS;
        float GuideSettleS => GuideShort ? ShortSettleS : Guide == GuideMode.Test ? TestSettleS : SettleS;
        readonly List<CalPoint> guidePoints = new List<CalPoint>();

        public void StartGuide(GuideMode mode)
        {
            if (Steps == null) { Message = "센서 준비 중"; return; }
            if (Guide != GuideMode.None)
            {
                Log("guide_cancel", GuideStep);
                Guide = GuideMode.None;
                Message = "안내 중지 (보정은 바뀌지 않음)";
                return;
            }
            if (CalState != 0) { CalState = 0; Log("calib_cancel", TrueKmh); }
            Guide = mode;
            GuideStep = 0;
            guidePoints.Clear();
            Log("guide_mode", (int)mode);
            BeginGuideStep();
        }

        void BeginGuideStep()
        {
            float v = GuidePlan[GuideStep];
            SetTrue(v);
            Log("guide_set", v);
            GuideSettling = true;
            GuideLeft = GuideSettleS;
            beep.PlayOneShot(beepChange);
            Message = "";
        }

        void UpdateGuide()
        {
            if (Guide == GuideMode.None) return;
            float before = GuideLeft;
            GuideLeft -= Time.unscaledDeltaTime;
            float v = GuidePlan[GuideStep];
            if (GuideSettling)
            {
                if (GuideLeft <= 3 && Mathf.Ceil(GuideLeft) != Mathf.Ceil(before)) beep.PlayOneShot(beepShort);
                if (GuideLeft > 0) return;
                GuideSettling = false;
                GuideLeft = GuideSegS;
                Log("guide_start", v);
                return;
            }
            if (GuideLeft > 0) return;
            Log("guide_end", v);
            if (Guide == GuideMode.Calib) guidePoints.Add(MakePoint(v, GuideSegS, false));
            if (++GuideStep < GuidePlan.Length) { BeginGuideStep(); return; }
            FinishGuide();
        }

        void FinishGuide()
        {
            beep.PlayOneShot(beepLong);
            var mode = Guide;
            int steps = GuidePlan.Length;
            Guide = GuideMode.None;
            if (mode != GuideMode.Calib)
            {
                Log("guide_done", steps);
                Message = (mode == GuideMode.Walk ? "걷기" : "검증") + " 기록 끝: " + Path.GetFileName(Sensor.SessionDir);
                return;
            }
            // 걸음이 안 잡힌 구간(안 뛰었거나 검출 실패)은 버림
            var ok = guidePoints.FindAll(p => p.q >= 0.3f && p.impact > 0);
            Log("guide_done", ok.Count);
            if (ok.Count < 2) { Message = $"보정 실패: 걸음이 잡힌 구간 {ok.Count}개 (2개 이상 필요), 기존 보정 유지"; return; }
            cal.points.RemoveAll(p => p.q >= 0.3f);
            cal.points.AddRange(ok);
            cal.points.Sort((a, b) => a.kmh.CompareTo(b.kmh));
            Save();
            Refit();
            Message = $"안내 보정 끝: {ok.Count}/{CalibPlan.Length}구간 사용" + (ModelText.Length > 0 ? ", " + ModelText : "");
            // 빠를수록 충격이 세야 정상(10/28 기록). 반대면 속도를 잘못 맞췄거나 거치가 흔들린 것
            if (ImpModel.ok && ImpModel.b <= 0) Message += "\n경고: 빠를수록 충격이 약하게 나옴 → 보정 다시 권장";
        }

        public void DeleteLast()
        {
            if (cal.points.Count == 0) return;
            cal.points.RemoveAt(cal.points.Count - 1);
            Save(); Refit();
        }

        public void ClearAll()
        {
            cal.points.Clear();
            Save(); Refit();
        }

        void Save()
        {
            File.WriteAllText(calPath, JsonUtility.ToJson(cal));
            File.WriteAllText(Path.Combine(Sensor.SessionDir, "calib.json"), JsonUtility.ToJson(cal));
        }

        // 지금 실시간 속도에 쓰는 식
        public string ModelText =>
            ImpCadModel.ok ? $"속도 = {ImpCadModel.a:0.00} + {ImpCadModel.b:0.00}×ln(충격) + {ImpCadModel.c:0.0000}×spm"
            : ImpModel.ok ? $"속도 = {ImpModel.a:0.00} + {ImpModel.b:0.00}×ln(충격)"
            : StepModel.ok ? $"보폭 = {StepModel.a:0.000} + {StepModel.b:0.0000}×spm" : "";

        void Refit()
        {
            var sp = new List<(double, double)>();
            var fp = new List<(float, float[])>();
            var ip = new List<(double, double)>();
            var icp = new List<(double, double, double)>();
            foreach (var p in cal.points)
            {
                if (p.q >= 0.3f && p.cad > 0) sp.Add((p.kmh, p.cad));
                if (p.q >= 0.3f && p.impact > 0) ip.Add((p.kmh, p.impact));
                if (p.q >= 0.3f && p.impact > 0 && p.cad > 0) icp.Add((p.kmh, p.impact, p.cad));
                if (Freq != null && p.peak != null && p.fs == Sensor.Fs && p.peak.Length == Freq.Bins) fp.Add((p.kmh, p.peak));
            }
            StepModel = StepSpeed.Fit(sp);
            ImpModel = StepSpeed.FitImpact(ip);
            ImpCadModel = StepSpeed.FitImpactCad(icp);
            Freq?.Fit(fp);
        }

        void OnDestroy()
        {
            est?.Dispose(); events?.Dispose();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) { est?.Flush(); events?.Flush(); }
        }
    }
}
