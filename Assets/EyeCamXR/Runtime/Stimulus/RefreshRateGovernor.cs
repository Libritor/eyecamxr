using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace EyeCamXR
{
    /// <summary>
    /// Requests 120 Hz on Quest via the meta-openxr display-utilities
    /// extension, purely through reflection so the Runtime assembly never
    /// takes a compile-time dependency on the package's API surface
    /// (fallback: whatever rate the system grants — 90 Hz still renders
    /// 15 Hz exactly at 3 frames on / 3 off).
    /// Also counts dropped frames and computes the delivered flicker
    /// frequency for session.json.
    /// </summary>
    public sealed class RefreshRateGovernor
    {
        public float RequestedRate { get; private set; }
        public float ActualRate { get; private set; } = -1f;
        public int SlowFrames { get; private set; }
        public int TotalFrames { get; private set; }
        double _lastFrameTime = -1;

        public void RequestMax()
        {
            var display = GetDisplay();
            if (display == null)
            {
                ActualRate = Application.isEditor ? 60f : -1f;
                Debug.Log("[EyeCam] no XR display subsystem (editor?)");
                return;
            }
            try
            {
                float[] rates = TryGetSupportedRates(display);
                float best = 0;
                if (rates != null)
                    foreach (var r in rates) if (r > best) best = r;
                if (best <= 0) best = 120f; // Quest 3S max; harmless if refused
                if (TryRequestRate(display, best))
                    RequestedRate = best;
                if (display.TryGetDisplayRefreshRate(out float actual))
                    ActualRate = actual;
                Debug.Log($"[EyeCam] refresh requested={RequestedRate} actual={ActualRate}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EyeCam] refresh-rate control unavailable: {e.Message}");
            }
        }

        public void PollActual()
        {
            var display = GetDisplay();
            if (display != null && display.TryGetDisplayRefreshRate(out float actual))
                ActualRate = actual;
        }

        /// <summary>Call once per rendered frame during stimulus stages.</summary>
        public void CountFrame(double now)
        {
            if (_lastFrameTime >= 0)
            {
                TotalFrames++;
                double expect = ActualRate > 1 ? 1.0 / ActualRate : 1.0 / 60.0;
                if (now - _lastFrameTime > 1.5 * expect) SlowFrames++;
            }
            _lastFrameTime = now;
        }

        static XRDisplaySubsystem GetDisplay()
        {
            var list = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(list);
            return list.Count > 0 ? list[0] : null;
        }

        static MethodInfo FindExt(string method)
        {
            // UnityEngine.XR.OpenXR.Features.Meta.XRDisplaySubsystemExtensions
            // lives in Unity.XR.MetaOpenXR; resolve at runtime only.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.Contains("MetaOpenXR")) continue;
                foreach (var type in asm.GetTypes())
                {
                    if (!type.IsAbstract || !type.IsSealed) continue; // static
                    var m = type.GetMethod(method,
                        BindingFlags.Public | BindingFlags.Static);
                    if (m != null) return m;
                }
            }
            return null;
        }

        static float[] TryGetSupportedRates(XRDisplaySubsystem display)
        {
            var m = FindExt("TryGetSupportedDisplayRefreshRates");
            if (m == null) return null;
            try
            {
                var ps = m.GetParameters();
                // signature variants: (display, Allocator, out NativeArray<float>)
                // or (display, out float[]) — probe defensively.
                if (ps.Length == 2)
                {
                    var args = new object[] { display, null };
                    var ok = (bool)m.Invoke(null, args);
                    return ok ? args[1] as float[] : null;
                }
            }
            catch { }
            return null;
        }

        static bool TryRequestRate(XRDisplaySubsystem display, float rate)
        {
            var m = FindExt("TryRequestDisplayRefreshRate");
            if (m == null) return false;
            try { return (bool)m.Invoke(null, new object[] { display, rate }); }
            catch { return false; }
        }
    }
}
