using System;

namespace EyeCamXR.Dsp
{
    /// <summary>Ports reconstruct.py ssvep_score + cell_score exactly.</summary>
    public static class Ssvep
    {
        /// <summary>Sum of psd bins with lo &lt;= freq &lt;= hi (both inclusive) times df.</summary>
        static double BandPower(double[] psd, double df, double lo, double hi)
        {
            double sum = 0;
            for (int k = 0; k < psd.Length; k++)
            {
                double f = k * df;
                if (f >= lo && f <= hi) sum += psd[k];
            }
            return sum * df;
        }

        /// <summary>Relative SSVEP power; NaN when the segment is too short or
        /// the noise denominator is non-positive. stimFreq parameterized for the
        /// color-multiplex extension (harmonic is always 2*stimFreq).</summary>
        public static double Score(double[] x, int offset, int count, double fs,
                                   double stimFreq = EyeCamConfig.StimFreqHz)
        {
            int nperseg = (int)Math.Round(fs);
            if (count < nperseg) return double.NaN;
            var psd = Welch.Psd(x, offset, count, fs, nperseg);
            if (psd == null) return double.NaN;
            double df = fs / nperseg;

            double nyquist = fs / 2.0;
            double f0 = stimFreq, f1 = 2.0 * stimFreq;
            double sig = BandPower(psd, df, f0 - 0.6, f0 + 0.6);
            if (f1 < nyquist - 1.0)
                sig += BandPower(psd, df, f1 - 0.6, f1 + 0.6);
            double total = BandPower(psd, df, EyeCamConfig.NoiseBandLo,
                                     Math.Min(EyeCamConfig.NoiseBandHi, nyquist - 1.0));
            double denom = total - sig; // broadband minus the stimulus bins
            if (denom <= 0) return double.NaN;
            return sig / denom;
        }

        /// <summary>Artifact-aware score: per-segment median baseline and robust
        /// sigma (floored by the channel's HF sigma), clip at ArtifactZ sigmas,
        /// drop the cell-channel when too many samples are clipped.</summary>
        public static double CellScore(double[] x, int offset, int count, double fs,
                                       double sigmaFloor,
                                       double stimFreq = EyeCamConfig.StimFreqHz)
        {
            if (count < 8) return double.NaN;

            var seg = new double[count];
            Array.Copy(x, offset, seg, 0, count);
            double baseline = Stats.Median(seg, 0, count);

            var dev = new double[count];
            for (int i = 0; i < count; i++) dev[i] = seg[i] - baseline;
            double sigma = Math.Max(Stats.RobustSigma(dev), sigmaFloor);

            double limit = EyeCamConfig.ArtifactZ * sigma;
            int bad = 0;
            for (int i = 0; i < count; i++)
                if (Math.Abs(dev[i]) > limit) bad++;
            if ((double)bad / count > EyeCamConfig.ArtifactDropFrac)
                return double.NaN; // blink/motion-dominated

            var clipped = new double[count];
            for (int i = 0; i < count; i++)
            {
                double d = dev[i];
                if (d > limit) d = limit;
                else if (d < -limit) d = -limit;
                clipped[i] = baseline + d;
            }
            return Score(clipped, 0, count, fs, stimFreq);
        }
    }

    /// <summary>Gap-aware sample-rate inference (reconstruct.infer_fs).</summary>
    public static class InferFs
    {
        public static double FromTimestamps(double[] t)
        {
            if (t.Length < 2) throw new ArgumentException("need >= 2 timestamps");
            double span = 0;
            int good = 0;
            for (int i = 1; i < t.Length; i++)
            {
                double dt = t[i] - t[i - 1];
                if (dt < 0.5) { span += dt; good++; } // >0.5 s = stream gap
            }
            if (span <= 0) throw new ArgumentException("EEG timestamps do not advance");
            return good / span;
        }
    }
}
