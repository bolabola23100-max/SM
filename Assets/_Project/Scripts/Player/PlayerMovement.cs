using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public Transform playerCamera;
    public MobileJoystick joystick;
    public TouchLook touchLook;

    [Header("Movement")]
    public float moveSpeed = 4f;
    public float gravity = -20f;
    public float jumpHeight = 1.2f;

    [Header("Look")]
    public float mouseSensitivity = 0.05f;
    public float touchSensitivity = 0.15f;

    private CharacterController controller;

    private float verticalVelocity;
    private float cameraRotationX;

    private bool jumpRequested;

    [HideInInspector] public bool MovementLocked;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (!MovementLocked)
            MovePlayer();
        else
            jumpRequested = false;

        // Keep camera look active while checkout locks character movement.
        LookAround();
    }

    public void Jump()
    {
        jumpRequested = true;
    }

    void MovePlayer()
    {
        float x = 0f;
        float z = 0f;

        // Keyboard - New Input System
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed)
                x = -1f;

            if (Keyboard.current.dKey.isPressed)
                x = 1f;

            if (Keyboard.current.wKey.isPressed)
                z = 1f;

            if (Keyboard.current.sKey.isPressed)
                z = -1f;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                jumpRequested = true;
        }

        // Mobile Joystick
        if (joystick != null &&
            joystick.InputDirection.sqrMagnitude > 0.01f)
        {
            x = joystick.InputDirection.x;
            z = joystick.InputDirection.y;
        }

        Vector3 direction =
            transform.right * x +
            transform.forward * z;

        direction = Vector3.ClampMagnitude(direction, 1f);

        // Ground
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0)
                verticalVelocity = -2f;

            if (jumpRequested)
            {
                verticalVelocity =
                    Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }

        jumpRequested = false;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = direction * moveSpeed;

        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    void LookAround()
    {
        float mouseX = 0f;
        float mouseY = 0f;

        // New Input System Mouse
        if (Mouse.current != null)
        {
            Vector2 mouseDelta =
                Mouse.current.delta.ReadValue();

            mouseX = mouseDelta.x * mouseSensitivity;
            mouseY = mouseDelta.y * mouseSensitivity;
        }

        Vector2 touchDelta = Vector2.zero;

        if (touchLook != null)
        {
            touchDelta = touchLook.ConsumeDelta();
        }

        float lookX =
            mouseX + touchDelta.x * touchSensitivity;

        float lookY =
            mouseY + touchDelta.y * touchSensitivity;

        transform.Rotate(Vector3.up * lookX);

        cameraRotationX -= lookY;

        cameraRotationX =
            Mathf.Clamp(cameraRotationX, -80f, 80f);

        playerCamera.localRotation =
            Quaternion.Euler(cameraRotationX, 0f, 0f);
    }
}