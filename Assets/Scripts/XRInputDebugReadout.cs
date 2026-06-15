using UnityEngine;
using UnityEngine.XR;
using TMPro;
using System.Text;

/// <summary>
/// Reads live input features from one XR controller every frame and prints them
/// to a TextMeshPro field. Demonstrates buttons, analog axes, 6DoF tracking, and
/// velocity using the UnityEngine.XR InputDevices / CommonUsages API.
/// NOTE: Only works on a real headset, not the XR Device Simulator.
/// </summary>
public class XRInputDebugReadout : MonoBehaviour
{
    [Tooltip("The TextMeshPro field that displays the live input values.")]
    public TMP_Text display;

    [Tooltip("Which hand's controller to read.")]
    public XRNode hand = XRNode.RightHand;

    private InputDevice device;
    private readonly StringBuilder sb = new StringBuilder();

    void Update()
    {
        // Re-acquire the device each frame so the readout survives a reconnect.
        device = InputDevices.GetDeviceAtXRNode(hand);

        if (!device.isValid)
        {
            if (display != null)
                display.text = $"{hand}: no controller detected";
            return;
        }

        // Each TryGetFeatureValue asks the device for one named feature.
        // It returns false (and the value stays default) if unsupported.
        device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary);
        device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondary);
        device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerPressed);
        device.TryGetFeatureValue(CommonUsages.trigger, out float trigger);
        device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripPressed);
        device.TryGetFeatureValue(CommonUsages.grip, out float grip);
        device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stickClick);
        device.TryGetFeatureValue(CommonUsages.menuButton, out bool menu);
        device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos);
        device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot);
        device.TryGetFeatureValue(CommonUsages.deviceVelocity, out Vector3 velocity);

        sb.Clear();
        sb.AppendLine($"<b>{hand}</b>");
        sb.AppendLine($"A/X (primary):   {primary}");
        sb.AppendLine($"B/Y (secondary): {secondary}");
        sb.AppendLine($"Trigger:  {trigger:F2}   pressed: {triggerPressed}");
        sb.AppendLine($"Grip:     {grip:F2}   pressed: {gripPressed}");
        sb.AppendLine($"Stick:    ({stick.x:F2}, {stick.y:F2})   click: {stickClick}");
        sb.AppendLine($"Menu:     {menu}");
        sb.AppendLine($"Position: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
        sb.AppendLine($"Rotation: ({rot.eulerAngles.x:F0}, {rot.eulerAngles.y:F0}, {rot.eulerAngles.z:F0})");
        sb.AppendLine($"Speed:    {velocity.magnitude:F2} m/s");

        if (display != null)
            display.text = sb.ToString();
    }
}
