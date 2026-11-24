using UnityEngine;

public class ShelfPlacement : MonoBehaviour
{
    [Header("Settings")]
    public float moveDistance = 2.5f;
    public float moveSmoothness = 15f;
    public float rotationStep = 90f;

    [Header("References")]
    public Camera playerCamera;
    public Collider placementCollider;

    private bool isHeld;
    private bool canPlace;

    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float currentRotation;
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private void Update()
    {
        if (isHeld)
            MoveShelf();
    }

    private void MoveShelf()
    {
        if (playerCamera == null)
            return;

        Vector3 targetPosition = playerCamera.transform.position
                               + playerCamera.transform.forward * moveDistance;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            Time.deltaTime * moveSmoothness
        );

        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            Quaternion.Euler(0f, currentRotation, 0f),
            Time.deltaTime * moveSmoothness
        );
    }

    public void PickUp()
    {
        if (isHeld)
            return;

        originalPosition = transform.position;
        originalRotation = transform.rotation;
        currentRotation = transform.eulerAngles.y;

        isHeld = true;
        canPlace = false;

        if (rb != null)
            rb.isKinematic = true;
    }

    public void RotateShelf()
    {
        if (isHeld)
            currentRotation += rotationStep;
    }

    public void ConfirmPlacement()
    {
        if (!isHeld || !canPlace)
            return;

        isHeld = false;
        canPlace = false;

        if (rb != null)
            rb.isKinematic = true;
    }

    public void CancelPlacement()
    {
        if (!isHeld)
            return;

        transform.SetPositionAndRotation(originalPosition, originalRotation);
        isHeld = false;
        canPlace = false;
    }

    public void SetPlacementValidity(bool valid)
    {
        canPlace = valid;
    }

    public bool IsHeld()
    {
        return isHeld;
    }

    public bool CanPlace()
    {
        return canPlace;
    }
}