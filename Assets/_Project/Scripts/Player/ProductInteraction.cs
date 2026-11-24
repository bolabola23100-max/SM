using UnityEngine;
using UnityEngine.InputSystem;

public class ProductInteraction : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform holdPoint;

    [Header("Settings")]
    public float interactionDistance = 3.5f;
    [Tooltip("Small offset above the shelf surface to prevent clipping.")]
    public float placementSurfaceOffset = 0.01f;

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

    /// <summary>
    /// Places the held box only when the player is aiming at a shelf.
    /// Returns false when no valid shelf surface is targeted.
    /// </summary>
    public bool TryPlaceHeldProductOnShelf()
    {
        if (!IsHoldingProduct || playerCamera == null)
            return false;

        if (!Physics.Raycast(
                playerCamera.transform.position,
                playerCamera.transform.forward,
                out RaycastHit hit,
                interactionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return false;

        ShelfPlacement shelf = hit.collider.GetComponentInParent<ShelfPlacement>();
        if (shelf == null || shelf.IsHeld())
            return false;

        // Only place on an upward-facing shelf surface, not its side or back.
        if (Vector3.Dot(hit.normal, Vector3.up) < 0.65f)
            return false;

        ShelfZone zone = shelf.GetComponentInChildren<ShelfZone>();
        if (zone == null || zone.IsFull)
            return false;

        PlaceProductOnShelf(hit, zone);
        return true;
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

    private void PlaceProductOnShelf(RaycastHit hit, ShelfZone zone)
    {
        if (heldProduct == null || zone == null)
            return;

        // Restore the product colliders so ShelfZone's trigger can detect it.
        RestoreProductColliders();

        // Keep the Rigidbody frozen on the shelf: no gravity and no physics falling.
        heldProduct.isKinematic = true;
        heldProduct.useGravity = false;
        heldProduct.linearVelocity = Vector3.zero;
        heldProduct.angularVelocity = Vector3.zero;

        Bounds productBounds = GetProductBounds();
        Bounds zoneBounds = zone.GetComponent<BoxCollider>().bounds;

        float halfX = Mathf.Min(productBounds.extents.x, zoneBounds.extents.x * 0.45f);
        float halfZ = Mathf.Min(productBounds.extents.z, zoneBounds.extents.z * 0.45f);

        float x = Mathf.Clamp(hit.point.x,
            zoneBounds.min.x + halfX,
            zoneBounds.max.x - halfX);

        float z = Mathf.Clamp(hit.point.z,
            zoneBounds.min.z + halfZ,
            zoneBounds.max.z - halfZ);

        // Put the bottom of the product just above the shelf surface.
        float y = hit.point.y + productBounds.extents.y + placementSurfaceOffset;
        Vector3 targetPosition = new Vector3(x, y, z);

        heldProduct.transform.SetParent(zone.transform, true);
        heldProduct.position = targetPosition;
        heldProduct.rotation = Quaternion.Euler(0f, heldProduct.rotation.eulerAngles.y, 0f);

        heldProduct.isKinematic = true;
        heldProduct.useGravity = false;
        heldProduct.linearVelocity = Vector3.zero;
        heldProduct.angularVelocity = Vector3.zero;

        ClearHeldState();
    }

    private Bounds GetProductBounds()
    {
        Bounds bounds = new Bounds(heldProduct.position, Vector3.zero);
        bool hasBounds = false;

        if (productColliders != null)
        {
            foreach (Collider productCollider in productColliders)
            {
                if (productCollider == null || !productCollider.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = productCollider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(productCollider.bounds);
                }
            }
        }

        if (!hasBounds)
            bounds = new Bounds(heldProduct.position, Vector3.one * 0.2f);

        return bounds;
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

        RestoreProductColliders();

        heldProduct.isKinematic = false;
        heldProduct.useGravity = true;
        heldProduct.linearVelocity = Vector3.zero;
        heldProduct.angularVelocity = Vector3.zero;

        ClearHeldState();
    }

    private void RestoreProductColliders()
    {
        if (productColliders == null)
            return;

        for (int i = 0; i < productColliders.Length; i++)
        {
            if (productColliders[i] != null)
                productColliders[i].enabled = originalColliderStates[i];
        }
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
