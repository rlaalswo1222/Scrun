using System;
using System.Collections.Generic;

namespace Scrun.SpeedLab
{
    // 러닝머신 모터·롤러 진동 주파수 → 속도.
    // 1초 창(0.25초 간격, Hann, 4배 0 채움) 스펙트럼 8개의 중앙값 → 발 충격 같은 순간 잡음을 걸러 낸다.
    // 주변(±6Hz) 중앙값을 빼서 "주변보다 얼마나 솟았나"(log10)만 남긴다 = Peak.
    // 보정: 여러 속도에서 같은 비율 k(Hz per km/h)로 움직이는 봉우리 줄을 찾는다(회전 주파수 ∝ 벨트 속도).
    // 실시간: 찾은 줄들(k_j)이 모두 봉우리에 걸리는 속도 v를 찾는다.
    public class FreqSpeed
    {
        public const float FMin = 5f;
        const int Keep = 8;                  // 중앙값을 낼 창 개수 (약 2.75초)
        public readonly int Fs, N, Bins;     // N = FFT 크기, Bins = 쓰는 칸 수(FMax까지)
        public readonly float BinHz, FMax;
        public readonly float[] Peak;        // 최신 봉우리 세기(칸별)
        public bool Fresh;                   // 새 스펙트럼이 나왔나(읽은 쪽이 끔)

        readonly int win, hop;
        readonly float[][] ring;             // 최근 Keep개 log 파워
        int ringN, ringHead, sinceHop;
        readonly float[] bx, by, bz;         // 최근 1초 원본
        int bufHead, bufCount;
        readonly double[] re, im, hann;
        readonly float[] logP, tmp;
        readonly int halfBand;

        public FreqSpeed(int fs)
        {
            Fs = fs; win = fs; hop = fs / 4;
            N = 1; while (N < win * 4) N <<= 1;
            BinHz = (float)fs / N;
            FMax = Math.Min(0.45f * fs, 190f);
            Bins = (int)(FMax / BinHz);
            ring = new float[Keep][];
            for (int i = 0; i < Keep; i++) ring[i] = new float[Bins];
            bx = new float[win]; by = new float[win]; bz = new float[win];
            re = new double[N]; im = new double[N]; hann = new double[win];
            for (int i = 0; i < win; i++) hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (win - 1));
            logP = new float[Bins]; tmp = new float[Keep]; Peak = new float[Bins];
            halfBand = Math.Max(8, (int)(6f / BinHz));   // Hann 1초 창의 봉우리 폭(±2Hz)보다 넉넉히
        }

        public void Add(float x, float y, float z)
        {
            bx[bufHead] = x; by[bufHead] = y; bz[bufHead] = z;
            bufHead = (bufHead + 1) % win;
            if (bufCount < win) bufCount++;
            if (++sinceHop < hop || bufCount < win) return;
            sinceHop = 0;
            Spectrum();
        }

        void Spectrum()
        {
            var acc = ring[ringHead];
            Array.Clear(acc, 0, Bins);
            foreach (var b in new[] { bx, by, bz })
            {
                double mean = 0;
                for (int i = 0; i < win; i++) mean += b[i];
                mean /= win;
                Array.Clear(re, 0, N); Array.Clear(im, 0, N);
                for (int i = 0; i < win; i++) re[i] = (b[(bufHead + i) % win] - mean) * hann[i];
                Fft.Transform(re, im);
                for (int k = 0; k < Bins; k++) acc[k] += (float)(re[k] * re[k] + im[k] * im[k]);
            }
            for (int k = 0; k < Bins; k++) acc[k] = (float)Math.Log10(acc[k] + 1e-9);
            ringHead = (ringHead + 1) % Keep;
            if (ringN < Keep) ringN++;

            for (int k = 0; k < Bins; k++)
            {
                for (int i = 0; i < ringN; i++) tmp[i] = ring[i][k];
                logP[k] = Median(tmp, ringN);
            }
            // 주변 바닥 = ±6Hz 중앙값 (칸이 많아 3칸 건너 표본)
            var nb = new List<float>(64);
            int lo0 = (int)(FMin / BinHz);
            for (int k = 0; k < Bins; k++)
            {
                if (k < lo0) { Peak[k] = 0; continue; }
                nb.Clear();
                for (int j = Math.Max(lo0, k - halfBand); j <= Math.Min(Bins - 1, k + halfBand); j += 3) nb.Add(logP[j]);
                nb.Sort();
                Peak[k] = Math.Max(0f, logP[k] - nb[nb.Count / 2]);
            }
            Fresh = true;
        }

        static float Median(float[] a, int n)
        {
            var c = new float[n];
            Array.Copy(a, c, n);
            Array.Sort(c);
            return n % 2 == 1 ? c[n / 2] : 0.5f * (c[n / 2 - 1] + c[n / 2]);
        }

        // 주파수 f에서의 봉우리 세기 (±1칸 중 최대 — 칸 경계 사이 봉우리도 잡게). mask = 주변 기계 고정 진동 칸(무시)
        public static float At(float[] p, float binHz, float f, bool[] mask = null)
        {
            int k = (int)Math.Round(f / binHz);
            if (k < 1 || k >= p.Length - 1) return 0;
            if (mask != null && mask[k]) return 0;
            return Math.Max(p[k], Math.Max(p[k - 1], p[k + 1]));
        }

        [Serializable]
        public struct Line { public float k, score; }

        public List<Line> Lines { get; private set; } = new List<Line>();
        public bool[] Mask { get; private set; }
        public float VLo = 3f, VHi = 22f;
        float[] post, ker;                   // 추적: 속도 칸별 확률, 퍼뜨리는 가우스

