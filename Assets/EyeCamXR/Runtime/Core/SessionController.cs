using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using EyeCamXR.Dsp;
using UnityEngine;

namespace EyeCamXR
{
    [Serializable]
    public class SessionInfo
    {
        public string mode;
        public string[] channels;
        public int gridW, gridH;
        public double secondsPerCell;
        public int calibBlocks;
        public double fsInferred = -1;
        public float refreshRequested = -1, refreshActual = -1;
        public int slowFrames, totalFrames;
        public double measuredFlickerHz = -1;
        public bool calibPassed;
        public string bestChannel = "";
        public double[] weights = new double[0];
        public double r = double.NaN;
        public bool aborted;
        public string error = "";
        public List<string> stages = new List<string>();
    }

    /// <summary>
    /// The six-stage eye-as-camera session, ported from run_session.py:
    /// Lobby -> SignalCheck -> Calibration -> Confirm -> Scan -> Reconstruct
    /// -> Result. All heavy DSP on Task.Run workers; the frame loop only
    /// flips flicker state and writes log rows.
    /// </summary>
    public sealed class SessionController : MonoBehaviour
    {
        public static SessionController Instance { get; private set; }
        public static SessionInfo LastResult { get; private set; }
        public static bool Completed { get; private set; }

        IEegSource _source;
        SessionData _store;
        EegRecorder _recorder;
        SpectatorSender _spectator;
        StimulusBoard _board;
        UiPanel _panel;
        readonly ControllerInput _input = new ControllerInput();
        readonly RefreshRateGovernor _governor = new RefreshRateGovernor();

        string _sessionDir;
        double[,] _target;
        SessionInfo _info;
        CalibResult _calib;

        // in-memory cursor log mirror (reconstruction input)
        readonly List<double> _curT = new List<double>();
        readonly List<int> _curGx = new List<int>();
        readonly List<int> _curGy = new List<int>();
        readonly List<double> _flickerRises = new List<double>();
        bool _lastFlicker;

        void Awake()
        {
            Instance = this;
            Completed = false;
        }

        void Start()
        {
            StartCoroutine(RunSession());
        }

        void Update()
        {
            _input.Poll();
            _source?.Tick();
        }

        void OnDestroy()
        {
            _recorder?.Stop();
            _source?.Stop();
            _spectator?.Close();
        }

        void Log(string msg)
        {
            Debug.Log("[EyeCam] " + msg);
            _spectator?.Log(msg);
        }

        void StageEvent(string stage, string status)
        {
            _info.stages.Add($"{stage}:{status}");
            _spectator?.Stage(stage, status);
            Log($"stage {stage} {status}");
        }

