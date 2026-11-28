using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CheckoutStation : MonoBehaviour
{
    [SerializeField] private CheckoutManager checkoutManager;
    [SerializeField, Min(1f)] private float lookDistance = 5f;
    [SerializeField] private Color aimedColor = new Color(.25f, 1f, .35f, 1f);
    private PlayerMovement player;
    private Camera playerCamera;
    private bool highlighted;
    private Renderer[] renderers;
    private readonly Dictionary<Renderer, MaterialPropertyBlock[]> originalBlocks =
        new Dictionary<Renderer, MaterialPropertyBlock[]>();

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        CachePropertyBlocks();
    }
    void Start() { ResolveReferences(); }

    void Update()
    {
        ResolveReferences();
        if (checkoutManager != null && checkoutManager.IsActive) { SetHighlight(false); return; }
        bool aimed = IsAimedAtThisStation();
        SetHighlight(aimed);

    }

    void ResolveReferences()
    {
        if (!checkoutManager) checkoutManager = CheckoutManager.Instance;
        if (!player) player = FindFirstObjectByType<PlayerMovement>();
        if (playerCamera == null && player != null && player.playerCamera != null) playerCamera = player.playerCamera.GetComponent<Camera>();
        if (playerCamera == null) playerCamera = Camera.main;
    }

    bool HasCheckoutTag(Transform target)
    {
        for (Transform t = target; t != null; t = t.parent) if (t.CompareTag("Checkout")) return true;
        return false;
    }

    public bool IsAimedAtThisStation()
    {
        if (playerCamera == null) return false;
        RaycastHit[] hits = Physics.RaycastAll(new Ray(playerCamera.transform.position, playerCamera.transform.forward), lookDistance, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (player != null && hit.collider.transform.root == player.transform.root) continue;
            CheckoutStation station = hit.collider.GetComponentInParent<CheckoutStation>();
            if (station != null) return station == this;
            if (HasCheckoutTag(hit.collider.transform))
                return hit.collider.transform.root == transform.root || hit.collider.transform.IsChildOf(transform) || transform.IsChildOf(hit.collider.transform);
            return false;
        }
        return false;
    }

    public bool TryInteract()
    {
        ResolveReferences();
        if (checkoutManager == null || checkoutManager.IsActive || !IsAimedAtThisStation())
            return false;
        checkoutManager.EnterCheckout();
        return checkoutManager.IsActive;
    }

    void CachePropertyBlocks()
    {
        foreach (Renderer r in renderers)
        {
            if (!r) continue;
            int materialCount = r.sharedMaterials.Length;
            MaterialPropertyBlock[] saved = new MaterialPropertyBlock[materialCount];

            for (int i = 0; i < materialCount; i++)
            {
                saved[i] = new MaterialPropertyBlock();
                r.GetPropertyBlock(saved[i], i);
            }

            originalBlocks[r] = saved;
        }
    }

    void SetHighlight(bool state)
    {
        if (highlighted == state || renderers == null) return;
        highlighted = state;

        foreach (Renderer r in renderers)
        {
            if (!r) continue;

            Material[] materials = r.sharedMaterials;
            if (!state)
            {
                if (originalBlocks.TryGetValue(r, out MaterialPropertyBlock[] saved))
                {
                    for (int i = 0; i < materials.Length; i++)
                        r.SetPropertyBlock(i < saved.Length ? saved[i] : null, i);
                }
                continue;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (!material) continue;

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block, i);

                if (material.HasProperty("_BaseColor"))
                    block.SetColor("_BaseColor", aimedColor);
                if (material.HasProperty("_Color"))
                    block.SetColor("_Color", aimedColor);
                if (material.HasProperty("_EmissionColor"))
                    block.SetColor("_EmissionColor", aimedColor * 1.2f);

                r.SetPropertyBlock(block, i);
            }
        }
    }
    void OnDisable() { SetHighlight(false); }
}