        // 보정 점들(속도, 평균 Peak) → 모든 속도에서 봉우리에 걸리는 비율 k들(최대 6개, 센 순서)과 주변 고정 진동 칸
        public void Fit(List<(float kmh, float[] peak)> pts)
        {
            var lines = new List<Line>();
            Lines = lines; Mask = null; post = null;
            if (pts.Count < 2) return;
            float vmin = 1e9f, vmax = 0;
            foreach (var p in pts) { vmin = Math.Min(vmin, p.kmh); vmax = Math.Max(vmax, p.kmh); }
            if (vmax - vmin < 1.4f) return;    // 속도가 너무 비슷하면 아무 봉우리나 맞아 버림
            // 보정한 범위 밖으로 너무 멀리는 찾지 않는다(주변 진동이 엉뚱한 속도로 맞는 것 방지)
            VLo = Math.Max(3f, vmin - 4f); VHi = vmax + 4f;

            var ks = new List<float>(); var sc = new List<float>();
            for (float k = 0.3f; k < 25f; k *= 1.001f)
            {
                float worst = 1e9f, sum = 0; bool inside = true;
                foreach (var p in pts)
                {
                    float f = k * p.kmh;
                    if (f < FMin || f > FMax * 0.98f) { inside = false; break; }
                    float s = At(p.peak, BinHz, f);
                    worst = Math.Min(worst, s); sum += s;
                }
                ks.Add(k); sc.Add(inside ? 0.5f * worst + 0.5f * sum / pts.Count : 0f);
            }
            // 국소 최대만, 서로 8% 이상 떨어진 것(봉우리 폭 때문에 같은 줄이 두 번 잡히지 않게)
            var cand = new List<Line>();
            for (int i = 1; i < ks.Count - 1; i++)
                if (sc[i] >= sc[i - 1] && sc[i] > sc[i + 1] && sc[i] > 0.2f) cand.Add(new Line { k = ks[i], score = sc[i] });
            cand.Sort((a, b) => b.score.CompareTo(a.score));
            foreach (var c in cand)
            {
                bool near = false;
                foreach (var l in lines) if (Math.Abs(c.k / l.k - 1) < 0.08f) near = true;
                if (!near) lines.Add(c);
                if (lines.Count == 6) break;
            }
            // 모든 보정 속도에서 같은 자리에 솟아 있고 우리 줄로 설명되지 않는 칸 = 주변 기계 → 무시
            var mask = new bool[Bins];
            for (int b = 0; b < Bins; b++)
            {
                float f = b * BinHz, lowest = 1e9f;
                foreach (var p in pts) lowest = Math.Min(lowest, p.peak[b]);
                if (lowest <= 0.25f) continue;
                bool own = false;
                foreach (var p in pts)
                    foreach (var l in lines)
                    {
                        float g = l.k * p.kmh;
                        if (Math.Abs(f - g) < Math.Max(0.6f, 0.03f * g)) own = true;
                    }
                mask[b] = !own;
            }
            Mask = mask;
        }

        // 최신 스펙트럼 → (속도, 신뢰도). 신뢰도 = 줄들이 걸린 봉우리 세기 가중 평균(log10, 0.3 ≈ 주변의 2배)
        // 추적: 0.25초마다 속도는 조금만 변한다(σ 0.35 km/h, 아주 가끔 멀리 점프) → 순간 잡음에 튀지 않음
        public (float kmh, float conf) Estimate()
        {
            var lines = Lines;
            if (lines.Count == 0) return (0, 0);
            float wsum = 0;
            foreach (var l in lines) wsum += l.score;
            const float step = 0.02f, beta = 6f, jump = 0.0005f;
            int nv = (int)((VHi - VLo) / step) + 1;
            var s = new float[nv];
            float smax = 0;
            for (int i = 0; i < nv; i++)
            {
                float v = VLo + i * step, acc = 0;
                foreach (var l in lines)
                {
                    float f = l.k * v;
                    if (f < FMin || f > FMax) continue;
                    acc += l.score * At(Peak, BinHz, f, Mask);
                }
                s[i] = acc / wsum;
                smax = Math.Max(smax, s[i]);
            }
            if (post == null || post.Length != nv) { post = new float[nv]; for (int i = 0; i < nv; i++) post[i] = 1f / nv; }
            // 이전 확률을 σ만큼 퍼뜨리고(가우스), 이번 점수로 곱한다
            const int r = 60;
            if (ker == null)
            {
                ker = new float[2 * r + 1];
                float sig = 0.35f / step;
                for (int d = -r; d <= r; d++) ker[d + r] = (float)Math.Exp(-0.5 * d * d / (sig * sig));
            }
            var prior = new float[nv];
            for (int i = 0; i < nv; i++)
            {
                if (post[i] < 1e-9f) continue;
                for (int d = -r; d <= r; d++)
                {
                    int j = i + d;
                    if (j >= 0 && j < nv) prior[j] += post[i] * ker[d + r];
                }
            }
            float ps = 0;
            for (int i = 0; i < nv; i++) ps += prior[i];
            float total = 0; int bi = 0;
            for (int i = 0; i < nv; i++)
            {
                float pr = (1 - jump) * prior[i] / ps + jump / nv;
                post[i] = pr * (float)Math.Exp(beta * (s[i] - smax));
                total += post[i];
            }
            for (int i = 0; i < nv; i++) { post[i] /= total; if (post[i] > post[bi]) bi = i; }
            float bestV = VLo + bi * step;
            if (bi > 0 && bi < nv - 1)
            {
                float y0 = s[bi - 1], y1 = s[bi], y2 = s[bi + 1], den = y0 - 2 * y1 + y2;
                if (den != 0) bestV += step * Math.Max(-0.5f, Math.Min(0.5f, 0.5f * (y0 - y2) / den));
            }
            return (bestV, s[bi]);
        }
    }
}
