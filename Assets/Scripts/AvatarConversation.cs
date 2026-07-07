using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.XR;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Quest-side voice loop. Push-to-talk -> POST audio -> play spoken reply + chosen
/// animation. The whole turn always returns to Idle, so repeat prompts work.
/// Idle -> Recording -> Thinking -> Speaking -> Idle
/// </summary>
public class AvatarConversation : MonoBehaviour
{
    [Header("Backend")]
    public string backendUrl = "http://127.0.0.1:8000/converse";

    [Header("References")]
    public AudioSource audioSource;
    public Animator animator;

    [Header("Subtitles (optional — leave unset to disable)")]
    [Tooltip("World-space TMP text that shows what the user said.")]
    public TMP_Text userCaption;
    [Tooltip("World-space TMP text that shows the avatar's spoken reply.")]
    public TMP_Text avatarCaption;
    [Tooltip("Prefix shown before the user's transcript, e.g. \"You: \". Leave empty for none.")]
    public string userCaptionPrefix = "You: ";
    [Tooltip("Prefix shown before the avatar's reply, e.g. \"Avatar: \". Leave empty for none.")]
    public string avatarCaptionPrefix = "";

    [Header("State cues (optional — leave unset to disable)")]
    [Tooltip("TMP text that shows the current state (Listening / Thinking / Speaking).")]
    public TMP_Text statusLabel;
    public string idleStatus      = "";
    public string listeningStatus = "Listening…";
    public string thinkingStatus  = "Thinking…";
    public string speakingStatus  = "Speaking…";
    [Tooltip("Optional GameObjects toggled on only during their matching state (e.g. an icon or glow).")]
    public GameObject idleIndicator;
    public GameObject listeningIndicator;
    public GameObject thinkingIndicator;
    public GameObject speakingIndicator;
    [Tooltip("Fires on every state change with the state name (\"Idle\"/\"Recording\"/\"Thinking\"/\"Speaking\") for custom visuals.")]
    public StringEvent onStateChanged;

    [Serializable] public class StringEvent : UnityEvent<string> { }

    [Header("Face the player (optional)")]
    [Tooltip("Off = never turn. FaceOnce = turn to the player once when the reply starts, then hold. " +
             "Track = re-aim at the player every few seconds while speaking, for a more lifelike feel.")]
    public FacingMode facingMode = FacingMode.Off;
    [Tooltip("The character transform to rotate. Leave empty to use the Animator's transform.")]
    public Transform avatarRoot;
    [Tooltip("The player's head to face. Leave empty to auto-use Camera.main (the XR head).")]
    public Transform playerHead;
    [Tooltip("How fast the avatar yaws toward the player. Lower = slower, more natural.")]
    public float turnSpeedDegPerSec = 120f;
    [Tooltip("Track mode only: seconds between re-aiming at the player.")]
    public float trackInterval = 3.5f;
    [Tooltip("Add 180 if your model ends up facing away from the player when it turns.")]
    public float yawOffset = 0f;

    public enum FacingMode { Off, FaceOnceWhenResponding, TrackWhileResponding }

    Quaternion faceTarget;
    bool haveFaceTarget;
    float nextRetargetTime;

    [Header("Input")]
    public XRNode recordHand = XRNode.RightHand;

    [Header("Recording")]
    public int maxRecordSeconds = 10;
    public int sampleRate = 16000;

    [Header("Animator state names")]
    public string idleState  = "idle";
    public string thinkState = "think";
    public string talkState  = "talk";

    enum State { Idle, Recording, Thinking, Speaking }
    State state = State.Idle;

    AudioClip recordClip;
    bool wasPressed;
    bool wasValid;

    [Serializable]
    class ConverseResponse
    {
        public string transcript;
        public string reply_text;
        public string animation;
        public string audio_base64;
        public string audio_format;
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            Permission.RequestUserPermission(Permission.Microphone);
#endif
        Debug.Log("[Avatar] Start. Mic devices: " + Microphone.devices.Length);
        if (avatarRoot == null)
            avatarRoot = animator != null ? animator.transform : transform;
        SetState(State.Idle);
        PlayAnimation(idleState);
    }

    void Update()
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(recordHand);
        if (device.isValid != wasValid)
        {
            Debug.Log("[Avatar] Device at " + recordHand + " valid = " + device.isValid);
            wasValid = device.isValid;
        }

        bool pressed = false;
        if (device.isValid)
            device.TryGetFeatureValue(CommonUsages.triggerButton, out pressed);

        if (pressed && !wasPressed && state == State.Idle)
            StartRecording();
        else if (!pressed && wasPressed && state == State.Recording)
            StopRecording();

