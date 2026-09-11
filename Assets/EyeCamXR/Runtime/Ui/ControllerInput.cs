using UnityEngine.InputSystem;

namespace EyeCamXR
{
    /// <summary>
    /// Polled input: Quest controller buttons (via the Input System's generic
    /// XR controller devices) with keyboard fallbacks for editor/tests.
    ///   Confirm = right trigger / Space
    ///   Pause   = A button (primaryButton) / P
    ///   Abort   = B button (secondaryButton) / Escape
    /// Edge-triggered (pressed this frame).
    /// </summary>
    public sealed class ControllerInput
    {
        bool _prevConfirm, _prevPause, _prevAbort;
        public bool Confirm { get; private set; }
        public bool Pause { get; private set; }
        public bool Abort { get; private set; }

        public void Poll()
        {
            bool confirm = false, pause = false, abort = false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                confirm |= kb.spaceKey.isPressed || kb.enterKey.isPressed;
                pause |= kb.pKey.isPressed;
                abort |= kb.escapeKey.isPressed;
            }

            foreach (var device in InputSystem.devices)
            {
                if (!(device is UnityEngine.InputSystem.XR.XRController ctrl))
                    continue;
                var trigger = ctrl.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("trigger");
                if (trigger != null && trigger.ReadValue() > 0.7f) confirm = true;
                var primary = ctrl.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("primaryButton");
                if (primary != null && primary.isPressed) pause = true;
                var secondary = ctrl.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("secondaryButton");
                if (secondary != null && secondary.isPressed) abort = true;
            }

            Confirm = confirm && !_prevConfirm;
            Pause = pause && !_prevPause;
            Abort = abort && !_prevAbort;
            _prevConfirm = confirm;
            _prevPause = pause;
            _prevAbort = abort;
        }
    }
}
