using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

/// <summary>
/// Manages data flow from Muse (Native or OSC).
/// Calculates Arousal = (Beta + Gamma) / (Theta + Alpha).
/// </summary>
public class MuseOSCManager : MonoBehaviour
{
    public static MuseOSCManager Instance { get; private set; }

    [Header("Settings")]
    public int port = 5000;
    public bool useNativeMuse = true; // If false, listen for OSC on `port`

    [Header("Live Data")]
    public float Alpha;
    public float Beta;
    public float Delta;
    public float Theta;
    public float Gamma;

    [Header("Raw EEG (latest)")]
    public float EegTP9;
    public float EegAF7;
    public float EegAF8;
    public float EegTP10;
    
    [Tooltip("Calculated as (Beta + Gamma) / (Theta + Alpha)")]
    public float Arousal;

    [Header("Status")]
    public bool IsConnected;
    private float _lastMessageTime;
    
    // OSC Internals
    private UdpClient _udpClient;
    private Thread _thread;
    private bool _isRunning;
    private readonly object _lock = new object();
    private Queue<Action> _mainThreadQueue = new Queue<Action>();

    // Track absolute band powers so we can normalize for UI.
    private float _deltaAbs;
    private float _thetaAbs;
    private float _alphaAbs;
    private float _betaAbs;
    private float _gammaAbs;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (useNativeMuse)
        {
            if (MuseHandler.Instance != null)
            {
                // Subscribe to native events
                MuseHandler.Instance.OnAlphaChanged += (v) => Alpha = v;
                MuseHandler.Instance.OnBetaChanged += (v) => Beta = v;
                MuseHandler.Instance.OnThetaChanged += (v) => Theta = v;
                MuseHandler.Instance.OnDeltaChanged += (v) => Delta = v;
                MuseHandler.Instance.OnGammaChanged += (v) => Gamma = v;
                
                // Auto-scan for simplicity in this kit
                MuseHandler.Instance.StartScanning();
            }
        }
        else
        {
            StartListening();
        }
    }

    private void Update()
    {
        if (useNativeMuse)
        {
            if (MuseHandler.Instance != null)
            {
                IsConnected = MuseHandler.Instance.IsConnected;
                
                // Native Handler events update the fields, but let's calc Arousal every frame
                CalculateArousal();
            }
        }
        else
        {
            // OSC Mode
            DispatchQueue();
            if (Time.time - _lastMessageTime > 3.0f) IsConnected = false;
        }
    }

    private void CalculateArousal()
    {
        // Simple linear usage if input is relative (0-1)
        float denominator = Theta + Alpha;
        if (denominator > 0.0001f)
        {
            Arousal = (Beta + Gamma) / denominator;
        }
    }

    // ── OSC ─────────────────────────────────────────────────────────

    private void StartListening()
    {
        try
        {
            _udpClient = new UdpClient(port);
            _isRunning = true;
            _thread = new Thread(ReceiveThread);
            _thread.IsBackground = true;
            _thread.Start();
            Debug.Log($"[MuseOSC] Listening on Port {port}");
        }
        catch (Exception e) { Debug.LogError($"[MuseOSC] Error: {e.Message}"); }
    }

    private void ReceiveThread()
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, port);
        while (_isRunning)
        {
            try
            {
                byte[] data = _udpClient.Receive(ref remote);
                if (data != null && data.Length > 0)
                {
                    // OSC message(s) from MuseAI / MindMonitor style senders.
                    // We parse only what we need for the immersive monitor:
                    // - /muse/eeg (4 floats)
                    // - /muse/elements/{delta,theta,alpha,beta,gamma}_absolute (4 floats)
                    // - /person{n}/eeg ([delta,theta,alpha,beta] legacy)
                    ParseOscPacket(data);
                }
            }
            catch { }
        }
    }

    private void ParseOscPacket(byte[] data)
    {
        // Bundle support (optional): "#bundle\0" + timetag + [size][packet]...
        if (IsBundle(data))
        {
            int offset = 16; // "#bundle\0" (8) + timetag (8)
            while (offset + 4 <= data.Length)
            {
                int size = ReadInt32BE(data, offset);
                offset += 4;
                if (size <= 0 || offset + size > data.Length) break;
                byte[] sub = new byte[size];
                Buffer.BlockCopy(data, offset, sub, 0, size);
                offset += size;
                ParseOscMessage(sub);
            }
            return;
        }

        ParseOscMessage(data);
    }

    private void ParseOscMessage(byte[] data)
    {
        if (!TryReadPaddedString(data, 0, out string address, out int offset)) return;
        if (!TryReadPaddedString(data, offset, out string typeTags, out offset)) return;
        if (string.IsNullOrEmpty(typeTags) || typeTags[0] != ',') return;

        // Collect floats/ints as we go.
        var floats = new List<float>(8);
        for (int i = 1; i < typeTags.Length; i++)
        {
            char t = typeTags[i];
            switch (t)
            {
                case 'f':
                    if (offset + 4 > data.Length) return;
                    floats.Add(ReadFloatBE(data, offset));
                    offset += 4;
                    break;
                case 'i':
                    if (offset + 4 > data.Length) return;
                    floats.Add(ReadInt32BE(data, offset));
                    offset += 4;
                    break;
                case 's':
                    if (!TryReadPaddedString(data, offset, out _, out offset)) return;
                    break;
                default:
                    // Unsupported type — abort parsing this message safely.
                    return;
            }
        }

        // Dispatch on main thread (Unity API + Time.time).
        Enqueue(() =>
        {
            IsConnected = true;
            _lastMessageTime = Time.time;

            if (address == "/muse/eeg" && floats.Count >= 4)
            {
                EegTP9 = floats[0];
                EegAF7 = floats[1];
                EegAF8 = floats[2];
                EegTP10 = floats[3];
                return;
            }

            if (address.StartsWith("/muse/elements/", StringComparison.Ordinal) && floats.Count >= 1)
            {
                // MuseAI sends 4 values [TP9, AF7, AF8, TP10]. We'll average them.
                float avg = floats.Count >= 4
                    ? (floats[0] + floats[1] + floats[2] + floats[3]) / 4f
                    : floats.Average();

                if (address.EndsWith("delta_absolute", StringComparison.Ordinal)) _deltaAbs = avg;
                else if (address.EndsWith("theta_absolute", StringComparison.Ordinal)) _thetaAbs = avg;
                else if (address.EndsWith("alpha_absolute", StringComparison.Ordinal)) _alphaAbs = avg;
                else if (address.EndsWith("beta_absolute", StringComparison.Ordinal)) _betaAbs = avg;
                else if (address.EndsWith("gamma_absolute", StringComparison.Ordinal)) _gammaAbs = avg;

                NormalizeBandsFromAbs();
                CalculateArousal();
                return;
            }

            // MuseLog legacy format: /person1/eeg with [delta,theta,alpha,beta]
            if (address.StartsWith("/person", StringComparison.Ordinal) && address.EndsWith("/eeg", StringComparison.Ordinal) && floats.Count >= 4)
            {
                Delta = Mathf.Clamp01(floats[0]);
                Theta = Mathf.Clamp01(floats[1]);
                Alpha = Mathf.Clamp01(floats[2]);
                Beta = Mathf.Clamp01(floats[3]);
                // Gamma isn't provided in legacy; keep last.
                CalculateArousal();
                return;
            }
        });
    }

    private void NormalizeBandsFromAbs()
    {
        float sum = _deltaAbs + _thetaAbs + _alphaAbs + _betaAbs + _gammaAbs;
        if (sum <= 0.000001f) return;
        Delta = Mathf.Clamp01(_deltaAbs / sum);
        Theta = Mathf.Clamp01(_thetaAbs / sum);
        Alpha = Mathf.Clamp01(_alphaAbs / sum);
        Beta  = Mathf.Clamp01(_betaAbs / sum);
        Gamma = Mathf.Clamp01(_gammaAbs / sum);
    }

    private static bool IsBundle(byte[] data)
    {
        // "#bundle\0"
        return data.Length >= 8 &&
               data[0] == (byte)'#' &&
               data[1] == (byte)'b' &&
               data[2] == (byte)'u' &&
               data[3] == (byte)'n' &&
               data[4] == (byte)'d' &&
               data[5] == (byte)'l' &&
               data[6] == (byte)'e' &&
               data[7] == 0;
    }

    private static bool TryReadPaddedString(byte[] data, int start, out string value, out int nextOffset)
    {
        value = null;
        nextOffset = start;
        if (start < 0 || start >= data.Length) return false;

        int end = start;
        while (end < data.Length && data[end] != 0) end++;
        if (end >= data.Length) return false;

        value = Encoding.ASCII.GetString(data, start, end - start);
        end++; // skip null

        // pad to 4 bytes
        int padded = (end + 3) & ~3;
        nextOffset = padded;
        return true;
    }

    private static int ReadInt32BE(byte[] data, int offset)
    {
        return (data[offset] << 24) |
               (data[offset + 1] << 16) |
               (data[offset + 2] << 8) |
               data[offset + 3];
    }

    private static float ReadFloatBE(byte[] data, int offset)
    {
        int i = ReadInt32BE(data, offset);
        return BitConverter.Int32BitsToSingle(i);
    }
    
    private void Enqueue(Action action) { lock(_lock) { _mainThreadQueue.Enqueue(action); } }
    private void DispatchQueue() { lock(_lock) { while(_mainThreadQueue.Count > 0) _mainThreadQueue.Dequeue().Invoke(); } }

    private void OnDestroy()
    {
        _isRunning = false;
        _udpClient?.Close();
        _thread?.Abort();
    }
}
