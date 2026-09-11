using UnityEngine;

/// <summary>
/// Ensures the immersive Muse monitor exists even if the scene is empty.
/// This avoids relying on editor menu actions (e.g., "MuseLog > Setup Scene").
/// </summary>
public static class MuseLogBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // Only own the original MuseLog scenes; other scenes (EyeCamXR)
        // build their own rig and must not inherit this one or its port bind.
        var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName != "V1" && sceneName != "V2") return;

        // Managers (OSC receiver)
        if (MuseOSCManager.Instance == null)
        {
            var managers = GameObject.Find("Managers") ?? new GameObject("Managers");
            var osc = managers.GetComponent<MuseOSCManager>() ?? managers.AddComponent<MuseOSCManager>();
            osc.useNativeMuse = false; // default to OSC from phone/computer
            osc.port = 5000;
        }

        // Camera passthrough config (optional)
        var cam = Camera.main;
        if (cam != null && cam.GetComponent<PassthroughSetup>() == null)
        {
            cam.gameObject.AddComponent<PassthroughSetup>();
        }

        // UI Panel
        if (Object.FindAnyObjectByType<MuseEegMonitorPanel>() == null)
        {
            var go = new GameObject("MuseEegMonitorPanel");
            go.AddComponent<MuseEegMonitorPanel>();
        }
    }
}

