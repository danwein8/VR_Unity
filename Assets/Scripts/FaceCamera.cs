using UnityEngine;

/// <summary>
/// Keeps world-space UI (e.g. the subtitle canvas) readable in VR.
/// Always billboards toward the player's camera. Optionally *follows* the player,
/// smoothly repositioning to a point in front of the head so captions stay in view
/// as the player walks and turns. Attach to the SubtitleCanvas.
/// Leave <see cref="target"/> empty to auto-use Camera.main (the XR head).
/// </summary>
[DisallowMultipleComponent]
public class FaceCamera : MonoBehaviour
{
    [Tooltip("The player's head/camera to face. Leave empty to auto-use Camera.main (the XR head).")]
    public Transform target;

    [Tooltip("Only rotate around Y so the panel stays upright (recommended for text).")]
    public bool yawOnly = true;

    [Header("Follow the player (optional)")]
    [Tooltip("On = the panel moves to stay in front of the player. Off = fixed in the world, just billboards.")]
    public bool followPlayer = false;

    [Tooltip("Metres in front of the head to sit.")]
    public float followDistance = 1.5f;

    [Tooltip("Vertical offset from eye height. Negative sits it a bit below centre so it doesn't block the view.")]
    public float verticalOffset = -0.25f;

    [Tooltip("Follow damping. Higher = laggier and calmer (more comfortable); 0 = rigidly glued (can feel nauseating).")]
    public float followSmoothTime = 0.35f;

    [Tooltip("Dead zone: only re-centre once the target point drifts more than this many metres from the panel. " +
             "Keeps text still while you read, re-centres when you look away. 0 = follow continuously.")]
    public float recenterThreshold = 0.35f;

    Vector3 followVel;      // SmoothDamp state
    bool recentring;

    void LateUpdate()
    {
        if (target == null)
        {
            if (Camera.main == null) return;   // XR head not ready yet
            target = Camera.main.transform;
        }

        if (followPlayer)
        {
            // Flatten the head's forward so the panel doesn't bob when you look up/down.
            Vector3 fwd = target.forward;
            if (yawOnly) { fwd.y = 0f; fwd.Normalize(); }
            Vector3 desired = target.position + fwd * followDistance + Vector3.up * verticalOffset;

            // Dead zone: leave the panel put until it drifts far enough, then chase until it arrives.
            float drift = Vector3.Distance(transform.position, desired);
            if (drift > recenterThreshold) recentring = true;
            if (recentring)
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position, desired, ref followVel, followSmoothTime);
                if (drift < 0.02f) recentring = false;
            }
        }

        // Billboard: a world-space panel reads correctly when its +Z points away from the viewer.
        Vector3 dir = transform.position - target.position;
        if (yawOnly) dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}