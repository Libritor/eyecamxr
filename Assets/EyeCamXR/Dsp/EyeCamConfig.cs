namespace EyeCamXR.Dsp
{
    /// <summary>
    /// Mirrors C:\Users\alexa\eyecam\config.py — the single source of truth for
    /// analysis constants. Any change must be made in BOTH places and the
    /// golden-vector parity tests re-run.
    /// </summary>
    public static class EyeCamConfig
    {
        public const double StimFreqHz = 15.0;
        public const double HarmonicHz = 30.0;

        public const double NoiseBandLo = 14.0;
        public const double NoiseBandHi = 50.0;

        public static readonly double[] VerticalKernel = { 0.5, 0.5, 1.0, 0.5, 0.5 };

        public const double ArtifactZ = 4.0;
        public const double ArtifactDropFrac = 0.2;

        public const int CalibBlocks = 6;
        public const double CalibOnS = 7.0;
        public const double CalibOffS = 7.0;
        public const double CalibDiscardS = 1.0;
        public const double CalibDprimeMin = 1.0;
        public const double CalibRatioMin = 1.3;
        public const int CalibRankMin = 5;
        public const double CalibWeightDprimeMin = 0.5;

        public const double GateRMin = 0.6;

        // preset name -> (gridW, gridH, secondsPerCell)
        public static readonly (string name, int w, int h, double spc)[] Presets =
        {
            ("quick", 12, 8, 4.0),
            ("standard", 16, 12, 5.0),
            ("fine", 24, 18, 6.0),
        };
    }
}
