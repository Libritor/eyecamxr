using System;
using UnityEngine;

namespace EyeCamXR
{
    /// <summary>
    /// All-in-one path: Muse over BLE via libmuse, through the poll-based
    /// com.eyecamxr.EyeCamMuseBridge (Assets/Plugins/Android). One drain()
    /// JNI call per frame returns a packed double[]:
    ///   [count, nCh, then per sample: tMuseUs, v0..v(nCh-1)]
    /// Arrival time anchors the batch on the EegClock; libmuse microsecond
    /// timestamps provide intra-batch spacing (same clock philosophy as the
    /// laptop stack: single-device arrival-time domain).
    /// Editor: inert (use the OSC source + phantom there).
    /// </summary>
    public sealed class NativeMuseSource : IEegSource
    {
        SessionData _store;
        bool _running;
        int _channelCount = 4;
        string _status = "init";
        readonly float[] _values = new float[8];

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _bridge;
#endif

        public string[] ChannelNames { get; private set; } =
            { "TP9", "AF7", "AF8", "TP10" };

        public string Info => $"native BLE ({_status}, {_channelCount}ch)";
        public bool IsRunning => _running;

        public void Start(SessionData store)
        {
            _store = store;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                _bridge = new AndroidJavaObject("com.eyecamxr.EyeCamMuseBridge", activity);
                bool aux = PlayerPrefs.GetInt("eyecam_aux_electrode", 0) == 1;
                _bridge.Call("autoConnect", aux);
                _running = true;
                _status = "scanning";
            }
            catch (Exception e)
            {
                _status = "error: " + e.Message;
                Debug.LogError("[EyeCam] native muse init failed: " + e);
            }
#else
            _status = "editor stub";
            Debug.LogWarning("[EyeCam] native Muse source is Android-only; " +
                             "use OSC + phantom in the editor");
#endif
        }

        public void Tick()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_bridge == null) return;
            try
            {
                _status = _bridge.Call<string>("getStatus");
                int ch = _bridge.Call<int>("getChannelCount");
                if (ch > 0 && ch != _channelCount)
                {
                    _channelCount = Math.Min(ch, 8);
                    ChannelNames = NamesFor(_channelCount);
                    Debug.Log($"[EyeCam] muse channel count: {_channelCount}");
                }
                var batch = _bridge.Call<double[]>("drain");
                if (batch == null || batch.Length < 2) return;
                int count = (int)batch[0];
                int nCh = (int)batch[1];
                if (count <= 0) return;
                double arrival = EegClock.Now;
                int stride = 1 + nCh;
                double lastUs = batch[2 + (count - 1) * stride];
                for (int i = 0; i < count; i++)
                {
                    int off = 2 + i * stride;
                    double tUs = batch[off];
                    double t = arrival - (lastUs - tUs) * 1e-6;
                    int n = Math.Min(nCh, 8);
                    for (int c = 0; c < n; c++)
                        _values[c] = (float)batch[off + 1 + c];
                    _store.Add(t, _values, n);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EyeCam] native drain: " + e.Message);
            }
#endif
        }

        public void Stop()
        {
            _running = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _bridge?.Call("disconnect"); } catch { }
            _bridge?.Dispose();
            _bridge = null;
#endif
        }

        static string[] NamesFor(int n)
        {
            switch (n)
            {
                case 5: return new[] { "TP9", "AF7", "AF8", "TP10", "AUX" };
                case 6: return new[] { "TP9", "AF7", "AF8", "TP10", "AUXL", "AUXR" };
                case 8: return new[] { "TP9", "AF7", "AF8", "TP10",
                                       "AUX1", "AUX2", "AUX3", "AUX4" };
                default: return new[] { "TP9", "AF7", "AF8", "TP10" };
            }
        }
    }
}
