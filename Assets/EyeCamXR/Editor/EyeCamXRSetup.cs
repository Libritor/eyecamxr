#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EyeCamXR.EditorTools
{
    public static class EyeCamXRSetup
    {
        [MenuItem("EyeCamXR/Build APK (Quest)")]
        public static void BuildEyeCamAPK()
        {
            // Same proven settings path as MuseLogSetup.FixProjectSettings.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,
                "com.mannlab.eyecamxr");
            PlayerSettings.productName = "EyeCamXR";

            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/EyeCamXR/EyeCamXR.unity" },
                locationPathName = "EyeCamXR.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log("EYECAM BUILD SUCCEEDED: EyeCamXR.apk");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("EYECAM BUILD FAILED: "
                               + report.summary.totalErrors + " errors");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
#endif
