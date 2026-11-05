using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Moves the player with the Arduino Uno controller:
///   Joystick up / down    -> player goes up / down
///   Joystick left / right -> player turns left / right (or strafes, see option)
///   Button (held)         -> player goes forward
///   Joystick press        -> eat
/// Put this on the player. Works with a CharacterController if there is one, otherwise moves the transform.
/// If you already have a movement script (e.g. FirstPersonController) on the same object, disable its
/// movement or this will fight with it - you can also just read ArduinoSerial's values from that script instead.
/// </summary>
public class ArduinoPlayerMove : MonoBehaviour
{
    [Tooltip("Found automatically if left empty.")]
    public ArduinoSerial arduino;

    [Header("Speeds")]
    public float forwardSpeed = 3f;
    public float verticalSpeed = 2f;
    [Tooltip("Degrees per second when turning.")]
    public float turnSpeed = 90f;

    [Header("Options")]
    [Tooltip("Tick to slide left/right instead of turning.")]
    public bool strafeInsteadOfTurn = false;
    [Tooltip("Forward follows where this object is looking (e.g. the camera) instead of the player body. Optional.")]
    public Transform lookSource;

    [Header("Eat")]
    [Tooltip("Runs when the joystick is pressed (hook up your eat code / animation here).")]
    public UnityEvent onEat;

    CharacterController cc;

    void Start()
    {
        if (arduino == null) arduino = FindAnyObjectByType<ArduinoSerial>();
        cc = GetComponent<CharacterController>();
        if (arduino == null) Debug.LogWarning("[ArduinoPlayerMove] No ArduinoSerial found in the scene.");
    }

    void Update()
    {
        if (arduino == null || !arduino.IsConnected) return;

        float dt = Time.deltaTime;
        Transform look = lookSource != null ? lookSource : transform;

        Vector3 move = Vector3.zero;

        // Button held -> forward
        if (arduino.ForwardHeld) move += look.forward * forwardSpeed;

        // Joystick up / down -> up / down
        move += Vector3.up * (arduino.Vertical * verticalSpeed);

        // Joystick left / right -> turn or strafe
        float h = arduino.Horizontal;
        if (strafeInsteadOfTurn) move += transform.right * (h * forwardSpeed);
        else transform.Rotate(0f, h * turnSpeed * dt, 0f, Space.World);

        if (cc != null && cc.enabled) cc.Move(move * dt);
        else transform.position += move * dt;

        // Joystick press -> eat
        if (arduino.EatPressedThisFrame)
        {
            Debug.Log("[ArduinoPlayerMove] Eat!");
            onEat?.Invoke();
        }
    }
}
