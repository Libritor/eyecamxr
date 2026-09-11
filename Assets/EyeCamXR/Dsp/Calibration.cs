using System;
using System.Collections.Generic;

namespace EyeCamXR.Dsp
{
    [Serializable]
    public class CalibChannel
    {
        public string name;
        public double dprime;
        public double ratio;
        public int rank;
        public double weight;
        public List<double> onScores = new List<double>();
        public List<double> offScores = new List<double>();
    }

    [Serializable]
    public class CalibResult
    {
        public double fs;
        public CalibChannel[] channels;
        public string best;
        public bool passed;
    }

    /// <summary>Ports run_session.py score_calibration exactly.</summary>
    public static class Calibration
    {
        public static CalibResult Score(
            double[] t, double[][] chans, string[] names,
            (bool isOn, double t0, double t1)[] blocks,
            int rankMin = EyeCamConfig.CalibRankMin,
            double stimFreq = EyeCamConfig.StimFreqHz)
        {
            double fs = InferFs.FromTimestamps(t);
            int nCh = chans.Length;
            var result = new CalibResult { fs = fs, channels = new CalibChannel[nCh] };

            int bestIdx = -1;
            var rawWeights = new double[nCh];
            for (int c = 0; c < nCh; c++)
            {
                var ch = new CalibChannel { name = names[c] };
                double sigma = Stats.HfSigma(chans[c]);
                foreach (var (isOn, t0, t1) in blocks)
                {
                    int i0 = Stats.SearchSorted(t, t0 + EyeCamConfig.CalibDiscardS);
                    int i1 = Stats.SearchSorted(t, t1);
                    double sc = Ssvep.CellScore(chans[c], i0, Math.Max(0, i1 - i0),
                                                fs, sigma, stimFreq);
                    if (!double.IsNaN(sc))
                        (isOn ? ch.onScores : ch.offScores).Add(sc);
                }

                var on = ch.onScores.ToArray();
                var off = ch.offScores.ToArray();
                if (on.Length < 3 || off.Length < 3)
                {
                    ch.dprime = 0.0; ch.ratio = 1.0; ch.rank = 0;
                }
                else
                {
                    double madOn = Stats.RobustSigma(on);
                    double madOff = Stats.RobustSigma(off);
                    double medOn = Stats.Median(on);
                    double medOff = Stats.Median(off);
                    // Scale-aware floor: MAD of <=6 block scores can quantize
                    // to ~0 and make d' explode; caps d' near 20.
                    double spread = Math.Sqrt(0.5 * (madOn * madOn + madOff * madOff));
                    spread = Math.Max(spread, 0.05 * Math.Abs(medOn));
                    ch.dprime = (medOn - medOff) / Math.Max(spread, 1e-12);
                    ch.ratio = medOn / Math.Max(medOff, 1e-12);
                    double offMax = double.NegativeInfinity;
                    foreach (var v in off) if (v > offMax) offMax = v;
                    int rank = 0;
                    foreach (var v in on) if (v > offMax) rank++;
                    ch.rank = rank;
                }

                // d' only gates admission; the bounded ratio apportions weight.
                rawWeights[c] = ch.dprime >= EyeCamConfig.CalibWeightDprimeMin
                    ? Math.Max(0.0, ch.ratio - 1.0) : 0.0;

                result.channels[c] = ch;
                if (bestIdx < 0 || ch.dprime > result.channels[bestIdx].dprime)
                    bestIdx = c;
            }

            double total = 0;
            foreach (var w in rawWeights) total += w;
            for (int c = 0; c < nCh; c++)
                result.channels[c].weight = total > 0
                    ? rawWeights[c] / total : 1.0 / nCh; // uniform fallback

            var b = result.channels[bestIdx];
            result.best = b.name;
            result.passed = b.dprime >= EyeCamConfig.CalibDprimeMin
                            && b.ratio >= EyeCamConfig.CalibRatioMin
                            && b.rank >= rankMin;
            return result;
        }
    }
}
