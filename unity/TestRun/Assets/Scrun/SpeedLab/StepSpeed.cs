using System;
using System.Collections.Generic;

namespace Scrun.SpeedLab
{
    // 걸음 진동 → 속도. speedlab/dsp.py의 impact 방식과 같다:
    // 8Hz 이상 떨림의 세기(포락선) → 자기상관으로 한 걸음 주기 → 케이던스, 보폭 = a + b·케이던스 (보정으로 맞춤)
    public class StepSpeed
    {
        public const int EnvFs = 100;              // 포락선 샘플링
        const double CadMin = 70, CadMax = 220;

        readonly int decim;
        Butter4 hx, hy, hz, lp, lp30;
        readonly float[] env = new float[EnvFs * 40];   // 최근 40초
        readonly float[] imp = new float[EnvFs * 40];   // 충격 세기(30Hz 포락선, 칸마다 최대)
        int envHead, envCount, phase;
        float blockMax;

        public StepSpeed(int fs)
        {
            decim = Math.Max(1, fs / EnvFs);
            hx = new Butter4(true, 8, fs); hy = new Butter4(true, 8, fs); hz = new Butter4(true, 8, fs);
            lp = new Butter4(false, 5, fs);
            lp30 = new Butter4(false, 30, fs);
        }

        public int Count => envCount;

        public void Add(float x, float y, float z)
        {
            double a = hx.Step(x), b = hy.Step(y), c = hz.Step(z);
            double m = Math.Sqrt(a * a + b * b + c * c);
            double e = lp.Step(m);
            blockMax = Math.Max(blockMax, (float)lp30.Step(m));
            if (++phase < decim) return;
            phase = 0;
            env[envHead] = (float)e;
            imp[envHead] = blockMax; blockMax = 0;
            envHead = (envHead + 1) % env.Length;
            if (envCount < env.Length) envCount++;
        }

        // 최근 seconds초 → (케이던스 spm, 신뢰도 0~1, 평균 세기)
        public (double cad, double q, double level) Cadence(double seconds)
        {
            int n = Math.Min(envCount, (int)(seconds * EnvFs));
            if (n < EnvFs * 2) return (0, 0, 0);
            var s = new double[n];
            double mean = 0;
            for (int i = 0; i < n; i++) { s[i] = env[(envHead - n + i + env.Length) % env.Length]; mean += s[i]; }
            mean /= n;
            for (int i = 0; i < n; i++) s[i] -= mean;

            int lo = (int)Math.Floor(EnvFs * 60 / CadMax), hi = (int)Math.Ceiling(EnvFs * 60 / CadMin);
            hi = Math.Min(hi + 2, n / 2);
            var r = new double[hi + 2];
            double r0 = 0;
            for (int i = 0; i < n; i++) r0 += s[i] * s[i];
            r0 /= n;
            if (r0 <= 1e-12) return (0, 0, mean);
            for (int k = lo - 1; k <= hi + 1 && k < n; k++)
            {
                double acc = 0;
                for (int i = 0; i + k < n; i++) acc += s[i] * s[i + k];
                r[k] = acc / (n - k) / r0;
            }
            int best = lo;
            for (int k = lo; k <= hi; k++) if (r[k] > r[best]) best = k;
            // 두 걸음·세 걸음 주기를 한 걸음으로 착각하지 않게: 1/3, 1/2 지점에도 봉우리가 있으면 그게 한 걸음
            foreach (int m in new[] { 3, 2 })
            {
                double c = (double)best / m;
                if (c < lo) continue;
                int a = Math.Max(lo, (int)c - 2), b = Math.Min(hi, (int)c + 3), kh = a;
                for (int k = a; k <= b; k++) if (r[k] > r[kh]) kh = k;
                if (r[kh] > 0.1) { best = kh; break; }
            }
            double d = 0;
            if (best > lo && best < hi)
            {
                double y0 = r[best - 1], y1 = r[best], y2 = r[best + 1], den = y0 - 2 * y1 + y2;
                if (den != 0) d = Math.Max(-0.5, Math.Min(0.5, 0.5 * (y0 - y2) / den));
            }
            return (60.0 / ((best + d) / EnvFs), Math.Max(0, r[best]), mean);
        }

        // 최근 seconds초 발 충격 세기 = 충격 봉우리(±0.12초 안 최대, 상위 25%) 높이의 중앙값
        // 이 사용자는 속도를 올려도 걸음 수가 거의 같고(176/177 spm) 충격이 세짐 → 속도 단서 (10/28 기록)
        public double Impact(double seconds)
        {
            int n = Math.Min(envCount, (int)(seconds * EnvFs));
            if (n < EnvFs * 2) return 0;
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = imp[(envHead - n + i + imp.Length) % imp.Length];
            var sorted = (float[])s.Clone();
            Array.Sort(sorted);
            float thr = sorted[(int)(0.75 * (n - 1))];
            var peaks = new List<float>();
            const int r = 12;
            for (int i = r; i < n - r; i++)
            {
                if (s[i] < thr) continue;
                bool top = true;
                for (int j = i - r; j <= i + r && top; j++) if (s[j] > s[i] || (s[j] == s[i] && j < i)) top = false;
                if (top) peaks.Add(s[i]);
            }
            if (peaks.Count == 0) return 0;
            peaks.Sort();
            return peaks[peaks.Count / 2];
        }

