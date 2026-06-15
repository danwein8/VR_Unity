using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Sends a haptic (vibration) impulse to a controller each time its trigger is
/// pressed. Demonstrates the OUTPUT side of XR input using SendHapticImpulse.
/// NOTE: Only works on a real headset, not the XR Device Simulator.
/// </summary>
public class XRHapticOnPress : MonoBehaviour
{
    [Tooltip("Which hand's controller to buzz.")]
    public XRNode hand = XRNode.RightHand;

    [Tooltip("Vibration strength, 0 to 1.")]
    [Range(0f, 1f)] public float amplitude = 0.5f;

    [Tooltip("Vibration length in seconds.")]
    public float duration = 0.1f;

    private InputDevice device;
    private bool wasPressed;

    void Update()
    {
        device = InputDevices.GetDeviceAtXRNode(hand);
        if (!device.isValid) return;

        device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed);

        // Rising edge: fire once when the trigger goes from released to pressed.
        if (pressed && !wasPressed)
        {
            // channel 0, amplitude (0-1), duration in seconds.
            device.SendHapticImpulse(0u, amplitude, duration);
        }

        wasPressed = pressed;
    }
}
