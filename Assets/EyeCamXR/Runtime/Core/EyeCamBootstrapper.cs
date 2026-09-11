using UnityEngine;
using UnityEngine.SceneManagement;

namespace EyeCamXR
{
    /// <summary>Builds the EyeCamXR rig at runtime — only in its own scene
    /// (MuseLogKit's bootstrapper is likewise gated to V1/V2).</summary>
    public static class EyeCamBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (SceneManager.GetActiveScene().name != "EyeCamXR") return;
            if (SessionController.Instance != null) return;
            var go = new GameObject("EyeCamSession");
            go.AddComponent<SessionController>();
            Debug.Log("[EyeCam] bootstrapped");
        }
    }
}