        // 충격 모델: 속도 = a + b·ln(충격 세기)
        public struct ImpactModel { public bool ok; public double a, b, vmin, vmax; public int n; }

        public static ImpactModel FitImpact(List<(double kmh, double impact)> pts)
        {
            var m = new ImpactModel { n = pts.Count, vmin = 1e9, vmax = 0 };
            if (pts.Count < 2) return m;
            double sx = 0, sy = 0;
            foreach (var p in pts) { sx += Math.Log(p.impact); sy += p.kmh; m.vmin = Math.Min(m.vmin, p.kmh); m.vmax = Math.Max(m.vmax, p.kmh); }
            double mx = sx / pts.Count, my = sy / pts.Count, num = 0, den = 0;
            foreach (var p in pts) { double dx = Math.Log(p.impact) - mx; num += dx * (p.kmh - my); den += dx * dx; }
            if (den <= 1e-9 || m.vmax - m.vmin < 1) return m;
            m.b = num / den; m.a = my - m.b * mx; m.ok = true;
            return m;
        }

        public static double Speed(ImpactModel m, double impact)
        {
            if (impact <= 0) return 0;
            double v = m.a + m.b * Math.Log(impact);
            return Math.Max(m.vmin - 3, Math.Min(m.vmax + 3, v));
        }

        // 충격 + 걸음 수: 속도 = a + b·ln(충격) + c·spm (계획 0005). 맞출 값이 3개라 보정 4구간 이상일 때만
        public struct ImpCadModel { public bool ok; public double a, b, c, vmin, vmax; public int n; }

        public static ImpCadModel FitImpactCad(List<(double kmh, double impact, double cad)> pts)
        {
            var m = new ImpCadModel { n = pts.Count, vmin = 1e9, vmax = 0 };
            if (pts.Count < 4) return m;
            double m1 = 0, m2 = 0, my = 0;
            foreach (var p in pts) { m1 += Math.Log(p.impact); m2 += p.cad; my += p.kmh; m.vmin = Math.Min(m.vmin, p.kmh); m.vmax = Math.Max(m.vmax, p.kmh); }
            m1 /= pts.Count; m2 /= pts.Count; my /= pts.Count;
            double s11 = 0, s12 = 0, s22 = 0, s1y = 0, s2y = 0;
            foreach (var p in pts)
            {
                double x1 = Math.Log(p.impact) - m1, x2 = p.cad - m2, y = p.kmh - my;
                s11 += x1 * x1; s12 += x1 * x2; s22 += x2 * x2; s1y += x1 * y; s2y += x2 * y;
            }
            double det = s11 * s22 - s12 * s12;
            // 충격과 걸음 수가 거의 같이 움직이면(또는 걸음 수가 안 변하면) 계수가 튐 → 충격만 쓰게 둠
            if (m.vmax - m.vmin < 1 || det <= 1e-3 * s11 * s22) return m;
            m.b = (s1y * s22 - s2y * s12) / det;
            m.c = (s2y * s11 - s1y * s12) / det;
            m.a = my - m.b * m1 - m.c * m2;
            m.ok = true;
            return m;
        }

        public static double Speed(ImpCadModel m, double impact, double cad)
        {
            if (impact <= 0 || cad <= 0) return 0;
            double v = m.a + m.b * Math.Log(impact) + m.c * cad;
            return Math.Max(m.vmin - 3, Math.Min(m.vmax + 3, v));
        }

        // 보정: (속도, 케이던스) 점들 → 보폭 = a + b·케이던스
        public struct Model { public bool ok; public double a, b, minStep, maxStep; public int n; }

        public static double StepOf(double kmh, double cad) => kmh / 3.6 / (cad / 60);

        public static Model Fit(List<(double kmh, double cad)> pts)
        {
            var m = new Model { n = pts.Count };
            if (pts.Count == 0) return m;
            double sx = 0, sy = 0, minC = 1e9, maxC = -1e9;
            m.minStep = 1e9; m.maxStep = 0;
            foreach (var p in pts)
            {
                double st = StepOf(p.kmh, p.cad);
                sx += p.cad; sy += st;
                minC = Math.Min(minC, p.cad); maxC = Math.Max(maxC, p.cad);
                m.minStep = Math.Min(m.minStep, st); m.maxStep = Math.Max(m.maxStep, st);
            }
            double mx = sx / pts.Count, my = sy / pts.Count;
            if (pts.Count >= 2 && maxC - minC > 3)
            {
                double num = 0, den = 0;
                foreach (var p in pts) { num += (p.cad - mx) * (StepOf(p.kmh, p.cad) - my); den += (p.cad - mx) * (p.cad - mx); }
                m.b = num / den; m.a = my - m.b * mx;
            }
            else { m.a = my; m.b = 0; }
            m.ok = true;
            return m;
        }

        public static double Speed(Model m, double cad)
        {
            double st = m.a + m.b * cad;
            // 보정 범위 밖으로 너무 멀리 늘려 쓰지 않게
            st = Math.Max(0.8 * m.minStep, Math.Min(1.2 * m.maxStep, st));
            return st * cad / 60 * 3.6;
        }
    }
}
