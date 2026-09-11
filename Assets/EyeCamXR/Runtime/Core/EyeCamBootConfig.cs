using System;
using System.IO;
using UnityEngine;

namespace EyeCamXR
{
#pragma warning disable 0649
    [Serializable]
    class BootConfigJson
    {
        public string source = "";        // "osc" | "native"
        public int oscPort = -1;
        public string target = "";
        public string preset = "";        // quick | standard | fine
        public int gridW = -1, gridH = -1;
        public double secondsPerCell = -1;
        public int calibBlocks = -1;
        public int calibRankMin = -1;
        public int auto = -1;             // 1 = unattended
        public string spectatorHost = null;
        public int spectatorPort = -1;
        public int auxElectrode = -1;     // 1 = request aux preset (native)
    }
#pragma warning restore 0649

    /// <summary>
    /// Optional headless configuration: push a JSON file to
    /// &lt;persistentDataPath&gt;/boot_config.json (adb push ... on Quest) and it
    /// overrides EyeCamRunConfig at boot — enables fully unattended on-device
    /// sessions (the phantom-at-Quest gate) and demo setup without rebuilds.
    /// Missing fields keep their defaults.
    /// </summary>
    public static class EyeCamBootConfig
    {
        public static string Path =>
            System.IO.Path.Combine(Application.persistentDataPath,
                "boot_config.json");

        public static void Apply()
        {
            try
            {
                // Channel 1: intent extra (adb am start -e eyecam_config '<json>')
                // — survives scoped storage, works on release builds.
                string json = ReadIntentExtra("eyecam_config");
                // Channel 2: boot_config.json in persistentDataPath.
                if (string.IsNullOrEmpty(json) && File.Exists(Path))
                    json = File.ReadAllText(Path);
                if (string.IsNullOrEmpty(json)) return;
                var c = JsonUtility.FromJson<BootConfigJson>(json);
                if (c == null) return;

                if (c.source == "osc") EyeCamRunConfig.source = EegSourceKind.Osc;
                else if (c.source == "native")
                    EyeCamRunConfig.source = EegSourceKind.Native;
                if (c.oscPort > 0) EyeCamRunConfig.oscPort = c.oscPort;
                if (!string.IsNullOrEmpty(c.target))
                    EyeCamRunConfig.targetName = c.target;
                if (!string.IsNullOrEmpty(c.preset))
                    EyeCamRunConfig.ApplyPreset(c.preset);
                if (c.gridW > 0) EyeCamRunConfig.gridW = c.gridW;
                if (c.gridH > 0) EyeCamRunConfig.gridH = c.gridH;
                if (c.secondsPerCell > 0)
                    EyeCamRunConfig.secondsPerCell = c.secondsPerCell;
                if (c.calibBlocks > 0) EyeCamRunConfig.calibBlocks = c.calibBlocks;
                if (c.calibRankMin >= 0)
                    EyeCamRunConfig.calibRankMin = c.calibRankMin;
                if (c.auto >= 0) EyeCamRunConfig.auto = c.auto == 1;
                if (c.spectatorHost != null)
                    EyeCamRunConfig.spectatorHost = c.spectatorHost;
                if (c.spectatorPort > 0)
                    EyeCamRunConfig.spectatorPort = c.spectatorPort;
                if (c.auxElectrode >= 0)
                    PlayerPrefs.SetInt("eyecam_aux_electrode", c.auxElectrode);

                Debug.Log($"[EyeCam] boot_config applied: source=" +
                    $"{EyeCamRunConfig.source} auto={EyeCamRunConfig.auto} " +
                    $"grid={EyeCamRunConfig.gridW}x{EyeCamRunConfig.gridH} " +
                    $"spc={EyeCamRunConfig.secondsPerCell} " +
                    $"spectator={EyeCamRunConfig.spectatorHost}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EyeCam] boot_config parse failed: " + e.Message);
            }
        }

        static string ReadIntentExtra(string key)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using var activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                return intent.Call<string>("getStringExtra", key);
            }
            catch { return null; }
#else
            return null;
#endif
        }
    }
}
