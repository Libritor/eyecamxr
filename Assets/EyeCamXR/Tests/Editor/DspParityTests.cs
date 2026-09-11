using System;
using NUnit.Framework;
using EyeCamXR.Dsp;

namespace EyeCamXR.Tests
{
    public class WelchParityTests
    {
        [Test]
        public void MatchesScipyBinForBin()
        {
            var file = Golden.Load<WelchFile>("welch_cases.json");
            Assert.IsNotEmpty(file.cases);
            foreach (var c in file.cases)
            {
                double fs = Golden.Scalar(c.fs);
                var x = Golden.Doubles(c.x);
                var expected = Golden.Doubles(c.psd);
                var psd = Welch.Psd(x, 0, x.Length, fs, c.nperseg);
                Assert.IsNotNull(psd, c.name);
                Assert.AreEqual(expected.Length, psd.Length, c.name + " length");
                double maxPsd = 0;
                foreach (var v in expected) maxPsd = Math.Max(maxPsd, Math.Abs(v));
                for (int k = 0; k < psd.Length; k++)
                {
                    double tol = 1e-9 * Math.Max(Math.Abs(expected[k]), maxPsd * 1e-6);
                    Assert.LessOrEqual(Math.Abs(psd[k] - expected[k]), tol,
                        $"{c.name} bin {k}: {psd[k]} vs {expected[k]}");
                }
            }
        }
    }

    public class SsvepScoreTests
    {
        [Test]
        public void MatchesPython()
        {
            var file = Golden.Load<SsvepFile>("ssvep_cases.json");
            Assert.IsNotEmpty(file.cases);
            foreach (var c in file.cases)
            {
                var x = Golden.Doubles(c.x);
                double actual = Ssvep.Score(x, 0, x.Length, Golden.Scalar(c.fs));
                Golden.AssertClose(actual, Golden.Scalar(c.score), 1e-9, c.name);
            }
        }
    }

    public class CellScoreTests
    {
        [Test]
        public void MatchesPythonIncludingDropAndClipBranches()
        {
            var file = Golden.Load<CellFile>("cell_cases.json");
            Assert.IsNotEmpty(file.cases);
            foreach (var c in file.cases)
            {
                var x = Golden.Doubles(c.x);
                double actual = Ssvep.CellScore(x, 0, x.Length,
                    Golden.Scalar(c.fs), Golden.Scalar(c.sigmaFloor));
                Golden.AssertClose(actual, Golden.Scalar(c.score), 1e-9, c.name);
            }
        }
    }

    public class InferFsTests
    {
        [Test]
        public void GapAwareRateMatchesPython()
        {
            var file = Golden.Load<InferFsFile>("inferfs_cases.json");
            Assert.IsNotEmpty(file.cases);
            foreach (var c in file.cases)
            {
                var t = Golden.Doubles(c.t);
                Golden.AssertClose(InferFs.FromTimestamps(t),
                                   Golden.Scalar(c.fs), 1e-12, c.name);
            }
        }
    }

    public class CalibrationTests
    {
        [Test]
        public void FullDecisionMatchesPython()
        {
            var f = Golden.Load<CalibFile>("calib_case.json");
            var t = Golden.Doubles(f.t);
            var chans = Golden.Channels(f.chFlat, f.nCh);
            var t0 = Golden.Doubles(f.blockT0);
            var t1 = Golden.Doubles(f.blockT1);
            var blocks = new (bool, double, double)[f.blockIsOn.Length];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = (f.blockIsOn[i] == 1, t0[i], t1[i]);

            var res = Calibration.Score(t, chans, f.names, blocks, f.rankMin);

            // Python rounds fs to 2 decimals in calibration.json.
            Assert.LessOrEqual(Math.Abs(res.fs - Golden.Scalar(f.expFs)), 0.011, "fs");

            var expD = Golden.Doubles(f.expDprime);
            var expRt = Golden.Doubles(f.expRatio);
            var expW = Golden.Doubles(f.expWeights);
            for (int c = 0; c < f.nCh; c++)
            {
                Golden.AssertClose(res.channels[c].dprime, expD[c], 1e-9,
                                   f.names[c] + " dprime");
                Golden.AssertClose(res.channels[c].ratio, expRt[c], 1e-9,
                                   f.names[c] + " ratio");
                Assert.AreEqual(f.expRank[c], res.channels[c].rank,
                                f.names[c] + " rank");
                Golden.AssertClose(res.channels[c].weight, expW[c], 1e-9,
                                   f.names[c] + " weight");
            }
            Assert.AreEqual(f.expBest, res.best, "best channel");
            Assert.AreEqual(f.expPassed, res.passed, "passed");
        }
    }

    public class ReconstructionTests
    {
        [Test]
        public void EndToEndGridMatchesPython()
        {
            var f = Golden.Load<ReconFile>("recon_case.json");
            var eegT = Golden.Doubles(f.eegT);
            var chans = Golden.Channels(f.chFlat, f.nCh);
            var weights = Golden.Doubles(f.weights);
            var curT = Golden.Doubles(f.curT);

            var grid = Reconstruction.Grid(eegT, chans, weights, curT, f.gx, f.gy);
            Assert.AreEqual(f.gridH, grid.GetLength(0), "gridH");
            Assert.AreEqual(f.gridW, grid.GetLength(1), "gridW");

            var expGrid = Golden.Doubles(f.expGridFlat);
            int i = 0;
            for (int y = 0; y < f.gridH; y++)
                for (int x = 0; x < f.gridW; x++, i++)
                    Golden.AssertClose(grid[y, x], expGrid[i], 1e-9,
                                       $"grid[{y},{x}]");

            var norm = Reconstruction.Normalize(grid);
            var expNorm = Golden.Doubles(f.expNormFlat);
            i = 0;
            for (int y = 0; y < f.gridH; y++)
                for (int x = 0; x < f.gridW; x++, i++)
                    Assert.LessOrEqual(Math.Abs(norm[y, x] - expNorm[i]), 1e-9,
                                       $"norm[{y},{x}]");

            var targetFlat = Golden.Doubles(f.targetFlat);
            var target = new double[f.gridH, f.gridW];
            i = 0;
            for (int y = 0; y < f.gridH; y++)
                for (int x = 0; x < f.gridW; x++, i++)
                    target[y, x] = targetFlat[i];
            Golden.AssertClose(Reconstruction.PearsonVsTarget(grid, target),
                               Golden.Scalar(f.expR), 1e-9, "pearson r");
        }
    }
}
