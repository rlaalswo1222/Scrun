using System;

namespace Scrun.SpeedLab
{
    // 2차 IIR 필터(RBJ 쿡북). 실시간으로 샘플 하나씩 넣는다.
    public struct Biquad
    {
        double b0, b1, b2, a1, a2, z1, z2;

        public static Biquad Make(bool high, double fc, double fs, double q)
        {
            double w = 2 * Math.PI * fc / fs, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
            double a0 = 1 + alpha;
            var f = new Biquad();
            if (high) { f.b0 = (1 + c) / 2; f.b1 = -(1 + c); f.b2 = (1 + c) / 2; }
            else { f.b0 = (1 - c) / 2; f.b1 = 1 - c; f.b2 = (1 - c) / 2; }
            f.b0 /= a0; f.b1 /= a0; f.b2 /= a0;
            f.a1 = -2 * c / a0; f.a2 = (1 - alpha) / a0;
            return f;
        }

        public double Step(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    // 4차 버터워스 = 2차 두 개
    public struct Butter4
    {
        Biquad s1, s2;

        public Butter4(bool high, double fc, double fs)
        {
            s1 = Biquad.Make(high, fc, fs, 0.5412);
            s2 = Biquad.Make(high, fc, fs, 1.3066);
        }

        public double Step(double x) => s2.Step(s1.Step(x));
    }

    public static class Fft
    {
        // 제자리 복소 FFT (n = 2의 거듭제곱)
        public static void Transform(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double t = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr; cr = t;
                    }
                }
            }
        }
    }
}
