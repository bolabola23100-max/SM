using UnityEngine;
using UnityEngine.InputSystem;

public class ProductInteraction : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform holdPoint;

    [Header("Settings")]
    public float interactionDistance = 3.5f;

    private Rigidbody heldProduct;
    private Collider[] productColliders;
    private bool[] originalColliderStates;
    private bool isHolding;

    public bool IsHoldingProduct => isHolding && heldProduct != null;

    private void Update()
    {
        // Keyboard support for testing in the Editor.
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            Interact();
    }

    private void LateUpdate()
    {
        if (!isHolding)
            return;

        if (heldProduct == null)
        {
            ClearHeldState();
            return;
        }

        if (holdPoint != null)
        {
            heldProduct.transform.SetPositionAndRotation(
                holdPoint.position,
                holdPoint.rotation
            );
        }
    }

    // Called by the mobile interaction button or the Editor keyboard.
    public void Interact()
    {
        if (IsHoldingProduct)
            DropProduct();
        else
            TryPickupBox();
    }

    private void TryPickupBox()
    {
        if (playerCamera == null || holdPoint == null)
            return;

        if (!Physics.Raycast(
                playerCamera.transform.position,
                playerCamera.transform.forward,
                out RaycastHit hit,
                interactionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return;

        // Do not allow shelves, loose products, or arbitrary Rigidbody objects to be picked up.
        if (!HasTagInParents(hit.collider.transform, "Box"))
            return;

        Rigidbody box = hit.collider.GetComponentInParent<Rigidbody>();
        if (box != null)
            PickupProduct(box);
    }

    private void PickupProduct(Rigidbody product)
    {
        if (product == null || holdPoint == null)
            return;

        heldProduct = product;
        isHolding = true;

        productColliders = product.GetComponentsInChildren<Collider>(true);
        originalColliderStates = new bool[productColliders.Length];

        for (int i = 0; i < productColliders.Length; i++)
        {
            Collider current = productColliders[i];
            if (current == null)
                continue;

            originalColliderStates[i] = current.enabled;
            current.enabled = false;
        }

        heldProduct.transform.SetParent(null, true);
        heldProduct.isKinematic = true;
        heldProduct.useGravity = false;
        heldProduct.linearVelocity = Vector3.zero;
        heldProduct.angularVelocity = Vector3.zero;

        heldProduct.transform.SetPositionAndRotation(
            holdPoint.position,
            holdPoint.rotation
        );
    }

    private void DropProduct()
    {
        if (heldProduct == null)
        {
            ClearHeldState();
            return;
        }

        Vector3 dropPosition = heldProduct.transform.position;
        if (playerCamera != null)
        {
            dropPosition = playerCamera.transform.position
                         + playerCamera.transform.forward * 1.5f;
        }

        heldProduct.transform.SetParent(null, true);
        heldProduct.transform.position = dropPosition;

        if (productColliders != null)
        {
            for (int i = 0; i < productColliders.Length; i++)
            {
                if (productColliders[i] != null)
                    productColliders[i].enabled = originalColliderStates[i];
            }
        }

        heldProduct.isKinematic = false;
        heldProduct.useGravity = true;
        heldProduct.linearVelocity = Vector3.zero;
        heldProduct.angularVelocity = Vector3.zero;

        ClearHeldState();
    }

    private void ClearHeldState()
    {
        heldProduct = null;
        productColliders = null;
        originalColliderStates = null;
        isHolding = false;
    }

    private static bool HasTagInParents(Transform target, string tag)
    {
        Transform current = target;
        while (current != null)
        {
            if (current.CompareTag(tag))
                return true;

            current = current.parent;
        }

        return false;
    }
}