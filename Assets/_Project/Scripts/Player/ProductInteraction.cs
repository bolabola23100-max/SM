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
    public bool IsHoldingProduct => isHolding && heldProduct != null;
    private Collider[] productColliders;

    private bool isHolding;
    void Update()
    {
        // New Input System
        if (Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame)
        {
            Interact();
        }
    }

    void LateUpdate()
    {
        if (isHolding && heldProduct != null)
        {
            heldProduct.transform.SetPositionAndRotation(
                holdPoint.position,
                holdPoint.rotation
            );
        }
    }

    // Mobile Button
    public void Interact()
    {
        if (isHolding)
            DropProduct();
        else
            TryPickupProduct();
    }

    void TryPickupProduct()
    {
        Ray ray = new Ray(
            playerCamera.transform.position,
            playerCamera.transform.forward
        );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            interactionDistance))
        {
            Rigidbody product =
                hit.collider.GetComponentInParent<Rigidbody>();

            if (product != null)
            {
                PickupProduct(product);
            }
        }
    }

    void PickupProduct(Rigidbody product)
    {
        heldProduct = product;

        isHolding = true;

        productColliders =
            product.GetComponentsInChildren<Collider>();

        heldProduct.transform.SetParent(null, true);

        heldProduct.isKinematic = true;

        heldProduct.useGravity = false;

        heldProduct.linearVelocity = Vector3.zero;

        heldProduct.angularVelocity = Vector3.zero;

        // Disable collisions while holding
        foreach (Collider col in productColliders)
        {
            col.enabled = false;
        }

        heldProduct.transform.SetPositionAndRotation(
            holdPoint.position,
            holdPoint.rotation
        );
    }

    void DropProduct()
    {
        if (heldProduct == null)
            return;

        Vector3 dropPosition =
            playerCamera.transform.position +
            playerCamera.transform.forward * 1.5f;

        heldProduct.transform.SetParent(null);

        heldProduct.transform.position = dropPosition;

        // Restore collisions
        foreach (Collider col in productColliders)
        {
            if (col != null)
                col.enabled = true;
        }

        heldProduct.isKinematic = false;

        heldProduct.useGravity = true;

        heldProduct.linearVelocity = Vector3.zero;

        heldProduct.angularVelocity = Vector3.zero;

        heldProduct = null;

        productColliders = null;

        isHolding = false;
    }
}