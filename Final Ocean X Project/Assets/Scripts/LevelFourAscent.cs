using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on a cube with a Box Collider (Is Trigger = ON) at the start of level 4.
/// When the player enters it:
///   1. STUMBLE   - the player lurches backward and the camera shakes.
///   2. FALL BACK - the camera tips up until the player is staring at the sky/surface.
///   3. FLOAT UP  - the player drifts up, slowly speeding up, then eases to a stop
///                  just under the water surface.
/// Player controls are switched off during the sequence.
/// Hook up "On Surfaced" to fade the screen, load a scene, show text, etc.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LevelFourAscent : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Found automatically from the player that enters the trigger if left empty.")]
    public FirstPersonController player;
    [Tooltip("The player's camera. Found automatically if left empty.")]
    public Transform cameraTransform;
    [Tooltip("Drag your HDRP Water Surface object here. Its Y position is the surface height.")]
    public Transform waterSurface;
    [Tooltip("Used only if no Water Surface object is assigned.")]
    public float fallbackWaterY = 0f;
    public string playerTag = "Player";

    [Header("1. Stumble")]
    public float stumbleTime = 0.9f;
    [Tooltip("How far the player is knocked backward.")]
    public float stumbleBackDistance = 1.5f;
    [Tooltip("Degrees the camera tips FORWARD at the start of the stumble.")]
    public float stumbleLurchAngle = 12f;
    [Tooltip("Max camera tilt (degrees) while stumbling.")]
    public float stumbleRoll = 8f;

    [Header("2. Fall Back")]
    public float fallTime = 1.8f;
    [Tooltip("Camera pitch when looking up. Negative = up in Unity. -80 is almost straight up.")]
    public float lookUpAngle = -80f;
    public float fallDriftBack = 1.0f;
    public float fallSink = 0.5f;

    [Header("3. Float Up")]
    public float startRiseSpeed = 0.4f;
    public float maxRiseSpeed = 3f;
    [Tooltip("How quickly the rise speeds up.")]
    public float riseAcceleration = 1f;
    [Tooltip("Gentle sideways drifting while rising.")]
    public float swayAmount = 0.3f;
    public float swaySpeed = 0.8f;
    [Tooltip("How far BELOW the surface the camera stops. Keep it positive so the camera stays underwater.")]
    public float stopBelowSurface = 0.4f;

    [Header("After")]
    [Tooltip("Give the player their controls back once they reach the surface.")]
    public bool restoreControlAtEnd = false;
    public UnityEvent onSurfaced;

    bool started;
    bool running;

    // used to keep the potentiometer light alive while the controller is off
    OceanLightController lightController;
    ArduinoSerial arduino;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (started || !other.CompareTag(playerTag)) return;

        if (player == null)
            player = other.GetComponentInParent<FirstPersonController>();
        if (player == null)
        {
            Debug.LogWarning("LevelFourAscent: no FirstPersonController found on the player.");
            return;
        }

        started = true;
        StartCoroutine(Sequence());
    }

    /// <summary>Can also be called from another script or a UnityEvent.</summary>
    public void BeginAscent()
    {
        if (started || player == null) return;
        started = true;
        StartCoroutine(Sequence());
    }

    IEnumerator Sequence()
    {
        running = true;

        Transform p = player.transform;
        CharacterController cc = player.GetComponent<CharacterController>();

        Transform cam = cameraTransform != null ? cameraTransform
                      : player.cameraTransform != null ? player.cameraTransform
                      : (Camera.main != null ? Camera.main.transform : null);

        lightController = player.lightController;
        arduino = player.arduino;

        // take over: stop the swim controls and the controller's camera shake
        player.enabled = false;
        if (cc != null) cc.enabled = false;

        // direction "backward" is opposite to where the player was facing (kept flat)
        Vector3 flatForward = Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
        Vector3 back = -flatForward;

        float startPitch = cam != null ? NormalizeAngle(cam.localEulerAngles.x) : 0f;

        // ---------- 1. STUMBLE ----------
        Vector3 stumbleStart = p.position;
        float stumbleEndPitch = startPitch;
        float t = 0f;
        while (t < stumbleTime)
        {
            float k = Mathf.Clamp01(t / stumbleTime);
            float ease = 1f - (1f - k) * (1f - k);

            p.position = stumbleStart + back * stumbleBackDistance * ease;

            if (cam != null)
            {
                // quick lurch forward, then the camera tips back; wobbling roll the whole time
                float lurch = Mathf.Sin(k * Mathf.PI) * stumbleLurchAngle;
                float roll = (Mathf.PerlinNoise(Time.time * 12f, 0.5f) - 0.5f) * 2f * stumbleRoll * (1f - k * 0.5f);
                stumbleEndPitch = startPitch + lurch;
                cam.localRotation = Quaternion.Euler(stumbleEndPitch, 0f, roll);
            }

            t += Time.deltaTime;
            yield return null;
        }

        // ---------- 2. FALL BACK (camera tips up) ----------
        Vector3 fallStart = p.position;
        float fallStartPitch = cam != null ? NormalizeAngle(cam.localEulerAngles.x) : 0f;
        t = 0f;
        while (t < fallTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / fallTime);

            p.position = fallStart + back * fallDriftBack * k + Vector3.down * fallSink * k;

            if (cam != null)
            {
                float pitch = Mathf.Lerp(fallStartPitch, lookUpAngle, k);
                float roll = Mathf.Sin(k * Mathf.PI) * stumbleRoll * 0.6f; // wobble that settles to level
                cam.localRotation = Quaternion.Euler(pitch, 0f, roll);
            }

            t += Time.deltaTime;
            yield return null;
        }

        if (cam != null)
            cam.localRotation = Quaternion.Euler(lookUpAngle, 0f, 0f);

        // ---------- 3. FLOAT UP ----------
        float surfaceY = waterSurface != null ? waterSurface.position.y : fallbackWaterY;
        float targetY = surfaceY - Mathf.Abs(stopBelowSurface);
        float riseSpeed = startRiseSpeed;
        float swayClock = 0f;

        while (true)
        {
            float currentY = cam != null ? cam.position.y : p.position.y;
            float remaining = targetY - currentY;
            if (remaining <= 0.02f) break;

            riseSpeed = Mathf.Min(maxRiseSpeed, riseSpeed + riseAcceleration * Time.deltaTime);
            // ease to a stop near the surface instead of hitting it at full speed
            float speed = Mathf.Min(riseSpeed, Mathf.Max(0.15f, remaining * 1.5f));

            swayClock += Time.deltaTime * swaySpeed;
            Vector3 sway = p.right * Mathf.Sin(swayClock) * swayAmount;

            Vector3 move = Vector3.up * speed + sway;
            // never overshoot the target height in a single frame
            float stepUp = Mathf.Min(move.y * Time.deltaTime, remaining);
            p.position += new Vector3(move.x * Time.deltaTime, stepUp, move.z * Time.deltaTime);

            yield return null;
        }

        running = false;
        onSurfaced?.Invoke();

        if (restoreControlAtEnd)
        {
            if (cc != null) cc.enabled = true;
            player.enabled = true;
        }
    }

    // The player's controller normally sets the light from the potentiometer. It is switched off
    // during the sequence, so do the same here so the light doesn't freeze.
    void LateUpdate()
    {
        if (!running || lightController == null || arduino == null) return;
        if (arduino.IsConnected)
            lightController.SetHardwareBrightness01(arduino.Brightness01);
    }

    static float NormalizeAngle(float a)
    {
        return a > 180f ? a - 360f : a;
    }
}
