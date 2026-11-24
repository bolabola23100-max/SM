using UnityEngine;
using UnityEngine.InputSystem;

public class ProductInteraction : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform holdPoint;

    [Header("Settings")]
    public float interactionDistance = 3.5f;
    [Tooltip("Small gap above the shelf surface.")]
    public float placementSurfaceOffset = 0.015f;

    [Tooltip("Position of the held carton relative to the camera: X right, Y up, Z forward. Negative Y keeps it below the aiming point.")]
    public Vector3 heldBoxCameraOffset = new Vector3(0f, -0.48f, 1.35f);

    private Rigidbody heldProduct;
    private Collider[] productColliders;
    private bool[] originalColliderStates;
    private bool isHolding;

    public bool IsHoldingProduct => isHolding && heldProduct != null;

    private void Update()
    {
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

        if (playerCamera != null)
        {
            Transform cam = playerCamera.transform;
            Vector3 heldPosition = cam.position
                + cam.right * heldBoxCameraOffset.x
                + cam.up * heldBoxCameraOffset.y
                + cam.forward * heldBoxCameraOffset.z;
            heldProduct.transform.SetPositionAndRotation(heldPosition, cam.rotation);
        }
        else if (holdPoint != null)
        {
            heldProduct.transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
        }
    }

    public void Interact()
    {
        if (IsHoldingProduct)
        {
            ProductBox box = heldProduct.GetComponent<ProductBox>();
            if (box == null) return;

            // First tap opens the carton. Each later tap stocks one product on the aimed shelf.
            if (!box.IsOpen)
            {
                box.Open();
                return;
            }

            TryPlaceOneProductFromHeldBox(box);
            return;
        }

        TryPickupBox();
    }

    private bool TryPlaceOneProductFromHeldBox(ProductBox box)
    {
        if (box == null || box.IsEmpty || playerCamera == null)
            return false;

        if (!Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                out RaycastHit hit, interactionDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return false;

        ShelfPlacement shelf = hit.collider.GetComponentInParent<ShelfPlacement>();
        if (shelf == null || shelf.IsHeld())
            return false;

        ShelfZone zone = FindBestShelfZone(shelf, hit.point);
        if (zone == null || zone.IsFull)
            return false;

        ProductData data = box.Data;
        if (data == null || data.productPrefab == null)
            return false;

        GameObject product = box.TakeOne();
        if (product == null)
            return false;

        product.tag = "Product";

        // Freeze every Rigidbody immediately. Some product prefabs contain
        // child rigidbodies; leaving one dynamic can launch the item when it
        // overlaps the shelf or another collider during placement.
        Rigidbody[] productBodies = product.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in productBodies)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        product.transform.rotation = Quaternion.Euler(data.rotation);
        Physics.SyncTransforms();

        Bounds productBounds = GetWorldBounds(product);
        Vector3 visualCenterOffset = productBounds.center - product.transform.position;
        Bounds zoneBounds = zone.GetComponent<BoxCollider>().bounds;

        int index = zone.ProductCount;
        int columns = Mathf.Max(1, data.columns);
        int rows = Mathf.Max(1, data.rows);
        int col = index % columns;
        int row = (index / columns) % rows;
        int layer = index / (columns * rows);

        float cellWidth = zoneBounds.size.x / columns;
        float cellDepth = zoneBounds.size.z / rows;
        float localX = zoneBounds.min.x + cellWidth * (col + 0.5f);
        float localZ = zoneBounds.min.z + cellDepth * (row + 0.5f);
        float targetY = zoneBounds.min.y + productBounds.extents.y + placementSurfaceOffset
            + layer * Mathf.Max(0.01f, data.productHeight);

        Vector3 targetVisualCenter = new Vector3(localX, targetY, localZ) + data.offset;
        product.transform.position = targetVisualCenter - visualCenterOffset;
        product.transform.SetParent(zone.transform, true);

        // Apply the final transform to physics before registering the item.
        Physics.SyncTransforms();

        if (!zone.RegisterProduct(product.transform))
        {
            Destroy(product);
            return false;
        }

        Debug.Log($"Stocked {data.productName} on {zone.name}. Remaining in carton: {box.RemainingCount}");
        return true;
    }

    private static Bounds GetWorldBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        Bounds bounds = new Bounds(target.transform.position, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer item in renderers)
        {
            if (item == null) continue;
            if (!hasBounds) { bounds = item.bounds; hasBounds = true; }
            else bounds.Encapsulate(item.bounds);
        }

        if (!hasBounds)
        {
            foreach (Collider item in colliders)
            {
                if (item == null || item.isTrigger) continue;
                if (!hasBounds) { bounds = item.bounds; hasBounds = true; }
                else bounds.Encapsulate(item.bounds);
            }
        }

        return hasBounds ? bounds : new Bounds(target.transform.position, Vector3.one * 0.1f);
    }

    /// <summary>Places the held box in the ShelfZone closest to the aimed shelf level.</summary>
    public bool TryPlaceHeldProductOnShelf()
    {
        if (!IsHoldingProduct || playerCamera == null)
            return false;

        if (!Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                out RaycastHit hit, interactionDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return false;

        ShelfPlacement shelf = hit.collider.GetComponentInParent<ShelfPlacement>();
        if (shelf == null || shelf.IsHeld())
            return false;

        ShelfZone zone = FindBestShelfZone(shelf, hit.point);
        if (zone == null)
        {
            Debug.LogWarning("Shelf placement failed: no ShelfZone was found under this shelf.");
            return false;
        }

        if (zone.IsFull)
        {
            Debug.LogWarning("Shelf placement failed: the targeted ShelfZone is full.");
            return false;
        }

        PlaceProductOnShelf(hit.point, zone);
        return true;
    }

    private ShelfZone FindBestShelfZone(ShelfPlacement shelf, Vector3 aimPoint)
    {
        ShelfZone[] zones = shelf.GetComponentsInChildren<ShelfZone>(true);
        ShelfZone bestZone = null;
        float bestDistance = float.PositiveInfinity;

        foreach (ShelfZone candidate in zones)
        {
            if (candidate == null)
                continue;

            BoxCollider zoneCollider = candidate.GetComponent<BoxCollider>();
            if (zoneCollider == null || !zoneCollider.enabled)
                continue;

            Bounds bounds = zoneCollider.bounds;
            Vector3 closest = bounds.ClosestPoint(aimPoint);
            float distance = (closest - aimPoint).sqrMagnitude;

            // Prefer the zone whose volume is closest to the point the player is aiming at.
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = candidate;
            }
        }

        return bestZone;
    }

    private void TryPickupBox()
    {
        if (playerCamera == null || holdPoint == null)
            return;

        if (!Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                out RaycastHit hit, interactionDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return;

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
        heldProduct.transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
    }

    private void PlaceProductOnShelf(Vector3 aimPoint, ShelfZone zone)
    {
        if (heldProduct == null || zone == null)
            return;

        // Re-enable the box collider before measuring it and before the trigger detects it.
        RestoreProductColliders();

        heldProduct.isKinematic = true;
        heldProduct.useGravity = false;
        heldProduct.linearVelocity = Vector3.zero;
        heldProduct.angularVelocity = Vector3.zero;

        Bounds productBounds = GetProductBounds();
        Bounds zoneBounds = zone.GetComponent<BoxCollider>().bounds;

        float halfX = Mathf.Min(productBounds.extents.x, zoneBounds.extents.x * 0.45f);
        float halfZ = Mathf.Min(productBounds.extents.z, zoneBounds.extents.z * 0.45f);

        float x = Mathf.Clamp(aimPoint.x, zoneBounds.min.x + halfX, zoneBounds.max.x - halfX);
        float z = Mathf.Clamp(aimPoint.z, zoneBounds.min.z + halfZ, zoneBounds.max.z - halfZ);

        // The bottom of ProductZone should be aligned with the shelf platform.
        // This works even if the camera ray hits the shelf's vertical frame instead of its top.
        float y = zoneBounds.min.y + productBounds.extents.y + placementSurfaceOffset;
        Vector3 targetPosition = new Vector3(x, y, z);

        heldProduct.transform.SetParent(zone.transform, true);
        heldProduct.position = targetPosition;
        heldProduct.rotation = Quaternion.Euler(0f, heldProduct.rotation.eulerAngles.y, 0f);

        heldProduct.isKinematic = true;
        heldProduct.useGravity = false;
        heldProduct.linearVelocity = Vector3.zero;
        heldProduct.angularVelocity = Vector3.zero;

        Debug.Log("Product placed on shelf zone: " + zone.name);
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
                if (productCollider == null || !productCollider.enabled || productCollider.isTrigger)
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

    [Header("Throw Settings")]
    [Min(0f)] public float throwForwardForce = 2.5f;
    [Min(0f)] public float throwUpwardForce = 1.0f;
    [Min(0.1f)] public float throwDistance = 1.2f;

    /// <summary>Called by the separate Drop/Throw UI button.</summary>
    public void ThrowHeldBox()
    {
        if (!IsHoldingProduct)
            return;

        Rigidbody box = heldProduct;
        Vector3 forward = playerCamera != null
            ? playerCamera.transform.forward
            : transform.forward;
        Vector3 origin = playerCamera != null
            ? playerCamera.transform.position
            : transform.position;

        box.transform.SetParent(null, true);
        box.position = origin + forward * throwDistance;

        RestoreProductColliders();

        box.isKinematic = false;
        box.useGravity = true;
        box.linearVelocity = forward * throwForwardForce + Vector3.up * throwUpwardForce;
        box.angularVelocity = Vector3.zero;

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
