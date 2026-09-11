using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// Simple immersive EEG monitor panel (world-space, no UI package dependencies).
/// Renders 4 EEG waveforms and 5 band-power bars from <see cref="MuseOSCManager"/>.
/// </summary>
public class MuseEegMonitorPanel : MonoBehaviour
{
    [Header("Layout")]
    public Vector3 localOffset = new Vector3(0f, 0.05f, 1.25f);
    public Vector2 panelSize = new Vector2(1.4f, 0.8f);

    [Header("EEG")]
    public int waveformPoints = 192;
    public float eegScaleUv = 200f; // scale incoming eeg to [-1..1]
    public float waveformHeight = 0.12f;
    public float waveformWidthPadding = 0.06f;

    [Header("Smoothing")]
    public int eegDecimate = 2; // update buffer every N frames

    private Transform _anchor;
    private GameObject _panel;
    private TextMesh _title;
    private TextMesh _status;
    private TextMesh _bottom;

    private readonly string[] _chNames = { "TP9", "AF7", "AF8", "TP10" };
    private readonly Color[] _chColors = {
        new Color(0.2f, 0.7f, 1.0f), // cyan-ish
        new Color(0.3f, 1.0f, 0.3f), // green
        new Color(1.0f, 0.75f, 0.2f), // orange
        new Color(1.0f, 0.25f, 0.25f), // red
    };

    private LineRenderer[] _lines;
    private float[][] _buffers;
    private int _writeIndex;
    private int _frameCounter;

    private Transform[] _bandBars;
    private TextMesh[] _bandLabels;
    private readonly string[] _bandNames = { "Delta", "Theta", "Alpha", "Beta", "Gamma" };
    private readonly Color[] _bandColors = {
        new Color(0.45f, 0.2f, 0.9f),
        new Color(0.1f, 0.75f, 1.0f),
        new Color(0.15f, 0.9f, 0.25f),
        new Color(1.0f, 0.85f, 0.1f),
        new Color(1.0f, 0.25f, 0.25f),
    };

    private void Awake()
    {
        _anchor = Camera.main != null ? Camera.main.transform : null;
        BuildPanel();
        InitBuffers();
    }

    private void BuildPanel()
    {
        _panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _panel.name = "Panel";
        _panel.transform.SetParent(transform, false);
        _panel.transform.localScale = new Vector3(panelSize.x, panelSize.y, 1f);

        var mr = _panel.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Unlit/Color"))
        {
            color = new Color(0.05f, 0.06f, 0.09f, 1f)
        };

        // Remove collider (so it doesn't interfere with raycasts / grabs later)
        Destroy(_panel.GetComponent<Collider>());

        _title = CreateText("Title", "MUSE EEG MONITOR", new Vector3(0f, panelSize.y * 0.43f, -0.001f), 0.09f, TextAnchor.MiddleCenter);
        _status = CreateText("Status", "OSC: Waiting", new Vector3(0f, panelSize.y * 0.35f, -0.001f), 0.05f, TextAnchor.MiddleCenter);

        _bottom = CreateText("Bottom", "", new Vector3(0f, -panelSize.y * 0.47f, -0.001f), 0.045f, TextAnchor.MiddleCenter);

        BuildWaveforms();
        BuildBandBars();
    }

    private TextMesh CreateText(string name, string text, Vector3 localPos, float size, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;

        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.anchor = anchor;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.1f;
        tm.fontSize = 64;
        tm.color = new Color(0.75f, 0.95f, 1f, 1f);

        // scale text to fit
        go.transform.localScale = Vector3.one * size;
        return tm;
    }

