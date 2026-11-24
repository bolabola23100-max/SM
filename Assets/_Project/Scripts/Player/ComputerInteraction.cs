using UnityEngine;

/// <summary>
/// Lets the player focus on the monitor of the desktop-computer prefab.
/// Assign the player camera and PlayerMovement in the Inspector, then assign
/// this component to HoldInteractionButton so the same InteractButton toggles focus.
/// </summary>
public class ComputerInteraction : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public PlayerMovement playerMovement;

    [Header("Detection")]
    [Min(0.5f)] public float interactionDistance = 3.5f;
    [Min(0.1f)] public float focusDistanceFromScreen = 0.65f;
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
        while (computerRoot != null &&
               !computerRoot.name.Contains("SM_Desktop_Computer"))
        {
            computerRoot = computerRoot.parent;
        }

        if (computerRoot == null)
            return false;

        FocusOnScreen(hit.point);
        return true;
    }

    private void FocusOnScreen(Vector3 screenPoint)
    {
        Transform cam = playerCamera.transform;
        originalCameraPosition = cam.position;
        originalCameraRotation = cam.rotation;

        Vector3 directionToScreen = (screenPoint - cam.position).normalized;
        if (directionToScreen.sqrMagnitude < 0.001f)
            directionToScreen = cam.forward;

        targetCameraPosition = screenPoint - directionToScreen * focusDistanceFromScreen;
        targetCameraRotation = Quaternion.LookRotation(screenPoint - targetCameraPosition, Vector3.up);

        SetPlayerControl(false);
        isFocused = true;
        isTransitioning = true;
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
