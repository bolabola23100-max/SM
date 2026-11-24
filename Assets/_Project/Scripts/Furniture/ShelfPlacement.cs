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
    [SerializeField] private Color validColor = new Color(0.15f, 1f, 0.25f, 0.42f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.12f, 0.12f, 0.42f);

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

        // Create private transparent material copies. Original project materials are never modified.
        for (int i = 0; i < shelfRenderers.Length; i++)
        {
            Material[] sourceMaterials = shelfRenderers[i].sharedMaterials;
            originalMaterials[i] = sourceMaterials;
            previewMaterials[i] = new Material[sourceMaterials.Length];

            for (int j = 0; j < sourceMaterials.Length; j++)
            {
                if (sourceMaterials[j] == null)
                    continue;

                Material preview = new Material(sourceMaterials[j]);
                ConfigureTransparentMaterial(preview);
                previewMaterials[i][j] = preview;
            }
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

            shelfRenderer.sharedMaterials = previewMaterials[i];

            for (int j = 0; j < previewMaterials[i].Length; j++)
            {
                Material material = previewMaterials[i][j];
                if (material == null)
                    continue;

                Color tint = previewColor;
                if (material.HasProperty(BaseColorId))
                    material.SetColor(BaseColorId, tint);
                if (material.HasProperty(ColorId))
                    material.SetColor(ColorId, tint);
            }
        }
    }

    private void RestoreOriginalAppearance()
    {
        for (int i = 0; i < shelfRenderers.Length; i++)
        {
            if (shelfRenderers[i] != null)
                shelfRenderers[i].sharedMaterials = originalMaterials[i];
        }
    }

    private void ConfigureTransparentMaterial(Material material)
    {
        // URP Lit / compatible shaders.
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);

        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        if (material.HasProperty("_SrcBlend"))
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);

        if (material.HasProperty("_DstBlend"))
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        if (material.HasProperty("_ZWrite"))
            material.SetInt("_ZWrite", 0);

        // Built-in Standard shader.
        if (material.HasProperty("_Mode"))
            material.SetFloat("_Mode", 3f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private void OnDestroy()
    {
        if (previewMaterials == null)
            return;

        for (int i = 0; i < previewMaterials.Length; i++)
        {
            if (previewMaterials[i] == null)
                continue;

            for (int j = 0; j < previewMaterials[i].Length; j++)
            {
                if (previewMaterials[i][j] != null)
                    Destroy(previewMaterials[i][j]);
            }
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