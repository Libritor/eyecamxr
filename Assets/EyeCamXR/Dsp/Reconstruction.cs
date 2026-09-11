using System;

namespace EyeCamXR.Dsp
{
    /// <summary>Ports reconstruct.py reconstruct() + save_image normalization.</summary>
    public static class Reconstruction
    {
        /// <summary>
        /// eegT ascending; chans[c][i] aligned with eegT; weights per channel
        /// (&lt;=0 skips a channel); cursor arrays per logged stimulus row.
        /// Returns grid[h, w]. Throws if no cell produced a score.
        /// </summary>
        public static double[,] Grid(double[] eegT, double[][] chans, double[] weights,
                                     double[] curT, int[] gx, int[] gy,
                                     double fsOverride = 0,
                                     double stimFreq = EyeCamConfig.StimFreqHz)
        {
            int nCh = chans.Length;
            double fs = fsOverride > 0 ? fsOverride : InferFs.FromTimestamps(eegT);
            int nperseg = (int)Math.Round(fs);
            int n = eegT.Length;

            var chSigma = new double[nCh];
            for (int c = 0; c < nCh; c++) chSigma[c] = Stats.HfSigma(chans[c]);

            int gridW = 0, gridH = 0;
            for (int i = 0; i < gx.Length; i++)
            {
                if (gx[i] + 1 > gridW) gridW = gx[i] + 1;
                if (gy[i] + 1 > gridH) gridH = gy[i] + 1;
            }

            var acc = new double[gridH, gridW];
            var cnt = new double[gridH, gridW];

            // contiguous cell visits
            int s = 0;
            for (int i = 1; i <= gx.Length; i++)
            {
                bool boundary = i == gx.Length || gx[i] != gx[i - 1] || gy[i] != gy[i - 1];
                if (!boundary) continue;
                int e = i; // visit is [s, e)
                double t0 = curT[s], t1 = curT[e - 1];
                int i0 = Stats.SearchSorted(eegT, t0);
                int i1 = Stats.SearchSorted(eegT, t1);
                int need = nperseg - (i1 - i0);
                if (need > 0)
                {
                    i0 = Math.Max(0, i0 - need / 2);
                    i1 = Math.Min(n, i0 + nperseg);
                }
                double num = 0, den = 0;
                for (int c = 0; c < nCh; c++)
                {
                    if (weights[c] <= 0) continue;
                    double sc = Ssvep.CellScore(chans[c], i0, i1 - i0, fs,
                                                chSigma[c], stimFreq);
                    if (!double.IsNaN(sc)) { num += weights[c] * sc; den += weights[c]; }
                }
                if (den > 0)
                {
                    acc[gy[s], gx[s]] += num / den;
                    cnt[gy[s], gx[s]] += 1;
                }
                s = i;
            }

            var grid = new double[gridH, gridW];
            var flatValid = new double[gridH * gridW];
            int nValid = 0;
            for (int y = 0; y < gridH; y++)
                for (int x = 0; x < gridW; x++)
                {
                    grid[y, x] = cnt[y, x] > 0 ? acc[y, x] / cnt[y, x] : double.NaN;
                    if (!double.IsNaN(grid[y, x])) flatValid[nValid++] = grid[y, x];
                }
            if (nValid == 0)
                throw new InvalidOperationException(
                    "no cell produced a score - EEG/cursor overlap empty?");
            if (nValid < gridH * gridW)
            {
                var valid = new double[nValid];
                Array.Copy(flatValid, valid, nValid);
                double fill = Stats.Median(valid, 0, nValid);
                for (int y = 0; y < gridH; y++)
                    for (int x = 0; x < gridW; x++)
                        if (double.IsNaN(grid[y, x])) grid[y, x] = fill;
            }

            return VerticalBlend(grid);
        }

        /// <summary>Eq. 1 kernel [0.5,0.5,1,0.5,0.5]/3 along rows (axis 0),
        /// nearest-edge replication (scipy convolve1d mode='nearest').</summary>
        public static double[,] VerticalBlend(double[,] grid)
        {
            int h = grid.GetLength(0), w = grid.GetLength(1);
            var k = EyeCamConfig.VerticalKernel;
            double ksum = 0;
            foreach (var v in k) ksum += v;
            var outG = new double[h, w];
            int half = k.Length / 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double sum = 0;
                    for (int j = 0; j < k.Length; j++)
                    {
                        int yy = y + j - half;
                        if (yy < 0) yy = 0;
                        else if (yy >= h) yy = h - 1;
                        sum += k[j] / ksum * grid[yy, x];
                    }
                    outG[y, x] = sum;
                }
            return outG;
        }

        /// <summary>save_image normalization: clip((g-p2)/(p98-p2), 0, 1).</summary>
        public static double[,] Normalize(double[,] grid)
        {
            int h = grid.GetLength(0), w = grid.GetLength(1);
            var flat = new double[h * w];
            int i = 0;
            foreach (var v in grid) flat[i++] = v;
            double lo = Stats.Percentile(flat, 2);
            double hi = Stats.Percentile(flat, 98);
            double range = Math.Max(hi - lo, 1e-12);
            var norm = new double[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double v = (grid[y, x] - lo) / range;
                    norm[y, x] = v < 0 ? 0 : (v > 1 ? 1 : v);
                }
            return norm;
        }

        public static double PearsonVsTarget(double[,] grid, double[,] target)
        {
            int h = grid.GetLength(0), w = grid.GetLength(1);
            var a = new double[h * w];
            var b = new double[h * w];
            int i = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) { a[i] = grid[y, x]; b[i] = target[y, x]; i++; }
            return Stats.Pearson(a, b);
        }
    }
}
