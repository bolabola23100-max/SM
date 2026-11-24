using UnityEngine;

/// <summary>
/// Focuses the player camera on the FRONT-CENTER of the computer monitor.
/// Assign Screen Surface to the monitor's front-facing transform for the
/// most reliable orientation. The same InteractButton toggles focus on/off.
/// </summary>
public class ComputerInteraction : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public PlayerMovement playerMovement;
    [Tooltip("Optional: assign the monitor's front-facing transform. If empty, the hit collider is used.")]
    public Transform screenSurface;

    [Header("Detection")]
    [Min(0.5f)] public float interactionDistance = 3.5f;
    [Min(0.1f)] public float focusDistanceFromScreen = 0.35f;
    public LayerMask detectionMask = Physics.DefaultRaycastLayers;

    [Header("Camera Motion")]
    [Min(0.1f)] public float transitionSpeed = 5f;
    [Min(0.1f)] public float positionTolerance = 0.015f;
    [Min(0.1f)] public float rotationTolerance = 0.5f;

    private bool isFocused;
    private bool isTransitioning;
    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;
    private Vector3 targetCameraPosition;
    private Quaternion targetCameraRotation;

    public bool IsFocused => isFocused;

    private void Update()
    {
        if (!isTransitioning || playerCamera == null)
            return;

        Transform cam = playerCamera.transform;
        cam.position = Vector3.Lerp(cam.position, targetCameraPosition, Time.deltaTime * transitionSpeed);
        cam.rotation = Quaternion.Slerp(cam.rotation, targetCameraRotation, Time.deltaTime * transitionSpeed);

        if (Vector3.Distance(cam.position, targetCameraPosition) <= positionTolerance &&
            Quaternion.Angle(cam.rotation, targetCameraRotation) <= rotationTolerance)
        {
            cam.SetPositionAndRotation(targetCameraPosition, targetCameraRotation);
            isTransitioning = false;
        }
    }

    /// <summary>Called by HoldInteractionButton on a short tap.</summary>
    public bool TryInteract()
    {
        if (playerCamera == null)
            return false;

        if (isFocused || isTransitioning)
        {
            ReturnToPlayerView();
            return true;
        }

        if (!Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                out RaycastHit hit, interactionDistance, detectionMask, QueryTriggerInteraction.Ignore))
            return false;

        Transform computerRoot = hit.collider.transform;
        while (computerRoot != null && !computerRoot.name.Contains("SM_Desktop_Computer"))
            computerRoot = computerRoot.parent;

        if (computerRoot == null)
            return false;

        // Use the monitor's full bounds, not the exact pixel/point hit by the ray.
        // This makes taps aimed at any part of the screen focus the same center point.
        Transform surface = screenSurface != null ? screenSurface : hit.collider.transform;
        // ScreenSurface is an empty transform placed at the exact center of the display.
        // Use its position rather than the computer's large body collider center.
        Vector3 screenCenter = surface.position;

        FocusOnScreen(surface, screenCenter);
        return true;
    }

    private void FocusOnScreen(Transform surface, Vector3 screenCenter)
    {
        Transform cam = playerCamera.transform;
        originalCameraPosition = cam.position;
        originalCameraRotation = cam.rotation;

        // The surface's forward axis represents the monitor's outward/front direction.
        // Flip it when the player is on the opposite side, so the camera never moves behind it.
        Vector3 front = surface.forward.normalized;
        if (Vector3.Dot(cam.position - screenCenter, front) < 0f)
            front = -front;

        Vector3 up = surface.up.sqrMagnitude > 0.001f ? surface.up : Vector3.up;
        targetCameraPosition = screenCenter + front * focusDistanceFromScreen;
        targetCameraRotation = Quaternion.LookRotation(screenCenter - targetCameraPosition, up);

        SetPlayerControl(false);
        isFocused = true;
        isTransitioning = true;
    }

    /// <summary>Called by the monitor's on-screen EXIT COMPUTER button.</summary>
    public void ExitComputer()
    {
        if (isFocused || isTransitioning)
            ReturnToPlayerView();
    }

    private void ReturnToPlayerView()
    {
        targetCameraPosition = originalCameraPosition;
        targetCameraRotation = originalCameraRotation;
        isFocused = false;
        isTransitioning = true;
    }

    private void SetPlayerControl(bool enabledState)
    {
        if (playerMovement != null)
            playerMovement.enabled = enabledState;
    }

    private void LateUpdate()
    {
        if (!isFocused && !isTransitioning)
            SetPlayerControl(true);
    }
}
