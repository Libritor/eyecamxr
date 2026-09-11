using System;

namespace EyeCamXR.Dsp
{
    /// <summary>Robust statistics matching numpy semantics.</summary>
    public static class Stats
    {
        /// <summary>numpy median: mean of the two middle values for even n.</summary>
        public static double Median(double[] x, int offset, int count)
        {
            if (count <= 0) return double.NaN;
            var copy = new double[count];
            Array.Copy(x, offset, copy, 0, count);
            Array.Sort(copy);
            int mid = count / 2;
            return (count % 2 == 1) ? copy[mid] : 0.5 * (copy[mid - 1] + copy[mid]);
        }

        public static double Median(double[] x) => Median(x, 0, x.Length);

        /// <summary>1.4826 * median(|x - median(x)|) + 1e-12 (reconstruct.robust_sigma).</summary>
        public static double RobustSigma(double[] x, int offset, int count)
        {
            double med = Median(x, offset, count);
            var dev = new double[count];
            for (int i = 0; i < count; i++) dev[i] = Math.Abs(x[offset + i] - med);
            return 1.4826 * Median(dev, 0, count) + 1e-12;
        }

        public static double RobustSigma(double[] x) => RobustSigma(x, 0, x.Length);

        /// <summary>robust_sigma(diff(x)) / sqrt(2) (reconstruct.hf_sigma).</summary>
        public static double HfSigma(double[] x)
        {
            if (x.Length < 2) return 1e-12;
            var d = new double[x.Length - 1];
            for (int i = 0; i < d.Length; i++) d[i] = x[i + 1] - x[i];
            return RobustSigma(d) / Math.Sqrt(2.0);
        }

        /// <summary>numpy percentile with default linear interpolation, NaNs excluded by caller.</summary>
        public static double Percentile(double[] x, double q)
        {
            var copy = (double[])x.Clone();
            Array.Sort(copy);
            int n = copy.Length;
            if (n == 0) return double.NaN;
            if (n == 1) return copy[0];
            double rank = (n - 1) * q / 100.0;
            int lo = (int)Math.Floor(rank);
            int hi = Math.Min(lo + 1, n - 1);
            double frac = rank - lo;
            return copy[lo] + (copy[hi] - copy[lo]) * frac;
        }

        public static double NanMedian(double[] x)
        {
            int n = 0;
            foreach (var v in x) if (!double.IsNaN(v)) n++;
            if (n == 0) return double.NaN;
            var copy = new double[n];
            int j = 0;
            foreach (var v in x) if (!double.IsNaN(v)) copy[j++] = v;
            return Median(copy, 0, n);
        }

        /// <summary>np.searchsorted side='left': first index i with t[i] >= v.</summary>
        public static int SearchSorted(double[] t, double v)
        {
            int lo = 0, hi = t.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (t[mid] < v) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        public static double Pearson(double[] a, double[] b)
        {
            int n = a.Length;
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double num = 0, da = 0, db = 0;
            for (int i = 0; i < n; i++)
            {
                double xa = a[i] - ma, xb = b[i] - mb;
                num += xa * xb; da += xa * xa; db += xb * xb;
            }
            double den = Math.Sqrt(da * db);
            return den > 0 ? num / den : double.NaN;
        }
    }
}
