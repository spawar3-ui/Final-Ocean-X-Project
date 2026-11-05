using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First person controller with keyboard/mouse, gamepad and Arduino joystick support.
/// Works with the new Input System, the legacy Input Manager, or both.
/// Toggle "swimMode" for underwater movement (no gravity, move where you look).
/// Arduino joystick: up/down = move forward/back, left/right = turn.
/// Double-click the joystick to make up/down swim up/down instead (needs swimMode on).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Main Camera, which must be a child of this object.")]
    public Transform cameraTransform;

    [Header("Arduino joystick")]
    [Tooltip("Found automatically if left empty.")]
    public ArduinoSerial arduino;
    [Tooltip("Degrees per second when the stick is pushed fully left/right.")]
    public float arduinoTurnSpeed = 90f;
    [Tooltip("Leave off to control the player with the joystick only (mouse look disabled).")]
    public bool useMouseLook = false;

    [Header("Walking")]
    public float walkSpeed = 3f;
    public float sprintSpeed = 5f;
    public float jumpHeight = 1.2f;
    public float gravity = -15f;

    [Header("Swimming (underwater)")]
    public bool swimMode = false;
    public float swimSpeed = 2.5f;
    public float swimSprintSpeed = 4f;
    public float swimVerticalSpeed = 2f;
    [Tooltip("Higher = snappier, lower = floatier.")]
    public float swimAcceleration = 3f;

    [Header("Look")]
    public float mouseSensitivity = 0.1f;
    public float gamepadLookSpeed = 120f; // degrees per second
    public float maxLookAngle = 85f;
    public bool lockCursor = true;

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
    float pitch;
    float verticalVelocity;
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
    }

    void Start()
    {
        SetCursorLocked(lockCursor);
    }

    void Update()
    {
        HandleCursor();
        HandleLook();
        ApplyCameraRotation();

        // The knock-back shove fades out smoothly.
        knockVelocity = Vector3.Lerp(knockVelocity, Vector3.zero, 1f - Mathf.Exp(-knockDamping * Time.deltaTime));

        if (swimMode) HandleSwim();
        else HandleWalk();
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

    // ---------------- Look ----------------

    void HandleLook()
    {
        // Arduino joystick left/right turns the player (no mouse needed).
        if (arduino != null && arduino.IsConnected)
            transform.Rotate(Vector3.up * arduino.Joystick.x * arduinoTurnSpeed * Time.deltaTime);

        if (!useMouseLook) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Vector2 look = GetLookInput();
        transform.Rotate(Vector3.up * look.x);

        pitch = Mathf.Clamp(pitch - look.y, -maxLookAngle, maxLookAngle);
    }

    // Sets the camera rotation every frame: your look angle + the hit jerk + shake.
    void ApplyCameraRotation()
    {
        if (cameraTransform == null) return;

        float shake = trauma * trauma;
        float t = Time.time * shakeFrequency;
        float nx = (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(0.3f, t) - 0.5f) * 2f;
        float nz = (Mathf.PerlinNoise(t, 0.7f) - 0.5f) * 2f;

        cameraTransform.localRotation = Quaternion.Euler(
            pitch - kick + nx * maxShakeAngle * shake,
            ny * maxShakeAngle * shake,
            nz * maxShakeAngle * shake);

        trauma = Mathf.MoveTowards(trauma, 0f, shakeDecay * Time.deltaTime);
        kick = Mathf.Lerp(kick, 0f, 1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    // ---------------- Walking ----------------

    void HandleWalk()
    {
        Vector2 move = GetMove();
        float speed = SprintHeld() ? sprintSpeed : walkSpeed;

        Vector3 horizontal = (transform.right * move.x + transform.forward * move.y);
        if (horizontal.sqrMagnitude > 1f) horizontal.Normalize();
        horizontal *= speed;

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f; // keeps the controller stuck to the ground

        if (controller.isGrounded && JumpPressed())
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontal + Vector3.up * verticalVelocity + knockVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    // ---------------- Swimming ----------------

    void HandleSwim()
    {
        Vector2 move = GetMove();
        float speed = SprintHeld() ? swimSprintSpeed : swimSpeed;

        // Move in the direction the camera is looking (forward/back), strafe sideways.
        Transform look = cameraTransform != null ? cameraTransform : transform;
        Vector3 target = look.forward * move.y + transform.right * move.x;
        if (target.sqrMagnitude > 1f) target.Normalize();
        target *= speed;

        // Swim up / down
        float vertical = 0f;
        if (UpHeld()) vertical += 1f;
        if (DownHeld()) vertical -= 1f;
        if (arduino != null && arduino.IsConnected && arduino.UpDownMode)
            vertical += arduino.Joystick.y;
        vertical = Mathf.Clamp(vertical, -1f, 1f);
        target += Vector3.up * vertical * swimVerticalSpeed;

        // Smooth acceleration gives the floaty underwater feel.
        float t = 1f - Mathf.Exp(-swimAcceleration * Time.deltaTime);
        swimVelocity = Vector3.Lerp(swimVelocity, target, t);

        controller.Move((swimVelocity + knockVelocity) * Time.deltaTime);
        verticalVelocity = 0f;
    }

    /// <summary>Call this from other scripts (e.g. when the player enters water).</summary>
    public void SetSwimMode(bool value)
    {
        swimMode = value;
        if (!value) swimVelocity = Vector3.zero;
    }

    // ---------------- Cursor ----------------

    void HandleCursor()
    {
        if (EscapePressed())
            SetCursorLocked(false);
        else if (Cursor.lockState != CursorLockMode.Locked && lockCursor && ClickPressed())
            SetCursorLocked(true);
    }

    void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ---------------- Input ----------------

    // Keyboard/gamepad movement plus the Arduino joystick's forward/back
    // (in up/down mode the stick swims up/down instead, see HandleSwim).
    Vector2 GetMove()
    {
        Vector2 move = GetMoveInput();
        if (arduino != null && arduino.IsConnected && !arduino.UpDownMode)
            move.y += arduino.Joystick.y;
        return Vector2.ClampMagnitude(move, 1f);
    }

#if ENABLE_INPUT_SYSTEM

    Vector2 GetMoveInput()
    {
        Vector2 move = Vector2.zero;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed) move.y -= 1f;
            if (kb.dKey.isPressed) move.x += 1f;
            if (kb.aKey.isPressed) move.x -= 1f;
        }

        var pad = Gamepad.current;
        if (pad != null) move += pad.leftStick.ReadValue();

        return Vector2.ClampMagnitude(move, 1f);
    }

    Vector2 GetLookInput()
    {
        Vector2 look = Vector2.zero;

        var mouse = Mouse.current;
        if (mouse != null) look += mouse.delta.ReadValue() * mouseSensitivity;

        var pad = Gamepad.current;
        if (pad != null) look += pad.rightStick.ReadValue() * gamepadLookSpeed * Time.deltaTime;

        return look;
    }

    bool JumpPressed()
    {
        return (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
    }

    bool SprintHeld()
    {
        return (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed)
            || (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);
    }

    bool UpHeld()
    {
        return (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
            || (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed);
    }

    bool DownHeld()
    {
        return (Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed))
            || (Gamepad.current != null && Gamepad.current.buttonEast.isPressed);
    }

    bool EscapePressed()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    bool ClickPressed()
    {
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

#elif ENABLE_LEGACY_INPUT_MANAGER

    Vector2 GetMoveInput()
    {
        Vector2 move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        return Vector2.ClampMagnitude(move, 1f);
    }

    Vector2 GetLookInput()
    {
        // Legacy mouse axes are already scaled by 0.1, so multiply back up.
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity * 10f;
    }

    bool JumpPressed() { return Input.GetButtonDown("Jump"); }
    bool SprintHeld() { return Input.GetKey(KeyCode.LeftShift); }
    bool UpHeld() { return Input.GetButton("Jump"); }
    bool DownHeld() { return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C); }
    bool EscapePressed() { return Input.GetKeyDown(KeyCode.Escape); }
    bool ClickPressed() { return Input.GetMouseButtonDown(0); }

#endif
}