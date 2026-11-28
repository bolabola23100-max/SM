using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Detects the player inside the checkout trigger and starts checkout on E or via UI.</summary>
public class CheckoutStation : MonoBehaviour
{
    [SerializeField] private CheckoutManager checkoutManager;
    private readonly HashSet<Collider> playerColliders = new HashSet<Collider>();
    private bool playerInside => playerColliders.Count > 0;

    void Awake()
    {
        if (!checkoutManager) checkoutManager = CheckoutManager.Instance;
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsPlayerCollider(other))
        {
            playerColliders.Add(other);
            if (!checkoutManager) checkoutManager = CheckoutManager.Instance;
            Debug.Log("[CheckoutStation] Player entered checkout trigger.", this);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsPlayerCollider(other))
        {
            playerColliders.Remove(other);
            Debug.Log("[CheckoutStation] Player exited checkout trigger.", this);
        }
    }

    void Update()
    {
        // Remove destroyed/disabled colliders so the station does not remain stuck.
        playerColliders.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        if (playerInside && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            Interact();
    }

    private bool IsPlayerCollider(Collider other)
    {
        return other != null && (other.GetComponent<PlayerMovement>() != null ||
            other.GetComponentInParent<PlayerMovement>() != null ||
            other.GetComponentInChildren<PlayerMovement>() != null);
    }

    public void Interact()
    {
        if (!playerInside)
        {
            Debug.LogWarning("[CheckoutStation] Interact called but player is not detected inside the trigger.", this);
            return;
        }

        if (!checkoutManager) checkoutManager = CheckoutManager.Instance;
        if (!checkoutManager)
        {
            Debug.LogError("[CheckoutStation] CheckoutManager is missing from the scene.", this);
            return;
        }

        checkoutManager.EnterCheckout();
    }
}
