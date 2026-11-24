using UnityEngine;

public class FurnitureController : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;

    [Header("Settings")]
    public float interactionDistance = 4f;

    private ShelfPlacement heldShelf;

    public bool IsHoldingShelf => heldShelf != null;

    public void HoldButton()
    {
        if (playerCamera == null)
            return;

        if (heldShelf == null)
        {
            TryPickupShelf();
            return;
        }

        if (heldShelf.CanPlace())
        {
            heldShelf.ConfirmPlacement();
            heldShelf = null;
        }
    }

    private void TryPickupShelf()
    {
        if (playerCamera == null)
            return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                interactionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return;

        ShelfPlacement shelf = hit.collider.GetComponentInParent<ShelfPlacement>();
        if (shelf == null || shelf.IsHeld())
            return;

        // The shelf can come from its own prefab/scene, so inject the active player camera when picked up.
        shelf.playerCamera = playerCamera;
        shelf.PickUp();
        heldShelf = shelf;
    }

    public void RotateButton()
    {
        if (heldShelf != null)
            heldShelf.RotateShelf();
    }

    public void CancelButton()
    {
        if (heldShelf == null)
            return;

        heldShelf.CancelPlacement();
        heldShelf = null;
    }
}