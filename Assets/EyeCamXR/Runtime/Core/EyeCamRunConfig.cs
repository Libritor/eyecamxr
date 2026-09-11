using UnityEngine;

namespace EyeCamXR
{
    public enum EegSourceKind { Osc, Native }

    /// <summary>
    /// Session configuration. Static so PlayMode tests and the bootstrapper
    /// can set it before the rig spawns; the lobby UI edits it interactively.
    /// </summary>
    public static class EyeCamRunConfig
    {
        public static EegSourceKind source = EegSourceKind.Osc;
        public static int oscPort = 5005;

        public static string targetName = "NO";  // Resources/Targets/<name>.json
        public static int gridW = 12;
        public static int gridH = 8;
        public static double secondsPerCell = 4.0;

        public static int calibBlocks = 6;
        public static int calibRankMin = 5;

        /// <summary>Unattended: skip every key wait, auto-advance stages.</summary>
        public static bool auto;

        /// <summary>Editor/tests: wall-clock flicker instead of frame-locked.</summary>
        public static bool timeLockedFlicker =>
            Application.isEditor || forceTimeLocked;
        public static bool forceTimeLocked;

        /// <summary>Spectator bridge endpoint ("" disables).</summary>
        public static string spectatorHost =
            PlayerPrefs.GetString("eyecam_spectator_host", "");
        public static int spectatorPort = 8090;

        /// <summary>Session directory override (tests); "" = default naming.</summary>
        public static string sessionDirOverride = "";

        public static void ApplyPreset(string name)
        {
            foreach (var (pname, w, h, spc) in Dsp.EyeCamConfig.Presets)
            {
                if (pname == name)
                {
                    gridW = w; gridH = h; secondsPerCell = spc;
                    return;
                }
            }
        }
    }
}
