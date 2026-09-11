using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

/// <summary>
/// Simple Grab Interactable for Quest.
/// Uses XR InputDevice directly (lightweight).
/// Attach to the object you want to grab.
/// </summary>
public class SimpleGrabbable : MonoBehaviour
{
    private bool _isHeld = false;
    private InputDevice _device;
    private Transform _originalParent;
    private bool _wasGripPressed = false;

    private void Update()
    {
        if (_isHeld)
        {
            // Check for Release
            bool gripPressed = false;
            _device.TryGetFeatureValue(CommonUsages.gripButton, out gripPressed);

            if (!gripPressed)
            {
                Release();
            }
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (_isHeld) return;

        // Simplify: Assume controller has a SphereCollider or similar
        // We look for "Controller" in name or XR components
        if (other.name.ToLower().Contains("controller") || other.name.ToLower().Contains("hand"))
        {
            // Try get device from this object? 
            // Better: Get device from active controllers
            
            // Hack for simplicity: Check both Left/Right devices
            // If any has Grip pressed while colliding -> Grab
            
            CheckGrab(InputDeviceCharacteristics.Left, other);
            CheckGrab(InputDeviceCharacteristics.Right, other);
        }
    }

    private void CheckGrab(InputDeviceCharacteristics hand, Collider other)
    {
        var device = GetDevice(hand);
        if (device.isValid)
        {
            bool grip;
            device.TryGetFeatureValue(CommonUsages.gripButton, out grip);
            
            if (grip)
            {
                // Grab!
                Grab(device, other.transform);
            }
        }
    }

    private InputDevice GetDevice(InputDeviceCharacteristics hand)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(hand | InputDeviceCharacteristics.Controller, devices);
        return devices.Count > 0 ? devices[0] : default;
    }

    private void Grab(InputDevice device, Transform parent)
    {
        _isHeld = true;
        _device = device;
        _originalParent = transform.parent;
        
        transform.SetParent(parent);
        // Optional: Snap to position or keep offset? 
        // Keep offset (don't reset local position)
        
        // Disable physics if used
        var rb = GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;
    }

    private void Release()
    {
        _isHeld = false;
        transform.SetParent(null); // World space
        
        // Re-enable physics?
        var rb = GetComponent<Rigidbody>();
        if (rb) 
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
        }
    }
}
