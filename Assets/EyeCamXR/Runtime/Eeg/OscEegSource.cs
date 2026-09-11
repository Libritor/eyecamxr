using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace EyeCamXR
{
    /// <summary>
    /// Receives /muse/eeg (4+ floats: TP9, AF7, AF8, TP10[, AUX...]) over UDP —
    /// from the MuseLog phone app or from phantom_subject.py. Parser derived
    /// from MuseLogKit's MuseOSCManager (bundle support kept); shutdown is a
    /// volatile flag + socket close, never Thread.Abort (IL2CPP-safe).
    /// </summary>
    public sealed class OscEegSource : IEegSource
    {
        readonly int _port;
        UdpClient _udp;
        Thread _thread;
        volatile bool _running;
        SessionData _store;
        readonly float[] _values = new float[8];

        public OscEegSource(int port) { _port = port; }

        public string[] ChannelNames { get; private set; } =
            { "TP9", "AF7", "AF8", "TP10" };

        public string Info => $"OSC udp:{_port}";
        public bool IsRunning => _running;

        public void Start(SessionData store)
        {
            _store = store;
            _udp = new UdpClient(_port);
            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "EyeCamOsc" };
            _thread.Start();
        }

        public void Tick() { }

        public void Stop()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            _thread?.Join(1000);
        }

        void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try { data = _udp.Receive(ref remote); }
                catch (SocketException) { continue; }  // closed or transient
                catch (ObjectDisposedException) { break; }
                double t = EegClock.Now;
                try { ParseDatagram(data, t); }
                catch (Exception e) { Debug.LogWarning($"[EyeCam] OSC parse: {e.Message}"); }
            }
        }

        void ParseDatagram(byte[] data, double t)
        {
            if (data.Length >= 8 && data[0] == '#') // "#bundle"
            {
                int idx = 16; // "#bundle\0" + 8-byte timetag
                while (idx + 4 <= data.Length)
                {
                    int size = ReadBigInt32(data, idx);
                    idx += 4;
                    if (size <= 0 || idx + size > data.Length) break;
                    ParseMessage(data, idx, size, t);
                    idx += size;
                }
            }
            else
            {
                ParseMessage(data, 0, data.Length, t);
            }
        }

        void ParseMessage(byte[] data, int offset, int length, double t)
        {
            int end = offset + length;
            string address = ReadOscString(data, ref offset, end);
            if (address == null) return;
            string tags = ReadOscString(data, ref offset, end);
            if (tags == null || tags.Length == 0 || tags[0] != ',') return;

            bool isEeg = address == "/muse/eeg" || address == "/eeg";
            if (!isEeg) return;

            int n = 0;
            for (int i = 1; i < tags.Length && n < 8; i++)
            {
                switch (tags[i])
                {
                    case 'f':
                        if (offset + 4 > end) return;
                        _values[n++] = ReadBigFloat(data, offset);
                        offset += 4;
                        break;
                    case 'i':
                        if (offset + 4 > end) return;
                        _values[n++] = ReadBigInt32(data, offset);
                        offset += 4;
                        break;
                    case 'd':
                        if (offset + 8 > end) return;
                        _values[n++] = (float)ReadBigDouble(data, offset);
                        offset += 8;
                        break;
                    case 's':
                        ReadOscString(data, ref offset, end);
                        break;
                    default:
                        return; // unknown typetag: abort message
                }
            }
            if (n >= 4) _store?.Add(t, _values, n);
        }

        static string ReadOscString(byte[] data, ref int offset, int end)
        {
            int start = offset;
            while (offset < end && data[offset] != 0) offset++;
            if (offset >= end) return null;
            string s = System.Text.Encoding.ASCII.GetString(data, start, offset - start);
            offset = start + (((offset - start) / 4) + 1) * 4; // pad to 4
            return s;
        }

        static int ReadBigInt32(byte[] d, int o) =>
            (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

        static float ReadBigFloat(byte[] d, int o)
        {
            var b = new byte[4] { d[o + 3], d[o + 2], d[o + 1], d[o] };
            return BitConverter.ToSingle(b, 0);
        }

        static double ReadBigDouble(byte[] d, int o)
        {
            var b = new byte[8];
            for (int i = 0; i < 8; i++) b[i] = d[o + 7 - i];
            return BitConverter.ToDouble(b, 0);
        }
    }
}
