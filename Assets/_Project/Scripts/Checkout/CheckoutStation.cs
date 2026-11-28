using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CheckoutStation : MonoBehaviour
{
    [SerializeField] private CheckoutManager checkoutManager;
    [SerializeField, Min(1f)] private float lookDistance = 5f;
    [SerializeField] private Color aimedColor = new Color(.15f, 1f, .2f, 1f);
    [SerializeField, Range(0f, 5f)] private float emissionStrength = 1.5f;

    private PlayerMovement player;
    private Camera playerCamera;
    private bool highlighted;
    private bool warnedNoProperty;
    private Renderer[] renderers;

    // Original shared materials, and the green copies used only while highlighted.
    private readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, Material[]> tintedCopies = new Dictionary<Renderer, Material[]>();
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[64];

    // Property names used by URP Lit, Built-in Standard and glTFast (the .glb importer).
    private static readonly string[] BaseColorProps = { "_BaseColor", "_Color", "baseColorFactor", "BaseColorFactor" };
    private static readonly string[] EmissionProps = { "_EmissionColor", "emissiveFactor", "EmissiveFactor" };

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            Debug.LogWarning("[CheckoutStation] No Renderer components found under " + name);
    }

    private void Start() { ResolveReferences(); }

    private void Update()
    {
        ResolveReferences();

        // Green only while aiming at the cashier. Once the player is inside checkout, the colour is removed.
        bool inCheckout = checkoutManager != null && checkoutManager.IsActive;
        SetHighlight(!inCheckout && IsAimedAtThisStation());
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
            CheckoutStation hit = hitBuffer[nearestIndex].collider.GetComponentInParent<CheckoutStation>();
            result = hit == this;
        }

        for (int i = 0; i < count; i++) hitBuffer[i] = default;
        return result;
    }

    public bool TryInteract()
    {
        ResolveReferences();
        if (checkoutManager == null || checkoutManager.IsActive || !IsAimedAtThisStation())
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

            if (!state) { Restore(r); continue; }

            Material[] source = r.sharedMaterials;
            originals[r] = source;

            Material[] copies = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (!source[i]) continue;
                copies[i] = new Material(source[i]);
                Tint(copies[i]);
            }

            tintedCopies[r] = copies;
            r.sharedMaterials = copies;
        }
    }

    private void Restore(Renderer r)
    {
        if (originals.TryGetValue(r, out Material[] orig))
        {
            r.sharedMaterials = orig;
            originals.Remove(r);
        }

        if (tintedCopies.TryGetValue(r, out Material[] copies))
        {
            foreach (Material m in copies) if (m) Destroy(m);
            tintedCopies.Remove(r);
        }
    }

    private void Tint(Material m)
    {
        bool tinted = false;

        foreach (string p in BaseColorProps)
        {
            if (!m.HasProperty(p)) continue;
            m.SetColor(p, aimedColor);
            tinted = true;
        }

        foreach (string p in EmissionProps)
        {
            if (!m.HasProperty(p)) continue;
            m.EnableKeyword("_EMISSION");
            m.SetColor(p, aimedColor * emissionStrength);
            tinted = true;
        }

        // Unknown shader: tint any colour property whose name looks like a main/base/emissive colour.
        if (!tinted)
        {
            Shader sh = m.shader;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                if (sh.GetPropertyType(i) != ShaderPropertyType.Color) continue;
                string n = sh.GetPropertyName(i);
                string low = n.ToLowerInvariant();
                if (low.Contains("base") || low.Contains("color") || low.Contains("emissi"))
                {
                    m.SetColor(n, aimedColor);
                    tinted = true;
                }
            }
        }

        if (!tinted && !warnedNoProperty)
        {
            warnedNoProperty = true;
            Debug.LogWarning("[CheckoutStation] Could not tint shader '" + m.shader.name +
                             "' - it has no colour property I recognise.");
        }
    }

    private void OnDisable() { SetHighlight(false); }
    private void OnDestroy() { SetHighlight(false); }
}