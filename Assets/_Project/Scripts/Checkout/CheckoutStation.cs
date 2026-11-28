using System.Collections.Generic;
using UnityEngine;

public class CheckoutStation : MonoBehaviour
{
    [SerializeField] private CheckoutManager checkoutManager;
    [SerializeField, Min(1f)] private float lookDistance = 5f;
    [SerializeField] private Color aimedColor = new Color(.15f, 1f, .2f, 1f);
    [SerializeField, Range(0f, 5f)] private float emissionStrength = 1.5f;

    private PlayerMovement player;
    private Camera playerCamera;
    private bool highlighted;
    private Renderer[] renderers;

    private readonly Dictionary<Renderer, MaterialPropertyBlock[]> originalBlocks =
        new Dictionary<Renderer, MaterialPropertyBlock[]>();
    private readonly Dictionary<Renderer, Material[]> runtimeMaterials =
        new Dictionary<Renderer, Material[]>();
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[64];

    private void Awake()
    {
        CacheRenderers();
        CacheOriginalAppearance();
    }

    private void Start() { ResolveReferences(); }

    private void Update()
    {
        ResolveReferences();
        bool aimed = (checkoutManager == null || !checkoutManager.IsActive) && IsAimedAtThisStation();
        SetHighlight(aimed);
    }

    private void CacheRenderers()
    {
        // Include every visible mesh beneath this cashier object, including inactive model parts.
        renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            Debug.LogWarning("[CheckoutStation] No Renderer components found under " + name);
    }

    private void CacheOriginalAppearance()
    {
        foreach (Renderer r in renderers)
        {
            if (!r) continue;
            Material[] sourceMaterials = r.sharedMaterials;
            MaterialPropertyBlock[] saved = new MaterialPropertyBlock[sourceMaterials.Length];

            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                saved[i] = new MaterialPropertyBlock();
                r.GetPropertyBlock(saved[i], i);
            }

            originalBlocks[r] = saved;
        }
    }

    private void ResolveReferences()
    {
        if (!checkoutManager) checkoutManager = CheckoutManager.Instance;
        if (!player) player = FindFirstObjectByType<PlayerMovement>();
        if (playerCamera == null && player != null && player.playerCamera != null)
            playerCamera = player.playerCamera.GetComponent<Camera>();
        if (playerCamera == null) playerCamera = Camera.main;
    }

    private bool IsAimedAtThisStation()
    {
        if (playerCamera == null) return false;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, lookDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        int nearestIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider col = hitBuffer[i].collider;
            if (!col) continue;
            if (player != null && col.transform.root == player.transform.root) continue;

            if (hitBuffer[i].distance < nearestDistance)
            {
                nearestDistance = hitBuffer[i].distance;
                nearestIndex = i;
            }
        }

        bool result = false;
        if (nearestIndex >= 0)
        {
            Transform hitTransform = hitBuffer[nearestIndex].collider.transform;
            CheckoutStation hitStation = hitTransform.GetComponentInParent<CheckoutStation>();
            result = hitStation == this;
        }

        for (int i = 0; i < count; i++) hitBuffer[i] = default;
        return result;
    }

    public bool TryInteract()
    {
        ResolveReferences();
        if ((checkoutManager == null) || checkoutManager.IsActive || !IsAimedAtThisStation())
            return false;

        checkoutManager.EnterCheckout();
        return checkoutManager.IsActive;
    }

    private void SetHighlight(bool state)
    {
        if (highlighted == state || renderers == null) return;
        highlighted = state;

        foreach (Renderer r in renderers)
        {
            if (!r) continue;

            if (!state)
            {
                if (originalBlocks.TryGetValue(r, out MaterialPropertyBlock[] saved))
                {
                    for (int i = 0; i < r.sharedMaterials.Length; i++)
                        r.SetPropertyBlock(i < saved.Length ? saved[i] : null, i);
                }

                if (runtimeMaterials.TryGetValue(r, out Material[] originals))
                {
                    r.materials = originals;
                    runtimeMaterials.Remove(r);
                }
                continue;
            }

            Material[] instances = r.materials;
            runtimeMaterials[r] = r.sharedMaterials;
            for (int i = 0; i < instances.Length; i++)
            {
                Material material = instances[i];
                if (!material) continue;

                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", aimedColor);
                if (material.HasProperty("_Color")) material.SetColor("_Color", aimedColor);
                if (material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", aimedColor * emissionStrength);
                }
            }

            // Also set a property block so SRP-batched materials receive the tint.
            for (int i = 0; i < instances.Length; i++)
            {
                Material material = instances[i];
                if (!material) continue;
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block, i);
                if (material.HasProperty("_BaseColor")) block.SetColor("_BaseColor", aimedColor);
                if (material.HasProperty("_Color")) block.SetColor("_Color", aimedColor);
                if (material.HasProperty("_EmissionColor"))
                    block.SetColor("_EmissionColor", aimedColor * emissionStrength);
                r.SetPropertyBlock(block, i);
            }
        }
    }

    private void OnDisable()
    {
        SetHighlight(false);
    }

    private void OnDestroy()
    {
        foreach (var pair in runtimeMaterials)
        {
            Material[] instances = pair.Key ? pair.Key.materials : null;
            if (instances == null) continue;
            foreach (Material material in instances)
                if (material) Destroy(material);
        }
    }
}