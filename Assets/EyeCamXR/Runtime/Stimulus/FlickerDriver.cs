using System;
using UnityEngine;

namespace EyeCamXR
{
    /// <summary>
    /// Flicker state authority. Two modes:
    ///  - frame-locked (device): display-frame counter, halfFrames on / off —
    ///    at 120 Hz: 4/4 = exactly 15 Hz, phase-continuous across cells.
    ///  - time-locked (editor/tests): wall-clock phase, exact regardless of a
    ///    variable editor frame rate.
    /// The stimulus logger writes a row every frame AND on every state change
    /// (so flicker edges survive low editor frame rates for the phantom).
    /// </summary>
    public sealed class FlickerDriver
    {
        readonly double _freq;
        readonly bool _timeLocked;
        int _halfFrames = 2;
        long _frame;
        double _t0 = -1;

        public FlickerDriver(double freqHz, bool timeLocked, float refreshRate)
        {
            _freq = freqHz;
            _timeLocked = timeLocked;
            if (refreshRate > 1f)
                _halfFrames = Math.Max(1,
                    (int)Math.Round(refreshRate / (2.0 * freqHz)));
        }

        public void UpdateRefresh(float refreshRate)
        {
            if (refreshRate > 1f)
                _halfFrames = Math.Max(1,
                    (int)Math.Round(refreshRate / (2.0 * _freq)));
        }

        public int HalfFrames => _halfFrames;

        /// <summary>Advance one display frame; returns current on/off state.</summary>
        public bool Step(double now)
        {
            if (_timeLocked)
            {
                if (_t0 < 0) _t0 = now;
                long half = (long)((now - _t0) * _freq * 2.0);
                return (half & 1) == 0;
            }
            bool on = (_frame / _halfFrames) % 2 == 0;
            _frame++;
            return on;
        }
    }
}
