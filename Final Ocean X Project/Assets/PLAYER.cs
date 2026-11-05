using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PLAYER : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 12f;
    public float jumpHeight = 1.8f;
    public float gravity = -20f;

    [Header("Look Settings")]
    public float lookSensitivity = 0.12f;
    public Transform cameraTransform;
    public float minPitch = -85f;
    public float maxPitch = 85f;

    [Header("Head Bob")]
    public float bobSpeed = 10f;
    public float bobAmount = 0.04f;

    private CharacterController controller;
    private float pitch;
    private float verticalVelocity;
    private float defaultCameraLocalY = 1.4f;
    private float bobTimer;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        
        // Find camera if not assigned
        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cameraTransform = cam.transform;
            }
            else
            {
                // Look for camera in scene tagged MainCamera
                Camera main = Camera.main;
                if (main != null && main.transform.IsChildOf(transform))
                {
                    cameraTransform = main.transform;
                }
            }
        }

        if (cameraTransform != null)
        {
            defaultCameraLocalY = cameraTransform.localPosition.y;
            if (defaultCameraLocalY < 0.2f) defaultCameraLocalY = 1.4f;
        }
    }

    private void Start()
    {
        LockCursor();
    }

    private void OnEnable()
    {
        LockCursor();
    }

    private void Update()
    {
        // 1. Mouse Look
        Vector2 mouseDelta = Vector2.zero;
        if (Mouse.current != null)
        {
            mouseDelta = Mouse.current.delta.ReadValue();

            // Auto-lock cursor on click
            if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
            {
                LockCursor();
            }
        }

        // Apply Mouse Look
        float yaw = mouseDelta.x * lookSensitivity;
        transform.Rotate(0f, yaw, 0f);

        pitch -= mouseDelta.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        if (cameraTransform != null)
        {
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // 2. Keyboard Input Reading
        Vector2 moveInput = Vector2.zero;
        bool isSprinting = false;
        bool jumpPressed = false;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveInput.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveInput.y -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveInput.x += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveInput.x -= 1f;

            isSprinting = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            jumpPressed = Keyboard.current.spaceKey.wasPressedThisFrame;

            // Any movement key locks cursor
            if (moveInput.sqrMagnitude > 0.01f && Cursor.lockState != CursorLockMode.Locked)
            {
                LockCursor();
            }
        }

        // 3. Grounding & Slope handling
        bool isGrounded = controller.isGrounded;
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -4f; // Ground snap for steep terrain
        }

        // 4. Calculate Motion
        float speed = isSprinting ? sprintSpeed : walkSpeed;
        Vector3 moveDir = (transform.forward * moveInput.y + transform.right * moveInput.x);
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        Vector3 motion = moveDir * speed;

        // Jump
        if (jumpPressed && isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Gravity
        verticalVelocity += gravity * Time.deltaTime;
        motion.y = verticalVelocity;

        // Move controller
        controller.Move(motion * Time.deltaTime);

        // 5. Head Bobbing
        if (cameraTransform != null && isGrounded && moveDir.sqrMagnitude > 0.01f)
        {
            bobTimer += Time.deltaTime * (isSprinting ? bobSpeed * 1.35f : bobSpeed);
            float bobOffset = Mathf.Sin(bobTimer) * (isSprinting ? bobAmount * 1.4f : bobAmount);
            Vector3 camPos = cameraTransform.localPosition;
            camPos.y = defaultCameraLocalY + bobOffset;
            cameraTransform.localPosition = camPos;
        }
        else if (cameraTransform != null)
        {
            bobTimer = 0f;
            Vector3 camPos = cameraTransform.localPosition;
            camPos.y = Mathf.Lerp(camPos.y, defaultCameraLocalY, Time.deltaTime * 8f);
            cameraTransform.localPosition = camPos;
        }
    }

    public void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

