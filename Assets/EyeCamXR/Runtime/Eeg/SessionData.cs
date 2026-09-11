using System;
using System.Collections.Generic;

namespace EyeCamXR
{
    /// <summary>
    /// Full-session EEG store: growable per-channel sample lists plus arrival
    /// timestamps on the EegClock. Written by the acquisition thread (OSC) or
    /// the main thread (native drain); read under lock by DSP jobs and the
    /// live rate/RMS display. 90 min at 256 Hz x 8ch is ~60 MB — fine.
    /// </summary>
    public sealed class SessionData
    {
        readonly object _lock = new object();
        readonly List<double> _t = new List<double>(1 << 20);
        readonly List<float>[] _ch;
        public readonly int ChannelCount;

        public SessionData(int channels)
        {
            ChannelCount = channels;
            _ch = new List<float>[channels];
            for (int c = 0; c < channels; c++) _ch[c] = new List<float>(1 << 20);
        }

        public void Add(double t, float[] values, int nValues)
        {
            lock (_lock)
            {
                _t.Add(t);
                for (int c = 0; c < ChannelCount; c++)
                    _ch[c].Add(c < nValues ? values[c] : 0f);
            }
        }

        public int Count { get { lock (_lock) return _t.Count; } }

        /// <summary>Samples with t &gt;= cutoff (for live rate/RMS).</summary>
        public int CountSince(double cutoff)
        {
            lock (_lock)
            {
                int idx = LowerBound(cutoff);
                return _t.Count - idx;
            }
        }

        public float[] RmsSince(double cutoff)
        {
            lock (_lock)
            {
                int idx = LowerBound(cutoff);
                int n = _t.Count - idx;
                if (n < 8) return null;
                var rms = new float[ChannelCount];
                for (int c = 0; c < ChannelCount; c++)
                {
                    double mean = 0;
                    for (int i = idx; i < _t.Count; i++) mean += _ch[c][i];
                    mean /= n;
                    double ss = 0;
                    for (int i = idx; i < _t.Count; i++)
                    {
                        double d = _ch[c][i] - mean;
                        ss += d * d;
                    }
                    rms[c] = (float)Math.Sqrt(ss / n);
                }
                return rms;
            }
        }

        /// <summary>Snapshot full arrays for DSP (copies under lock).</summary>
        public void Snapshot(out double[] t, out double[][] chans)
        {
            lock (_lock)
            {
                int n = _t.Count;
                t = _t.ToArray();
                chans = new double[ChannelCount][];
                for (int c = 0; c < ChannelCount; c++)
                {
                    chans[c] = new double[n];
                    var src = _ch[c];
                    for (int i = 0; i < n; i++) chans[c][i] = src[i];
                }
            }
        }

        /// <summary>Copy up to max rows starting at index from; returns count.
        /// vBuf must be [max, 8]. Used by the CSV recorder thread.</summary>
        public int CopyRows(int from, int max, double[] tBuf, float[,] vBuf)
        {
            lock (_lock)
            {
                int available = _t.Count - from;
                int n = Math.Min(available, max);
                if (n <= 0) return 0;
                for (int i = 0; i < n; i++)
                {
                    tBuf[i] = _t[from + i];
                    for (int c = 0; c < ChannelCount; c++)
                        vBuf[i, c] = _ch[c][from + i];
                }
                return n;
            }
        }

        int LowerBound(double v)
        {
            int lo = 0, hi = _t.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_t[mid] < v) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