    private void BuildWaveforms()
    {
        _lines = new LineRenderer[4];

        float topY = panelSize.y * 0.18f;
        float spacingY = 0.14f;

        for (int ch = 0; ch < 4; ch++)
        {
            var root = new GameObject("EEG_" + _chNames[ch]);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, topY - ch * spacingY, -0.002f);

            // label
            var label = CreateText("Label", _chNames[ch], new Vector3(-panelSize.x * 0.47f, 0.02f, 0f), 0.035f, TextAnchor.MiddleLeft);
            label.color = _chColors[ch];
            label.transform.SetParent(root.transform, true);

            var lineGo = new GameObject("Line");
            lineGo.transform.SetParent(root.transform, false);
            lineGo.transform.localPosition = new Vector3(0f, 0f, 0f);

            var lr = lineGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = waveformPoints;
            lr.startWidth = 0.008f;
            lr.endWidth = 0.008f;
            lr.material = new Material(Shader.Find("Unlit/Color")) { color = _chColors[ch] };
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;

            _lines[ch] = lr;
        }
    }

    private void BuildBandBars()
    {
        _bandBars = new Transform[5];
        _bandLabels = new TextMesh[5];

        float barAreaY = -panelSize.y * 0.18f;
        float barWidth = 0.09f;
        float barMaxHeight = 0.22f;
        float gap = 0.07f;
        float startX = -((5 - 1) * (barWidth + gap)) / 2f;

        for (int i = 0; i < 5; i++)
        {
            var barRoot = new GameObject("Band_" + _bandNames[i]);
            barRoot.transform.SetParent(transform, false);
            barRoot.transform.localPosition = new Vector3(startX + i * (barWidth + gap), barAreaY, -0.0025f);

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(barRoot.transform, false);
            bar.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            bar.transform.localScale = new Vector3(barWidth, 0.01f, 0.02f);
            var r = bar.GetComponent<MeshRenderer>();
            r.material = new Material(Shader.Find("Unlit/Color")) { color = _bandColors[i] };

            _bandBars[i] = bar.transform;

            var lbl = CreateText("Label", _bandNames[i], new Vector3(0f, -0.08f, 0f), 0.03f, TextAnchor.MiddleCenter);
            lbl.color = new Color(0.85f, 0.9f, 1f, 1f);
            lbl.transform.SetParent(barRoot.transform, true);
            _bandLabels[i] = lbl;

            // store barMaxHeight in z of localScale? keep constant here
        }
    }

    private void InitBuffers()
    {
        _buffers = new float[4][];
        for (int i = 0; i < 4; i++)
        {
            _buffers[i] = new float[waveformPoints];
        }
        _writeIndex = 0;
    }

    private void LateUpdate()
    {
        // Anchor panel in front of the user's view
        if (_anchor == null && Camera.main != null) _anchor = Camera.main.transform;
        if (_anchor != null)
        {
            transform.position = _anchor.TransformPoint(localOffset);
            transform.rotation = Quaternion.LookRotation(transform.position - _anchor.position, Vector3.up);
        }

        var osc = MuseOSCManager.Instance;
        if (osc == null)
        {
            _status.text = "OSC: Manager missing";
            return;
        }

        // UI status
        _status.text = osc.IsConnected ? "OSC: Connected" : "OSC: Waiting";
        _bottom.text = $"Receiving on {GetLocalIpAddress()}:{osc.port}";

        // Update EEG buffers (decimated)
        _frameCounter++;
        if (_frameCounter % Mathf.Max(1, eegDecimate) == 0)
        {
            PushEeg(osc.EegTP9, osc.EegAF7, osc.EegAF8, osc.EegTP10);
        }

        RenderWaveforms();
        RenderBands(osc);
    }

    private void PushEeg(float tp9, float af7, float af8, float tp10)
    {
        _buffers[0][_writeIndex] = tp9;
        _buffers[1][_writeIndex] = af7;
        _buffers[2][_writeIndex] = af8;
        _buffers[3][_writeIndex] = tp10;
        _writeIndex = (_writeIndex + 1) % waveformPoints;
    }

    private void RenderWaveforms()
    {
        float width = panelSize.x - waveformWidthPadding * 2f;
        float x0 = -width / 2f;
        float dx = width / (waveformPoints - 1);

        for (int ch = 0; ch < 4; ch++)
        {
            var lr = _lines[ch];
            if (lr == null) continue;

            for (int i = 0; i < waveformPoints; i++)
            {
                int idx = (_writeIndex + i) % waveformPoints;
                float sample = _buffers[ch][idx];
                float norm = Mathf.Clamp(sample / Mathf.Max(1f, eegScaleUv), -1f, 1f);
                float y = norm * (waveformHeight * 0.5f);
                lr.SetPosition(i, new Vector3(x0 + i * dx, y, 0f));
            }
        }
    }

    private void RenderBands(MuseOSCManager osc)
    {
        // Values are normalized [0..1] in MuseOSCManager (from absolute bands).
        float[] vals = { osc.Delta, osc.Theta, osc.Alpha, osc.Beta, osc.Gamma };
        float barMaxHeight = 0.22f;

        for (int i = 0; i < 5; i++)
        {
            if (_bandBars[i] == null) continue;
            float h = Mathf.Clamp01(vals[i]) * barMaxHeight;
            var s = _bandBars[i].localScale;
            s.y = Mathf.Lerp(s.y, Mathf.Max(0.01f, h), Time.deltaTime * 10f);
            _bandBars[i].localScale = s;

            var p = _bandBars[i].localPosition;
            p.y = 0.08f + s.y * 0.5f;
            _bandBars[i].localPosition = p;
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "0.0.0.0";
    }
}

