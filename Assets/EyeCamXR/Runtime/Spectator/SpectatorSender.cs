using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace EyeCamXR
{
#pragma warning disable 0649
    [Serializable] class MsgHello
    { public int v = 1; public string type = "hello"; public string sid; public int seq; public double t;
      public string mode; public int ch; public double fs; public int gridW; public int gridH;
      public double freq; public float refresh; }
    [Serializable] class MsgStage
    { public int v = 1; public string type = "stage"; public string sid; public int seq; public double t;
      public string stage; public string detail; }
    [Serializable] class MsgCursor
    { public int v = 1; public string type = "cursor"; public string sid; public int seq; public double t;
      public int gx; public int gy; public double lum; public int fl; }
    [Serializable] class MsgRow
    { public int v = 1; public string type = "row"; public string sid; public int seq; public double t;
      public int y; public double[] scores; }
    [Serializable] class MsgGrid
    { public int v = 1; public string type = "grid"; public string sid; public int seq; public double t;
      public int w; public int h; public double[] scores; }
    [Serializable] class MsgEeg
    { public int v = 1; public string type = "eeg"; public string sid; public int seq; public double t;
      public double rate; public float[] rms; }
    [Serializable] class MsgCalib
    { public int v = 1; public string type = "calib"; public string sid; public int seq; public double t;
      public string detail; public bool passed; public string best; public double[] weights; }
    [Serializable] class MsgLog
    { public int v = 1; public string type = "log"; public string sid; public int seq; public double t;
      public string msg; }
#pragma warning restore 0649

    /// <summary>
    /// Fire-and-forget UDP JSON to the PC spectator bridge. Never blocks, never
    /// throws to callers, silently does nothing when no host is configured.
    /// </summary>
    public sealed class SpectatorSender
    {
        readonly UdpClient _udp;
        readonly string _host;
        readonly int _port;
        readonly string _sid;
        int _seq;
        public bool Enabled => _udp != null;

        public SpectatorSender(string host, int port, string sessionId)
        {
            _host = host;
            _port = port;
            _sid = sessionId;
            if (!string.IsNullOrEmpty(host))
            {
                try { _udp = new UdpClient(); }
                catch (Exception e) { Debug.LogWarning($"[EyeCam] spectator: {e.Message}"); }
            }
        }

        void Send(object msg)
        {
            if (_udp == null) return;
            try
            {
                var json = JsonUtility.ToJson(msg);
                var bytes = Encoding.UTF8.GetBytes(json);
                _udp.Send(bytes, bytes.Length, _host, _port);
            }
            catch { /* spectator is best-effort */ }
        }

        int Next() => ++_seq;

        public void Hello(string mode, int ch, double fs, int gw, int gh,
                          double freq, float refresh) =>
            Send(new MsgHello { sid = _sid, seq = Next(), t = EegClock.Now,
                mode = mode, ch = ch, fs = fs, gridW = gw, gridH = gh,
                freq = freq, refresh = refresh });

        public void Stage(string stage, string detail = "") =>
            Send(new MsgStage { sid = _sid, seq = Next(), t = EegClock.Now,
                stage = stage, detail = detail });

        public void Cursor(int gx, int gy, double lum, bool fl) =>
            Send(new MsgCursor { sid = _sid, seq = Next(), t = EegClock.Now,
                gx = gx, gy = gy, lum = lum, fl = fl ? 1 : 0 });

        public void Row(int y, double[] scores) =>
            Send(new MsgRow { sid = _sid, seq = Next(), t = EegClock.Now,
                y = y, scores = scores });

        public void Grid(int w, int h, double[] scores) =>
            Send(new MsgGrid { sid = _sid, seq = Next(), t = EegClock.Now,
                w = w, h = h, scores = scores });

        public void Eeg(double rate, float[] rms) =>
            Send(new MsgEeg { sid = _sid, seq = Next(), t = EegClock.Now,
                rate = rate, rms = rms });

        public void Calib(string detail, bool passed, string best, double[] weights) =>
            Send(new MsgCalib { sid = _sid, seq = Next(), t = EegClock.Now,
                detail = detail, passed = passed, best = best, weights = weights });

        public void Log(string msg) =>
            Send(new MsgLog { sid = _sid, seq = Next(), t = EegClock.Now, msg = msg });

        public void Close() { try { _udp?.Close(); } catch { } }
    }
}
