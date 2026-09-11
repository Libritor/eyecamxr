using System.Diagnostics;

namespace EyeCamXR
{
    /// <summary>
    /// One clock for stimulus logs, EEG arrival stamps and session events.
    /// Thread-safe (Stopwatch), monotonic, seconds. Mirrors the role of
    /// pylsl.local_clock in the laptop stack.
    /// </summary>
    public static class EegClock
    {
        static readonly Stopwatch Sw = Stopwatch.StartNew();

        public static double Now => Sw.Elapsed.TotalSeconds;
    }
}
