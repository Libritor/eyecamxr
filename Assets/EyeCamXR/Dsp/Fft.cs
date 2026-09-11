using System;

namespace EyeCamXR.Dsp
{
    /// <summary>
    /// Real FFT: radix-2 iterative for power-of-two lengths, direct DFT
    /// otherwise (nperseg = round(fs) can be any integer; N~256 keeps the
    /// O(N^2) fallback well under a millisecond per segment).
    /// </summary>
    public static class Fft
    {
        /// <summary>rfft: returns re/im arrays of length n/2 + 1.</summary>
        public static void Real(double[] x, out double[] re, out double[] im)
        {
            int n = x.Length;
            int half = n / 2 + 1;
            re = new double[half];
            im = new double[half];
            if (n == 0) return;

            if ((n & (n - 1)) == 0)
            {
                var cre = (double[])x.Clone();
                var cim = new double[n];
                Radix2(cre, cim);
                for (int k = 0; k < half; k++) { re[k] = cre[k]; im[k] = cim[k]; }
            }
            else
            {
                for (int k = 0; k < half; k++)
                {
                    double sre = 0, sim = 0;
                    double w = -2.0 * Math.PI * k / n;
                    for (int t = 0; t < n; t++)
                    {
                        double a = w * t;
                        sre += x[t] * Math.Cos(a);
                        sim += x[t] * Math.Sin(a);
                    }
                    re[k] = sre;
                    im[k] = sim;
                }
            }
        }

        static void Radix2(double[] re, double[] im)
        {
            int n = re.Length;
            // bit reversal
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cwr = 1, cwi = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double ur = re[a], ui = im[a];
                        double vr = re[b] * cwr - im[b] * cwi;
                        double vi = re[b] * cwi + im[b] * cwr;
                        re[a] = ur + vr; im[a] = ui + vi;
                        re[b] = ur - vr; im[b] = ui - vi;
                        double nwr = cwr * wr - cwi * wi;
                        cwi = cwr * wi + cwi * wr;
                        cwr = nwr;
                    }
                }
            }
        }
    }
}
