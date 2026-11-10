using UnityEngine;

/// <summary>
/// First person swim controller driven entirely by Victoria's ESP32 hardware
/// (two joysticks + a potentiometer) -- no keyboard/mouse/gamepad input.
///   Joystick 1 (facing): up/down = rise/dive, left/right = turn.
///   Joystick 2 (movement): up = swim forward (in the direction you're facing),
///                           left/right = strafe, down = nothing.
///   Potentiometer: brightness of the OceanLightController's point light (the lure) --
///   full master, same as pointLight/orb/pickup-glow all being driven by the hand you turn.
///
/// Keeps the class name FirstPersonController and the public Knock() method so
/// SharkChase.cs, SharkTrigger.cs, and MessageTrigger.cs keep working unchanged --
/// they only need this type and a Knock(Vector3, float) method to exist.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Main Camera, which must be a child of this object.")]
    public Transform cameraTransform;
    [Tooltip("Found automatically if left empty.")]
    public ArduinoSerial arduino;
    [Tooltip("Found automatically if left empty. The potentiometer drives its point light directly.")]
    public OceanLightController lightController;

    [Header("Swimming")]
    public float swimSpeed = 3f;
    public float riseDiveSpeed = 2.5f;
    public float turnSpeed = 90f; // degrees/sec
    [Tooltip("Higher = snappier, lower = floatier.")]
    public float swimAcceleration = 3f;

    [Tooltip("Light level while no ESP32 is connected (handy for testing). 0-1.")]
    public float brightnessWhenDisconnected = 1f;

    [Header("Knockback / Camera Shake")]
    [Tooltip("Higher = the shove fades faster.")]
    public float knockDamping = 4f;
    [Tooltip("Max camera rotation (degrees) at full shake.")]
    public float maxShakeAngle = 6f;
    public float shakeFrequency = 25f;
    [Tooltip("Higher = the shake settles faster.")]
    public float shakeDecay = 1.8f;
    [Tooltip("Degrees the camera snaps upward when hit.")]
    public float hitKickAngle = 8f;

    CharacterController controller;
    Vector3 swimVelocity;
    Vector3 knockVelocity;
    float trauma;
    float kick;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (arduino == null)
            arduino = FindAnyObjectByType<ArduinoSerial>();

        if (lightController == null)
            lightController = GetComponent<OceanLightController>();
    }

    void Update()
    {
        // the knock-back shove fades out smoothly
        knockVelocity = Vector3.Lerp(knockVelocity, Vector3.zero, 1f - Mathf.Exp(-knockDamping * Time.deltaTime));

        HandleTurn();
        HandleSwim();
        ApplyCameraShake();
    }

    // Runs after every script's Update() (including OceanLightController's own), so this
    // always wins -- the potentiometer is the single source of truth for the light.
    void LateUpdate()
    {
        if (lightController == null) return;
        bool connected = arduino != null && arduino.IsConnected;
        float level = connected ? arduino.Brightness01 : brightnessWhenDisconnected;
        lightController.SetHardwareBrightness01(level);
    }

    /// <summary>
    /// Shove the player and shake the camera. Called by the shark.
    /// shake is 0 to 1 (1 = strongest).
    /// </summary>
    public void Knock(Vector3 impulse, float shake = 0.8f)
    {
        knockVelocity += impulse;
        trauma = Mathf.Clamp01(Mathf.Max(trauma, shake));
        kick = hitKickAngle * Mathf.Clamp01(shake);
    }

    void HandleTurn()
    {
        if (arduino == null || !arduino.IsConnected) return;
        transform.Rotate(Vector3.up * arduino.Joystick1.x * turnSpeed * Time.deltaTime);
    }

    void HandleSwim()
    {
        Vector3 target = Vector3.zero;

        if (arduino != null && arduino.IsConnected)
        {
            // joystick 2: forward relative to facing, and strafe sideways
            target += transform.forward * arduino.Joystick2.y * swimSpeed;
            target += transform.right * arduino.Joystick2.x * swimSpeed;

            // joystick 1 up/down: rise/dive
            target += Vector3.up * arduino.Joystick1.y * riseDiveSpeed;
        }

        // smooth acceleration gives the floaty underwater feel
        float t = 1f - Mathf.Exp(-swimAcceleration * Time.deltaTime);
        swimVelocity = Vector3.Lerp(swimVelocity, target, t);

        controller.Move((swimVelocity + knockVelocity) * Time.deltaTime);
    }

    void ApplyCameraShake()
    {
        if (cameraTransform == null) return;

        float shake = trauma * trauma;
        float t = Time.time * shakeFrequency;
        float nx = (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(0.3f, t) - 0.5f) * 2f;
        float nz = (Mathf.PerlinNoise(t, 0.7f) - 0.5f) * 2f;

        cameraTransform.localRotation = Quaternion.Euler(
            -kick + nx * maxShakeAngle * shake,
            ny * maxShakeAngle * shake,
            nz * maxShakeAngle * shake);

        trauma = Mathf.MoveTowards(trauma, 0f, shakeDecay * Time.deltaTime);
        kick = Mathf.Lerp(kick, 0f, 1f - Mathf.Exp(-8f * Time.deltaTime));
    }
}