        IEnumerator RunSession()
        {
            // ---- init ----
            _sessionDir = string.IsNullOrEmpty(EyeCamRunConfig.sessionDirOverride)
                ? Path.Combine(Application.persistentDataPath, "EyeCamXR",
                    "session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                : EyeCamRunConfig.sessionDirOverride;
            Directory.CreateDirectory(_sessionDir);

            _info = new SessionInfo
            {
                mode = EyeCamRunConfig.source.ToString(),
                gridW = EyeCamRunConfig.gridW,
                gridH = EyeCamRunConfig.gridH,
                secondsPerCell = EyeCamRunConfig.secondsPerCell,
                calibBlocks = EyeCamRunConfig.calibBlocks,
            };
            LastResult = _info;

            var rig = Camera.main != null ? Camera.main.transform : transform;
            if (Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = Color.black; // black-void VR
            }
            _panel = new UiPanel(rig);
            _board = new StimulusBoard(rig);
            _target = TargetProvider.Load(EyeCamRunConfig.targetName,
                EyeCamRunConfig.gridW, EyeCamRunConfig.gridH);
            _board.SetTarget(_target);
            SaveTargetCsv();

            _governor.RequestMax();
            _info.refreshRequested = _governor.RequestedRate;
            _info.refreshActual = _governor.ActualRate;

            _source = EyeCamRunConfig.source == EegSourceKind.Native
                ? (IEegSource)new NativeMuseSource()
                : new OscEegSource(EyeCamRunConfig.oscPort);
            _store = new SessionData(8);
            _source.Start(_store);
            _recorder = new EegRecorder(_store,
                Path.Combine(_sessionDir, "eeg.csv"), PaddedNames());
            _recorder.Start();

            _spectator = new SpectatorSender(EyeCamRunConfig.spectatorHost,
                EyeCamRunConfig.spectatorPort,
                Path.GetFileName(_sessionDir));
            _spectator.Hello(_info.mode, 8, 256, _info.gridW, _info.gridH,
                EyeCamConfig.StimFreqHz, _governor.ActualRate);
            StageEvent("source_start", "ok");

            // ---- lobby ----
            if (!EyeCamRunConfig.auto)
            {
                bool go = false, quit = false;
                while (!go && !quit)
                {
                    _panel.Show("EyeCamXR",
                        $"source: {_source.Info}\n" +
                        $"target: {EyeCamRunConfig.targetName}  " +
                        $"grid: {_info.gridW}x{_info.gridH}  " +
                        $"{_info.secondsPerCell:0.#}s/cell\n" +
                        $"this device IP: {LocalIp()}\n\n" +
                        "TRIGGER / Space = start     B / Esc = quit");
                    if (_input.Confirm) go = true;
                    if (_input.Abort) quit = true;
                    yield return null;
                }
                if (quit) { Finish(3); yield break; }
            }
            StageEvent("lobby", "ok");

            // ---- signal check ----
            double stableSince = -1;
            double deadline = EegClock.Now + 90;
            bool ok = false, aborted = false;
            double lastUi = 0;
            while (!ok && !aborted)
            {
                double now = EegClock.Now;
                double rate = _store.CountSince(now - 2.0) / 2.0;
                bool stable = rate > 40;
                if (stable && stableSince < 0) stableSince = now;
                if (!stable) stableSince = -1;
                bool ready = stableSince > 0 && now - stableSince >= 3;

                if (now - lastUi > 0.1)
                {
                    lastUi = now;
                    var rms = _store.RmsSince(now - 2.0);
                    string rmsTxt = rms == null ? "--" :
                        string.Join("  ", Array.ConvertAll(rms,
                            v => v.ToString("F1", CultureInfo.InvariantCulture)));
                    _panel.Show("SIGNAL CHECK",
                        $"rate: {rate:F1} Hz  ({(stable ? "STABLE" : "waiting...")})\n" +
                        $"rms: {rmsTxt}\n" +
                        $"send /muse/eeg to {LocalIp()}:{EyeCamRunConfig.oscPort}\n\n" +
                        (ready ? "TRIGGER = continue" : "waiting for stable stream"),
                        stable ? Color.green : new Color(1f, 0.7f, 0f));
                    _spectator.Eeg(rate, rms);
                }

                if (ready && (EyeCamRunConfig.auto || _input.Confirm)) ok = true;
                if (_input.Abort) aborted = true;
                if (EyeCamRunConfig.auto && now > deadline)
                {
                    StageEvent("signal_check", "fail");
                    Finish(2);
                    yield break;
                }
                yield return null;
            }
            if (aborted) { StageEvent("signal_check", "abort"); Finish(3); yield break; }
            StageEvent("signal_check", "ok");

            // ---- calibration ----
            var blocks = new List<(bool isOn, double t0, double t1)>();
            yield return Calibrate(blocks);
            if (_info.aborted) { Finish(3); yield break; }

            var calibTask = ScoreCalibrationAsync(blocks);
            _panel.Show("CALIBRATION", "scoring...");
            while (!calibTask.IsCompleted) yield return null;
            if (calibTask.Exception != null)
            {
                _info.error = calibTask.Exception.InnerException?.Message
                              ?? calibTask.Exception.Message;
                StageEvent("calibrate", "error");
                Finish(3);
                yield break;
            }
            _calib = calibTask.Result;
            _info.calibPassed = _calib.passed;
            _info.bestChannel = _calib.best;
            _info.fsInferred = _calib.fs;
            var w = new double[_calib.channels.Length];
            for (int i = 0; i < w.Length; i++) w[i] = _calib.channels[i].weight;
            _info.weights = w;
            SaveCalibrationJson();
            _spectator.Calib($"best={_calib.best}", _calib.passed, _calib.best, w);
            StageEvent("calibrate", _calib.passed ? "ok" : "weak");

            // ---- confirm ----
            if (!EyeCamRunConfig.auto)
            {
                string table = "";
                foreach (var c in _calib.channels)
                    table += $"{c.name}: d'={c.dprime:F2} on/off={c.ratio:F2} " +
                             $"rank={c.rank} w={c.weight:F2}\n";
                bool proceed = false, quit = false;
                while (!proceed && !quit)
                {
                    _panel.Show(_calib.passed ? "SSVEP DETECTED" : "WARNING: WEAK SSVEP",
                        table + "\n" + (_calib.passed
                            ? "TRIGGER = start scan   B = abort"
                            : "No reliable contrast - scan will likely fail.\n" +
                              "TRIGGER = scan anyway   B = abort"),
                        _calib.passed ? Color.green : Color.red);
                    if (_input.Confirm) proceed = true;
                    if (_input.Abort) quit = true;
                    yield return null;
                }
                if (quit) { StageEvent("confirm", "abort"); Finish(3); yield break; }
            }
            StageEvent("confirm", "ok");

            // ---- scan ----
            yield return Scan();
            StageEvent("scan", _info.aborted ? "aborted" : "ok");

            // ---- reconstruct ----
            _board.SetVisible(false);
            _panel.Show("RECONSTRUCTING", "processing EEG...");
            yield return new WaitForSecondsRealtime(0.7f); // final packets land
            var reconTask = ReconstructAsync();
            while (!reconTask.IsCompleted) yield return null;

            Texture2D tex = null;
            if (reconTask.Exception != null)
            {
                _info.error = reconTask.Exception.InnerException?.Message
                              ?? reconTask.Exception.Message;
                StageEvent("reconstruct", "error");
            }
            else
            {
                var (norm, grid, r) = reconTask.Result;
                _info.r = r;
                tex = SaveReconPng(norm);
                var flat = new double[grid.GetLength(0) * grid.GetLength(1)];
                int fi = 0;
                foreach (var v in grid) flat[fi++] = v;
                _spectator.Grid(grid.GetLength(1), grid.GetLength(0), flat);
                StageEvent("reconstruct", "ok");
            }

            ComputeMeasuredFlicker();
            Finish(_info.aborted ? 4 : 0);

            // ---- result screen ----
            if (tex != null) ShowResultQuad(tex);
            _panel.Show("RESULT",
                double.IsNaN(_info.r) ? "no ground truth"
                    : $"r = {_info.r:F3}\nflicker {_info.measuredFlickerHz:F2} Hz  " +
                      $"drops {_info.slowFrames}/{_info.totalFrames}",
                _info.r >= EyeCamConfig.GateRMin ? Color.green : Color.yellow);
            if (!EyeCamRunConfig.auto)
                while (!_input.Confirm && !_input.Abort) yield return null;
        }

        IEnumerator Calibrate(List<(bool, double, double)> blocks)
        {
            var flicker = new FlickerDriver(EyeCamConfig.StimFreqHz,
                EyeCamRunConfig.timeLockedFlicker, _governor.ActualRate);
            _board.SetVisible(true);
            _panel.Hide();
            using var log = new StimulusLogger(
                Path.Combine(_sessionDir, "calib_log.csv"));

            for (int b = 0; b < EyeCamRunConfig.calibBlocks; b++)
            {
                for (int phase = 0; phase < 2; phase++)
                {
                    bool isOn = phase == 0;
                    double dur = isOn ? EyeCamConfig.CalibOnS : EyeCamConfig.CalibOffS;
                    double t0 = EegClock.Now;
                    _spectator.Stage("calibrate",
                        $"block {b + 1}/{EyeCamRunConfig.calibBlocks} " +
                        (isOn ? "ON" : "OFF"));
                    double prev = t0;
                    double lastSpec = 0;
                    bool prevFl = false;
                    while (EegClock.Now - t0 < dur)
                    {
                        if (_input.Abort) { _info.aborted = true; yield break; }
                        double now = EegClock.Now;
                        bool fl = flicker.Step(now) && isOn;
                        _board.ShowCalibrationPatch(fl, isOn);
                        log.Row(now, -1, -1, isOn ? 1.0 : 0.0, fl, now - prev);
                        TrackFlicker(fl, now);
                        _governor.CountFrame(now);
                        // Mirror the flicker to the bridge: the remote phantom
                        // (phantom-at-Quest) and spectators need calibration
                        // luminance/edges, not just scan cursor positions.
                        if (fl != prevFl || now - lastSpec > 0.1)
                        {
                            _spectator.Cursor(-1, -1, isOn ? 1.0 : 0.0, fl);
                            lastSpec = now;
                        }
                        prevFl = fl;
                        prev = now;
                        yield return null;
                    }
                    blocks.Add((isOn, t0, EegClock.Now));
                }
            }
            _board.SetVisible(false);
        }

        IEnumerator Scan()
        {
            var flicker = new FlickerDriver(EyeCamConfig.StimFreqHz,
                EyeCamRunConfig.timeLockedFlicker, _governor.ActualRate);
            _board.SetVisible(true);
            _panel.Hide();
            using var log = new StimulusLogger(
                Path.Combine(_sessionDir, "cursor_log.csv"));

            int gw = _info.gridW, gh = _info.gridH;
            double spc = _info.secondsPerCell;
            bool pauseRequested = false;
            double lastSpec = 0;

            for (int gy = 0; gy < gh; gy++)
            {
                if (pauseRequested && !EyeCamRunConfig.auto)
                {
                    pauseRequested = false;
                    _board.SetVisible(false);
                    _panel.Show("PAUSED", "TRIGGER = resume   B = abort");
                    while (!_input.Confirm)
                    {
                        if (_input.Abort) { _info.aborted = true; yield break; }
                        yield return null;
                    }
                    _panel.Hide();
                    _board.SetVisible(true);
                }

                bool leftToRight = gy % 2 == 0;
                for (int step = 0; step < gw; step++)
                {
                    int gx = leftToRight ? step : gw - 1 - step;
                    double lum = _board.Luminance(gx, gy);
                    double cellStart = EegClock.Now;
                    double prev = cellStart;
                    while (EegClock.Now - cellStart < spc)
                    {
                        if (_input.Abort) { _info.aborted = true; yield break; }
                        if (_input.Pause) pauseRequested = true;
                        double now = EegClock.Now;
                        bool fl = flicker.Step(now) && lum > 0.05;
                        _board.ShowScanCell(gx, gy, fl);
                        log.Row(now, gx, gy, lum, fl, now - prev);
                        _curT.Add(now); _curGx.Add(gx); _curGy.Add(gy);
                        TrackFlicker(fl, now);
                        _governor.CountFrame(now);
                        if (now - lastSpec > 0.1 || log.FlickerChanged(fl))
                        {
                            _spectator.Cursor(gx, gy, lum, fl);
                            lastSpec = now;
                        }
                        prev = now;
                        yield return null;
                    }
                }
                _spectator.Stage("scan", $"row {gy + 1}/{gh}");
            }
            _board.SetVisible(false);
        }

        Task<CalibResult> ScoreCalibrationAsync(List<(bool, double, double)> blocks)
        {
            _store.Snapshot(out var t, out var chans);
            var names = PaddedNames();
            int active = ActiveChannelCount(chans);
            var useChans = new double[active][];
            var useNames = new string[active];
            for (int c = 0; c < active; c++)
            {
                useChans[c] = chans[c];
                useNames[c] = names[c];
            }
            var blockArr = blocks.ToArray();
            return Task.Run(() => Calibration.Score(t, useChans, useNames,
                blockArr, EyeCamRunConfig.calibRankMin));
        }

        Task<(double[,] norm, double[,] grid, double r)> ReconstructAsync()
        {
            _store.Snapshot(out var t, out var chans);
            int active = ActiveChannelCount(chans);
            var useChans = new double[active][];
            var weights = new double[active];
            for (int c = 0; c < active; c++)
            {
                useChans[c] = chans[c];
                weights[c] = c < _calib.channels.Length
                    ? _calib.channels[c].weight : 0;
            }
            var curT = _curT.ToArray();
            var gx = _curGx.ToArray();
            var gy = _curGy.ToArray();
            var target = _target;
            return Task.Run(() =>
            {
                var grid = Reconstruction.Grid(t, useChans, weights, curT, gx, gy);
                var norm = Reconstruction.Normalize(grid);
                double r = Reconstruction.PearsonVsTarget(grid, target);
                return (norm, grid, r);
            });
        }

        int ActiveChannelCount(double[][] chans)
        {
            // channels beyond what the source delivers are all-zero padding
            int active = chans.Length;
            while (active > 1)
            {
                bool allZero = true;
                foreach (var v in chans[active - 1])
                    if (v != 0) { allZero = false; break; }
                if (allZero) active--;
                else break;
            }
            return active;
        }

        string[] PaddedNames()
        {
            var baseNames = _source?.ChannelNames ?? new[] { "TP9", "AF7", "AF8", "TP10" };
            var names = new string[8];
            for (int i = 0; i < 8; i++)
                names[i] = i < baseNames.Length ? baseNames[i] : $"AUX{i - baseNames.Length + 1}";
            return names;
        }

        void TrackFlicker(bool on, double t)
        {
            if (on && !_lastFlicker) _flickerRises.Add(t);
            _lastFlicker = on;
        }

        void ComputeMeasuredFlicker()
        {
            _info.slowFrames = _governor.SlowFrames;
            _info.totalFrames = _governor.TotalFrames;
            if (_flickerRises.Count < 10) return;
            var periods = new List<double>();
            for (int i = 1; i < _flickerRises.Count; i++)
            {
                double p = _flickerRises[i] - _flickerRises[i - 1];
                if (p > 0.01 && p < 0.5) periods.Add(p);
            }
            if (periods.Count < 5) return;
            periods.Sort();
            double median = periods[periods.Count / 2];
            _info.measuredFlickerHz = 1.0 / median;
        }

        void SaveTargetCsv()
        {
            using var w = new StreamWriter(Path.Combine(_sessionDir, "target.csv"));
            for (int y = 0; y < _target.GetLength(0); y++)
            {
                var parts = new string[_target.GetLength(1)];
                for (int x = 0; x < _target.GetLength(1); x++)
                    parts[x] = _target[y, x].ToString("F4", CultureInfo.InvariantCulture);
                w.WriteLine(string.Join(",", parts));
            }
        }

        void SaveCalibrationJson()
        {
            File.WriteAllText(Path.Combine(_sessionDir, "calibration.json"),
                JsonUtility.ToJson(_calib, true));
        }

        Texture2D SaveReconPng(double[,] norm)
        {
            int h = norm.GetLength(0), wdt = norm.GetLength(1);
            const int scale = 24;
            var tex = new Texture2D(wdt * scale, h * scale, TextureFormat.RGB24, false)
            { filterMode = FilterMode.Point };
            for (int y = 0; y < h * scale; y++)
                for (int x = 0; x < wdt * scale; x++)
                {
                    float v = (float)norm[h - 1 - y / scale, x / scale];
                    tex.SetPixel(x, y, new Color(v, v, v));
                }
            tex.Apply();
            File.WriteAllBytes(Path.Combine(_sessionDir, "recon.png"),
                tex.EncodeToPNG());
            return tex;
        }

        void ShowResultQuad(Texture2D tex)
        {
            var rig = Camera.main != null ? Camera.main.transform : transform;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ReconResult";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(rig, false);
            go.transform.localPosition = new Vector3(0, -0.12f, 0.9f);
            float aspect = (float)tex.width / tex.height;
            go.transform.localScale = new Vector3(0.4f * aspect, 0.4f, 1);
            var mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = tex;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        void Finish(int code)
        {
            _recorder?.Stop();
            _info.aborted |= code == 3 || code == 4;
            File.WriteAllText(Path.Combine(_sessionDir, "session.json"),
                JsonUtility.ToJson(_info, true));
            Log($"session finished code={code} dir={_sessionDir}");
            Completed = true;
        }

        static string LocalIp()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation
                             .NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus !=
                        System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                        if (addr.Address.AddressFamily ==
                            System.Net.Sockets.AddressFamily.InterNetwork
                            && !addr.Address.ToString().StartsWith("127."))
                            return addr.Address.ToString();
                }
            }
            catch { }
            return "unknown";
        }
    }
}
