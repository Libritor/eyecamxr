# MuseLogMR Project

I have set up the project structure and **integrated the LibMuse SDK** for you.
I have also added a custom **AndroidManifest.xml** to fix the "Windowed Mode" issue.

## How to Run (Immersive Mode Fix)
1.  **Open in Unity Hub**: Add `c:\Users\alexa\.gemini\antigravity\scratch\MuseLogMR`.
2.  **Setup Scene**:
    -   Click **MuseLog > Setup Scene**.
3.  **FIX Project Settings** (Crucial for "Android 12L" error):
    -   Click **MuseLog > Fix Project Settings**.
    -   This sets API Level to 32 and Fixes Color Space.
3.  **CRITICAL: Enable OpenXR**:
    -   Go to **Edit > Project Settings > XR Plug-in Management**.
    -   In the **Android Tab** (Robot icon), check **OpenXR**.
    -   Under **OpenXR > Features**, check **Meta Quest Support**.
    -   (If available) Check **Passthrough Support**.
4.  **Build**:
    -   File > Build Settings > Android.
    -   Build and Run.

**Troubleshooting:**
-   **"Android 12L API Level" Error?**
    1.  Go to **Edit > Project Settings > Player**.
    2.  Click the **Android Tab** (Robot icon).
    3.  Open **Other Settings > Identification**.
    4.  Set **Minimum API Level** to **Android 12L (API level 32)**.
    5.  Set **Scripting Backend** to **IL2CPP**.
    6.  Set **Target Architecture** to **ARM64**.
-   **"Gradle Build Failed" (Attribute application@label)?**
    -   I fixed `AndroidManifest.xml` by removing the conflicting label/icon.
    -   I also added a fix for "Both Input Handling".
    -   **Re-run Fix Project Settings** menu item.
    -   **Re-build**.