        wasPressed = pressed;
    }

    void StartRecording()
    {
        if (Microphone.devices.Length == 0) { Debug.LogWarning("[Avatar] No microphone."); return; }
        recordClip = Microphone.Start(null, false, maxRecordSeconds, sampleRate);
        SetState(State.Recording);
        // Clear last turn's subtitles so stale text doesn't linger during the new turn.
        SetCaption(userCaption, "", "");
        SetCaption(avatarCaption, "", "");
        Debug.Log("[Avatar] Recording...");
    }

    void StopRecording()
    {
        int position = Microphone.GetPosition(null);
        Microphone.End(null);
        Debug.Log("[Avatar] Recorded " + position + " samples.");

        if (recordClip == null || position <= 0) { ReturnToIdle(); return; }

        float[] full = new float[recordClip.samples * recordClip.channels];
        recordClip.GetData(full, 0);
        int valid = position * recordClip.channels;
        float[] trimmed = new float[valid];
        Array.Copy(full, trimmed, valid);

        byte[] wav = WavUtility.EncodeWav(trimmed, recordClip.frequency, recordClip.channels);
        StartCoroutine(RunTurn(wav));
    }

    IEnumerator RunTurn(byte[] wav)
    {
        SetState(State.Thinking);
        PlayAnimation(thinkState);
        Debug.Log("[Avatar] POSTing " + wav.Length + " bytes to " + backendUrl);

        var req = new UnityWebRequest(backendUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(wav);
        req.uploadHandler.contentType = "audio/wav";
        req.downloadHandler = new DownloadHandlerBuffer();
        req.timeout = 30;

        // try/finally guarantees we always return to Idle, even on error,
        // so the avatar can always take another prompt.
        try
        {
            yield return req.SendWebRequest();
            Debug.Log("[Avatar] result=" + req.result + " http=" + req.responseCode);

            if (req.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip;
                ConverseResponse resp;
                if (TryBuildClip(req.downloadHandler.text, out clip, out resp))
                {
                    SetState(State.Speaking);
                    SetCaption(userCaption, resp.transcript, userCaptionPrefix);
                    SetCaption(avatarCaption, resp.reply_text, avatarCaptionPrefix);
                    PlayAnimation(string.IsNullOrEmpty(resp.animation) ? talkState : resp.animation);
                    audioSource.clip = clip;
                    audioSource.Play();
                    Debug.Log("[Avatar] Speaking " + clip.length.ToString("0.0") + "s");
                    yield return new WaitForSeconds(clip.length);
                }
            }
            else
            {
                Debug.LogError("[Avatar] Backend error: " + req.error);
            }
        }
        finally
        {
            req.Dispose();
            SetState(State.Idle);
            PlayAnimation(idleState);
            Debug.Log("[Avatar] Idle (ready for next prompt).");
        }
    }

    // Parses the JSON and decodes the audio. Returns false (no throw) on any problem.
    bool TryBuildClip(string json, out AudioClip clip, out ConverseResponse resp)
    {
        clip = null; resp = null;
        try
        {
            resp = JsonUtility.FromJson<ConverseResponse>(json);
            if (resp == null || string.IsNullOrEmpty(resp.audio_base64))
            {
                Debug.LogWarning("[Avatar] No audio in response.");
                return false;
            }
            Debug.Log("[Avatar] Reply: \"" + resp.reply_text + "\"  animation=" + resp.animation);
            Debug.Log("[Avatar] transcript=\"" + resp.transcript + "\"");
            clip = WavUtility.ToAudioClip(Convert.FromBase64String(resp.audio_base64));
            if (clip == null) { Debug.LogError("[Avatar] WAV decode failed."); return false; }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[Avatar] Build clip failed: " + e.Message);
            return false;
        }
    }

    // Null-safe subtitle helper: does nothing if no TMP field is wired up.
    void SetCaption(TMP_Text field, string text, string prefix)
    {
        if (field != null)
            field.text = string.IsNullOrEmpty(text) ? "" : prefix + text;
    }

    // Single choke point for state transitions so visual cues always stay in sync.
    // All cue outputs are optional; unset ones are simply skipped.
    void SetState(State newState)
    {
        state = newState;

        if (statusLabel != null)
        {
            switch (newState)
            {
                case State.Recording: statusLabel.text = listeningStatus; break;
                case State.Thinking:  statusLabel.text = thinkingStatus;  break;
                case State.Speaking:  statusLabel.text = speakingStatus;  break;
                default:              statusLabel.text = idleStatus;       break;
            }
        }

        SetActiveSafe(idleIndicator,      newState == State.Idle);
        SetActiveSafe(listeningIndicator, newState == State.Recording);
        SetActiveSafe(thinkingIndicator,  newState == State.Thinking);
        SetActiveSafe(speakingIndicator,  newState == State.Speaking);

        // Turn to address the player as the reply begins.
        if (newState == State.Speaking && facingMode != FacingMode.Off)
        {
            AimAtPlayer();
            nextRetargetTime = Time.time + trackInterval;
        }

        onStateChanged?.Invoke(newState.ToString());
    }

    // Smoothly yaw the avatar toward its cached target while one is pending, and in
    // Track mode re-aim at the player periodically while speaking.
    void LateUpdate()
    {
        if (facingMode == FacingMode.Off || avatarRoot == null) return;

        if (facingMode == FacingMode.TrackWhileResponding &&
            state == State.Speaking && Time.time >= nextRetargetTime)
        {
            AimAtPlayer();
            nextRetargetTime = Time.time + trackInterval;
        }

        if (!haveFaceTarget) return;

        avatarRoot.rotation = Quaternion.RotateTowards(
            avatarRoot.rotation, faceTarget, turnSpeedDegPerSec * Time.deltaTime);

        if (Quaternion.Angle(avatarRoot.rotation, faceTarget) < 0.1f)
            haveFaceTarget = false;   // arrived; stop nudging until the next re-aim
    }

    // Point the avatar's cached target yaw at the player (horizontal only, so it
    // never tips forward/back). Does nothing if the player head can't be resolved.
    void AimAtPlayer()
    {
        Transform head = playerHead != null ? playerHead
                       : (Camera.main != null ? Camera.main.transform : null);
        if (head == null || avatarRoot == null) return;

        Vector3 dir = head.position - avatarRoot.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;

        faceTarget = Quaternion.LookRotation(dir.normalized, Vector3.up)
                   * Quaternion.Euler(0f, yawOffset, 0f);
        haveFaceTarget = true;
    }

    void SetActiveSafe(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }

    void ReturnToIdle()
    {
        SetState(State.Idle);
        PlayAnimation(idleState);
    }

    void PlayAnimation(string stateName)
    {
        if (animator != null && !string.IsNullOrEmpty(stateName))
            animator.CrossFade(stateName, 0.15f);
    }
}