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
    private MaterialPropertyBlock block;
    private readonly Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        block = new MaterialPropertyBlock();
        CacheColors();
    }

    void Start() { ResolveReferences(); }

    void Update()
    {
        ResolveReferences();
        if (checkoutManager != null && checkoutManager.IsActive) { SetHighlight(false); return; }

        bool aimed = IsAimed();
        SetHighlight(aimed);
        bool keyboardActivate = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        bool touchActivate = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
        if (aimed && (keyboardActivate || touchActivate))
        {
            if (checkoutManager != null) checkoutManager.EnterCheckout();
            else Debug.LogError("[CheckoutStation] CheckoutManager not found.", this);
        }
    }

    void ResolveReferences()
    {
        if (!checkoutManager) checkoutManager = CheckoutManager.Instance;
        if (!player) player = FindFirstObjectByType<PlayerMovement>();
        if (playerCamera == null && player != null && player.playerCamera != null)
            playerCamera = player.playerCamera.GetComponent<Camera>();
        if (playerCamera == null) playerCamera = Camera.main;
    }

    bool IsCheckoutTagged(Transform target)
    {
        // The Checkout tag may be placed on the root or any collider child.
        Transform current = target;
        while (current != null)
        {
            if (current.CompareTag("Checkout")) return true;
            current = current.parent;
        }
        return false;
    }

    bool IsAimed()
    {
        if (playerCamera == null) return false;
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, lookDistance, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            CheckoutStation station = hit.collider.GetComponentInParent<CheckoutStation>();
            if (station == this || IsCheckoutTagged(hit.collider.transform)) return true;
            // Ignore player colliders; other obstacles block targeting.
            if (player != null && hit.collider.transform.root == player.transform.root) continue;
            return false;
        }
        return false;
    }

    void CacheColors()
    {
        foreach (Renderer r in renderers)
        {
            if (!r) continue;
            foreach (Material m in r.sharedMaterials)
            {
                if (!m) continue;
                if (m.HasProperty("_BaseColor")) { originalColors[r] = m.GetColor("_BaseColor"); break; }
                if (m.HasProperty("_Color")) { originalColors[r] = m.GetColor("_Color"); break; }
            }
        }
    }

    void SetHighlight(bool state)
    {
        if (highlighted == state || renderers == null) return;
        highlighted = state;
        foreach (Renderer r in renderers)
        {
            if (!r) continue;
            r.GetPropertyBlock(block);
            Color c = state ? aimedColor : (originalColors.TryGetValue(r, out Color orig) ? orig : Color.white);
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            block.SetColor("_EmissionColor", state ? aimedColor * 1.2f : Color.black);
            r.SetPropertyBlock(block);
        }
    }

    void OnDisable() { SetHighlight(false); }
}