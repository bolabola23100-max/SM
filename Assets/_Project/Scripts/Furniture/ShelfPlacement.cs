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
    [SerializeField] private Color validColor = new Color(0.05f, 1f, 0.08f, 1f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.03f, 0.03f, 1f);

    private bool isHeld;
    private bool canPlace;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float currentRotation;
    private Rigidbody rb;

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

        shelfRenderers = GetComponentsInChildren<Renderer>(true);
        originalMaterials = new Material[shelfRenderers.Length][];
        previewMaterials = new Material[shelfRenderers.Length][];

        for (int i = 0; i < shelfRenderers.Length; i++)
        {
            Material[] source = shelfRenderers[i].sharedMaterials;
            originalMaterials[i] = source;
            previewMaterials[i] = new Material[source.Length];

            for (int j = 0; j < source.Length; j++)
            {
                if (source[j] == null) continue;

                Material preview = new Material(source[j]);
                ConfigureOpaquePreview(preview);
                previewMaterials[i][j] = preview;
            }
        }
    }

    private void Update()
    {
        if (isHeld) MoveShelf();
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

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        ApplyPlacementPreview(false);
    }

    public void RotateShelf()
    {
        if (isHeld) currentRotation += rotationStep;
    }

    public void ConfirmPlacement()
    {
        if (!isHeld || !canPlace) return;

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

    private static void ConfigureOpaquePreview(Material material)
    {
        // Keep preview fully opaque. Only the tint changes to green/red.
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
        if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 0f);
        if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 1);

        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

        if (material.HasProperty(BaseColorId))
        {
            Color c = material.GetColor(BaseColorId);
            c.a = 1f;
            material.SetColor(BaseColorId, c);
        }
        if (material.HasProperty(ColorId))
        {
            Color c = material.GetColor(ColorId);
            c.a = 1f;
            material.SetColor(ColorId, c);
        }
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

    public bool IsHeld() => isHeld;
    public bool CanPlace() => canPlace;
}