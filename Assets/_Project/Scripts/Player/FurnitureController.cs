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
        if (heldShelf == null)
        {
            TryPickupShelf();
        }
        else
        {
            if (heldShelf.CanPlace())
            {
                heldShelf.ConfirmPlacement();
                heldShelf = null;
            }
        }
    }

    void TryPickupShelf()
    {
        Ray ray = new Ray(
            playerCamera.transform.position,
            playerCamera.transform.forward
        );

        if (Physics.Raycast(ray, out RaycastHit hit,
            interactionDistance))
        {
            ShelfPlacement shelf =
                hit.collider.GetComponentInParent<ShelfPlacement>();

            if (shelf != null)
            {
                shelf.PickUp();
                heldShelf = shelf;
            }
        }
    }

    public void RotateButton()
    {
        if (heldShelf != null)
            heldShelf.RotateShelf();
    }

    public void CancelButton()
    {
        if (heldShelf != null)
        {
            heldShelf.CancelPlacement();
            heldShelf = null;
        }
    }
}