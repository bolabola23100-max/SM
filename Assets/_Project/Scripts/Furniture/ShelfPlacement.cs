using System.Collections.Generic;
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
    [SerializeField] private Color validColor = new Color(0f, 1f, 0f, 1f);
    [SerializeField] private Color invalidColor = new Color(1f, 0f, 0f, 1f);

    private bool isHeld;
    private bool canPlace;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float currentRotation;
    private Rigidbody rb;

    private sealed class ProductPose { public Transform product; public Vector3 localPosition; public Quaternion localRotation; public Rigidbody body; public bool wasKinematic; public bool usedGravity; }
    private readonly List<ProductPose> heldProducts = new List<ProductPose>();

    private Renderer[] shelfRenderers;
    private Material[][] originalMaterials;
    private Material[][] previewMaterials;

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

        // Cache renderers only. Preview materials are created lazily when the
        // shelf is actually picked up, avoiding startup allocations for every shelf.
        shelfRenderers = GetComponentsInChildren<Renderer>(true);
        originalMaterials = new Material[shelfRenderers.Length][];
        previewMaterials = null;
        for (int i = 0; i < shelfRenderers.Length; i++)
            originalMaterials[i] = shelfRenderers[i] != null ? shelfRenderers[i].sharedMaterials : null;
    }

    private void Update()
    {
        if (isHeld) { MoveShelf(); UpdateHeldProducts(); }
    }

    private void MoveShelf()
    {
        if (playerCamera == null) return;

        Vector3 flatForward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;

        Vector3 targetPosition = playerCamera.transform.position + flatForward * moveDistance;
        targetPosition.y = originalPosition.y;

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSmoothness);
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            Quaternion.Euler(0f, currentRotation, 0f),
            Time.deltaTime * moveSmoothness
        );
    }

    public void PickUp()
    {
        if (isHeld) return;

        originalPosition = transform.position;
        originalRotation = transform.rotation;
        currentRotation = transform.eulerAngles.y;
        isHeld = true;
        canPlace = false;
        CaptureShelfProducts();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        EnsurePreviewMaterials();
        ApplyPlacementPreview(false);
    }

    public void RotateShelf()
    {
        if (isHeld) currentRotation += rotationStep;
    }

    public void ConfirmPlacement()
    {
        if (!isHeld || !canPlace) return;

        UpdateHeldProducts();
        RestoreProductPhysics();
        heldProducts.Clear();
        isHeld = false;
        RestoreOriginalAppearance();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    public void CancelPlacement()
    {
        if (!isHeld) return;

        transform.SetPositionAndRotation(originalPosition, originalRotation);
        UpdateHeldProducts();
        RestoreProductPhysics();
        heldProducts.Clear();
        isHeld = false;
        canPlace = false;
        RestoreOriginalAppearance();
    }

    public void SetPlacementValidity(bool valid)
    {
        if (canPlace == valid && isHeld) return;
        canPlace = valid;
        if (isHeld) ApplyPlacementPreview(valid);
    }

    private void EnsurePreviewMaterials()
    {
        if (previewMaterials != null) return;
        previewMaterials = new Material[shelfRenderers.Length][];
        for (int i = 0; i < shelfRenderers.Length; i++)
        {
            Material[] source = originalMaterials[i];
            previewMaterials[i] = source != null ? new Material[source.Length] : new Material[0];
            for (int j = 0; j < previewMaterials[i].Length; j++)
                if (source[j] != null) previewMaterials[i][j] = CreatePreviewMaterial(source[j]);
        }
    }

    private void ApplyPlacementPreview(bool valid)
    {
        Color tint = valid ? validColor : invalidColor;
        tint.a = 1f;

        for (int i = 0; i < shelfRenderers.Length; i++)
        {
            Renderer r = shelfRenderers[i];
            if (r == null) continue;

            r.sharedMaterials = previewMaterials[i];
            for (int j = 0; j < previewMaterials[i].Length; j++)
            {
                Material m = previewMaterials[i][j];
                if (m == null) continue;

                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, tint);
                if (m.HasProperty(ColorId)) m.SetColor(ColorId, tint);
            }
        }
    }

    private void RestoreOriginalAppearance()
    {
        for (int i = 0; i < shelfRenderers.Length; i++)
            if (shelfRenderers[i] != null)
                shelfRenderers[i].sharedMaterials = originalMaterials[i];
    }

    private static Material CreatePreviewMaterial(Material original)
    {
        // Unlit preview keeps the green/red color vivid regardless of scene lighting.
        Shader previewShader =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color") ??
            Shader.Find("Sprites/Default");

        Material preview = previewShader != null ? new Material(previewShader) : new Material(original);
        preview.name = "Shelf Placement Preview";
        preview.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        return preview;
    }

    private void OnDestroy()
    {
        if (previewMaterials == null) return;
        foreach (Material[] group in previewMaterials)
        {
            if (group == null) continue;
            foreach (Material material in group)
                if (material != null) Destroy(material);
        }
    }


    private void CaptureShelfProducts()
    {
        heldProducts.Clear();
        HashSet<Transform> unique = new HashSet<Transform>();
        foreach (ShelfZone zone in GetComponentsInChildren<ShelfZone>(true))
        {
            if (zone == null) continue;
            foreach (Transform product in zone.Products)
            {
                if (product == null || !unique.Add(product)) continue;
                ProductPose pose = new ProductPose
                {
                    product = product,
                    localPosition = transform.InverseTransformPoint(product.position),
                    localRotation = Quaternion.Inverse(transform.rotation) * product.rotation,
                    body = product.GetComponent<Rigidbody>()
                };
                if (pose.body != null)
                {
                    pose.wasKinematic = pose.body.isKinematic;
                    pose.usedGravity = pose.body.useGravity;
                    pose.body.isKinematic = true;
                    pose.body.useGravity = false;
                }
                heldProducts.Add(pose);
            }
        }
    }

    private void UpdateHeldProducts()
    {
        foreach (ProductPose pose in heldProducts)
            if (pose != null && pose.product != null)
                pose.product.SetPositionAndRotation(transform.TransformPoint(pose.localPosition), transform.rotation * pose.localRotation);
    }

    private void RestoreProductPhysics()
    {
        foreach (ProductPose pose in heldProducts)
            if (pose != null && pose.product != null && pose.body != null)
            {
                pose.body.isKinematic = pose.wasKinematic;
                pose.body.useGravity = pose.usedGravity;
            }
    }

    public bool IsHeld() => isHeld;
    public bool CanPlace() => canPlace;
}