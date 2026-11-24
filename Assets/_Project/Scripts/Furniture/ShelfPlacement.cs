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

    [Header("Placement Preview")]
    [SerializeField] private Color validColor = new Color(0.15f, 1f, 0.25f, 1f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.12f, 0.12f, 1f);

    private bool isHeld;
    private bool canPlace;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float currentRotation;
    private Rigidbody rb;

    private Renderer[] shelfRenderers;
    private MaterialPropertyBlock[] originalPropertyBlocks;
    private MaterialPropertyBlock previewPropertyBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        shelfRenderers = GetComponentsInChildren<Renderer>(true);
        originalPropertyBlocks = new MaterialPropertyBlock[shelfRenderers.Length];
        previewPropertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < shelfRenderers.Length; i++)
        {
            originalPropertyBlocks[i] = new MaterialPropertyBlock();
            shelfRenderers[i].GetPropertyBlock(originalPropertyBlocks[i]);
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

        // Project the camera direction onto the horizontal plane.
        // This keeps the shelf at its pickup height even when the player looks up/down.
        Vector3 flatForward = Vector3.ProjectOnPlane(
            playerCamera.transform.forward,
            Vector3.up
        ).normalized;

        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.forward;

        Vector3 targetPosition = playerCamera.transform.position + flatForward * moveDistance;
        targetPosition.y = originalPosition.y;

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
        ApplyPlacementPreview(false);

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
        RestoreOriginalAppearance();

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
        RestoreOriginalAppearance();
    }

    public void SetPlacementValidity(bool valid)
    {
        canPlace = valid;

        if (isHeld)
            ApplyPlacementPreview(valid);
    }

    private void ApplyPlacementPreview(bool valid)
    {
        Color previewColor = valid ? validColor : invalidColor;

        for (int i = 0; i < shelfRenderers.Length; i++)
        {
            Renderer shelfRenderer = shelfRenderers[i];
            if (shelfRenderer == null)
                continue;

            shelfRenderer.GetPropertyBlock(previewPropertyBlock);
            previewPropertyBlock.SetColor(BaseColorId, previewColor);
            previewPropertyBlock.SetColor(ColorId, previewColor);
            shelfRenderer.SetPropertyBlock(previewPropertyBlock);
        }
    }

    private void RestoreOriginalAppearance()
    {
        for (int i = 0; i < shelfRenderers.Length; i++)
        {
            if (shelfRenderers[i] != null)
                shelfRenderers[i].SetPropertyBlock(originalPropertyBlocks[i]);
        }
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