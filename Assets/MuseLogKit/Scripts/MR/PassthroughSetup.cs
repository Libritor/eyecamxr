using UnityEngine;

/// <summary>
/// Configure camera for Passthrough (Quest 3S).
/// </summary>
public class PassthroughSetup : MonoBehaviour
{
    void Start()
    {
        // 1. Set Camera to Solid Color with Alpha 0
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0); // Transparent
        }

        // 2. Ensure OVRManager / OpenXR Passthrough is enabled if present
        // (This part usually requires the Meta XR Core SDK in the project)
        // We do a simple check:
        Debug.Log("[PassthroughSetup] Camera background set to transparent. Ensure 'Passthrough Layer' is added in your scene.");
    }
}
