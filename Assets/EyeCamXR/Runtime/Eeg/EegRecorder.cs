using System;
using System.IO;
using System.Threading;

namespace EyeCamXR
{
    /// <summary>
    /// Streams SessionData rows to eeg.csv (analysis schema:
    /// time,TP9,AF7,AF8,TP10[,AUX...]) on a background thread, ~4 Hz batches,
    /// flushed once per second. Same contract as the laptop stack's recorders.
    /// </summary>
    public sealed class EegRecorder
    {
        readonly SessionData _store;
        readonly string _path;
        readonly string[] _names;
        Thread _thread;
        volatile bool _running;
        int _written;

        public EegRecorder(SessionData store, string path, string[] names)
        {
            _store = store;
            _path = path;
            _names = names;
        }

        public void Start()
        {
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "EyeCamEegCsv" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(3000);
        }

        void Loop()
        {
            using var w = new StreamWriter(_path, false);
            w.Write("time");
            foreach (var n in _names) w.Write("," + n);
            w.Write("\n");

            var tBuf = new double[512];
            var vBuf = new float[512, 8];
            double lastFlush = EegClock.Now;
            while (true)
            {
                int got = _store.CopyRows(_written, 512, tBuf, vBuf);
                if (got > 0)
                {
                    int nCh = _store.ChannelCount;
                    for (int i = 0; i < got; i++)
                    {
                        w.Write(tBuf[i].ToString("F6",
                            System.Globalization.CultureInfo.InvariantCulture));
                        for (int c = 0; c < nCh; c++)
                        {
                            w.Write(',');
                            w.Write(vBuf[i, c].ToString("F4",
                                System.Globalization.CultureInfo.InvariantCulture));
                        }
                        w.Write("\n");
                    }
                    _written += got;
                }
                else if (!_running)
                {
                    break; // drained after stop
                }
                else
                {
                    Thread.Sleep(100);
                }
                if (EegClock.Now - lastFlush > 1.0)
                {
                    w.Flush();
                    lastFlush = EegClock.Now;
                }
            }
            w.Flush();
        }
    }
}
