using System;

namespace EyeCamXR.Dsp
{
    /// <summary>
    /// scipy.signal.welch with defaults, bin-exact (verified against
    /// scipy 1.15.3 to 3.5e-18 in the design phase):
    ///   window   = PERIODIC Hann  w[i] = 0.5 - 0.5*cos(2*pi*i/n)
    ///   noverlap = nperseg // 2  (trailing partial segment discarded)
    ///   detrend  = per-segment MEAN subtraction BEFORE windowing
    ///   nfft     = nperseg (no zero padding), rfft one-sided
    ///   scaling  = density: 1 / (fs * sum(w^2)); double all bins except DC
    ///              and (even nperseg only) Nyquist
    ///   average  = arithmetic mean over segments
    /// </summary>
    public static class Welch
    {
        /// <summary>PSD of x[offset..offset+count). Returns array of nperseg/2+1
        /// bins; freqs[k] = k * fs / nperseg. Null if count &lt; nperseg.</summary>
        public static double[] Psd(double[] x, int offset, int count, double fs, int nperseg)
        {
            if (count < nperseg || nperseg < 2) return null;

            var w = new double[nperseg];
            double sumW2 = 0;
            for (int i = 0; i < nperseg; i++)
            {
                w[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / nperseg);
                sumW2 += w[i] * w[i];
            }
            double scale = 1.0 / (fs * sumW2);

            int noverlap = nperseg / 2;
            int step = nperseg - noverlap;
            int half = nperseg / 2 + 1;
            var acc = new double[half];
            var buf = new double[nperseg];
            int nSeg = 0;

            for (int start = 0; start + nperseg <= count; start += step)
            {
                double mean = 0;
                for (int i = 0; i < nperseg; i++) mean += x[offset + start + i];
                mean /= nperseg;
                for (int i = 0; i < nperseg; i++)
                    buf[i] = (x[offset + start + i] - mean) * w[i];

                Fft.Real(buf, out var re, out var im);
                for (int k = 0; k < half; k++)
                    acc[k] += (re[k] * re[k] + im[k] * im[k]) * scale;
                nSeg++;
            }
            if (nSeg == 0) return null;

            bool even = nperseg % 2 == 0;
            int lastDoubled = even ? half - 2 : half - 1;
            for (int k = 0; k < half; k++)
            {
                acc[k] /= nSeg;
                if (k >= 1 && k <= lastDoubled) acc[k] *= 2.0;
            }
            return acc;
        }
    }
}
