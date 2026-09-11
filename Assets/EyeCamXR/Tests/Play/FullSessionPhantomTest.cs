using System.Collections;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EyeCamXR.Tests
{
    /// <summary>
    /// The P3 gate: a complete unattended session in Play Mode against the
    /// REAL Python phantom over loopback UDP — stimulus timing -> OSC ->
    /// SessionData -> calibration -> scan -> reconstruction, r >= 0.6.
    /// Mirrors the laptop stack's full_gate.py.
    /// </summary>
    public class FullSessionPhantomTest
    {
        const int Port = 5015;
        Process _phantom;
        GameObject _camGo, _sessionGo;
        string _dir;

        [UnityTest]
        [Timeout(480000)]
        public IEnumerator FullPhantomSessionReconstructs()
        {
            Application.targetFrameRate = 90;
            QualitySettings.vSyncCount = 0;

            _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "EditorRuns", "playmode_phantom"));
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);

            EyeCamRunConfig.source = EegSourceKind.Osc;
            EyeCamRunConfig.oscPort = Port;
            EyeCamRunConfig.targetName = "NO";
            EyeCamRunConfig.gridW = 8;
            EyeCamRunConfig.gridH = 6;
            EyeCamRunConfig.secondsPerCell = 0.8;
            EyeCamRunConfig.calibBlocks = 3;
            EyeCamRunConfig.calibRankMin = 2;
            EyeCamRunConfig.auto = true;
            EyeCamRunConfig.sessionDirOverride = _dir;
            EyeCamRunConfig.spectatorHost = "";

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();

            _phantom = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "phantom_subject.py --session \"" + _dir +
                                $"\" --host 127.0.0.1 --port {Port} --max-seconds 420",
                    WorkingDirectory = @"C:\Users\alexa\eyecam",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            Assert.IsTrue(_phantom.Start(), "phantom failed to start");
            yield return new WaitForSecondsRealtime(1.5f); // phantom boots

            _sessionGo = new GameObject("EyeCamSession");
            _sessionGo.AddComponent<SessionController>();

            while (!SessionController.Completed)
                yield return null;

            var info = SessionController.LastResult;
            Assert.IsNotNull(info, "no session info");
            UnityEngine.Debug.Log(
                $"[EyeCamTest] r={info.r:F3} calibPassed={info.calibPassed} " +
                $"fs={info.fsInferred:F1} flicker={info.measuredFlickerHz:F2}");

            Assert.IsTrue(File.Exists(Path.Combine(_dir, "eeg.csv")), "eeg.csv");
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "session.json")),
                "session.json");
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "recon.png")),
                "recon.png");
            Assert.IsTrue(info.calibPassed,
                "calibration did not detect the phantom SSVEP");
            Assert.GreaterOrEqual(info.r, Dsp.EyeCamConfig.GateRMin,
                $"reconstruction r {info.r:F3} below gate");
            Assert.IsFalse(info.aborted, "session aborted");
        }

        [TearDown]
        public void Cleanup()
        {
            try { if (_phantom != null && !_phantom.HasExited) _phantom.Kill(); }
            catch { }
            if (_sessionGo != null) Object.Destroy(_sessionGo);
            if (_camGo != null) Object.Destroy(_camGo);
        }
    }
}
