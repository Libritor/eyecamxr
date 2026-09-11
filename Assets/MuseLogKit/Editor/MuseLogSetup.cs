#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEditor.Build.Reporting;

public class MuseLogSetup
{
    [MenuItem("MuseLog/Setup Scene")]
    public static void SetupScene()
    {
        Debug.Log("Starting MuseLog Setup...");

        // 0. Ensure Android + OpenXR
        // Note: ProjectSettings modification via script is complex; we rely on User following "XR Plugin Management" steps.
        // We can warn:
        Debug.LogWarning("Make sure you have enabled OpenXR in Project Settings > XR Plug-in Management > Android!");

        // 1. Camera Setup
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camObj = new GameObject("Main Camera");
            cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }
        
        // CRITICAL FIX: Ensure Alpha is 0 for Passthrough
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); 
        
        if (cam.gameObject.GetComponent<PassthroughSetup>() == null)
            cam.gameObject.AddComponent<PassthroughSetup>();
            
        // 2. Managers
        GameObject managers = GameObject.Find("Managers");
        if (managers == null) managers = new GameObject("Managers");
        
        if (managers.GetComponent<MuseHandler>() == null) managers.AddComponent<MuseHandler>();
        if (managers.GetComponent<MuseOSCManager>() == null) managers.AddComponent<MuseOSCManager>();
        
        // 3. Visualization Rig
        GameObject rig = GameObject.Find("GraphRig");
        if (rig == null)
        {
            rig = new GameObject("GraphRig");
            rig.transform.position = new Vector3(0, 1.2f, 0.8f); // Eye level forward
            rig.transform.rotation = Quaternion.identity;
        }
        
        if (rig.GetComponent<BandPowerVisualizer>() == null) rig.AddComponent<BandPowerVisualizer>();
        if (rig.GetComponent<SimpleGrabbable>() == null) rig.AddComponent<SimpleGrabbable>();
        
        BoxCollider box = rig.GetComponent<BoxCollider>();
        if (box == null) box = rig.AddComponent<BoxCollider>();
        box.size = new Vector3(1.2f, 1f, 0.2f);
        
        Rigidbody rb = rig.GetComponent<Rigidbody>();
        if (rb == null) rb = rig.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;

        Debug.Log("MuseLog Scene Setup Complete! NOW: Go to Project Settings > XR Plug-in Management -> Android -> OpenXR -> Features and Enable 'Meta Quest Support'!");
    }
    [MenuItem("MuseLog/Fix Project Settings")]
    public static void FixProjectSettings()
    {
        // Fix Android API Level for Quest Passthrough
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
        
        // Fix Color Space (OpenXR requires Linear)
        PlayerSettings.colorSpace = ColorSpace.Linear;

        // Fix IL2CPP (Required for Meta Quest)
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // Fix Active Input Handling (Avoid "Both")
        var serializedObject = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var activeInputHandler = serializedObject.FindProperty("activeInputHandler");
        if (activeInputHandler != null)
        {
            activeInputHandler.intValue = 1; // 0=Old, 1=New, 2=Both
            serializedObject.ApplyModifiedProperties();
        }

        Debug.Log("MuseLog: Fixed Android API Level (32), Color Space (Linear), Scripting Backend (IL2CPP) and Input System (New)!");
    }

    [InitializeOnLoadMethod]
    public static void AutoFixProjectSettings()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel32)
            {
                FixProjectSettings();
            }
        }
    }

    [MenuItem("MuseLog/Build Android APK (Quest)")]
    public static void BuildAndroidAPK()
    {
        FixProjectSettings();

        string[] scenes = new string[] { "Assets/V1.unity" };
        string outputPath = "MuseLogMR-Quest.apk";

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log("BUILD SUCCEEDED: " + outputPath);
            // If called from the editor menu, we don't want to quit Unity.
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("BUILD FAILED: " + report.summary.totalErrors + " errors");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
#endif
