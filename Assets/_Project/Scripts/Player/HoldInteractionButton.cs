using UnityEngine;
using UnityEngine.EventSystems;

public class HoldInteractionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("References")]
    public Camera playerCamera;
    public ProductInteraction productInteraction;
    public FurnitureController furnitureController;

    [Header("Settings")]
    public float longPressDuration = 1f;
    public float interactionDistance = 4f;

    private bool isPressing;
    private float pressStartTime;

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressing = true;
        pressStartTime = Time.unscaledTime;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isPressing)
            return;

        isPressing = false;
        float pressDuration = Time.unscaledTime - pressStartTime;
        HandleInteraction(pressDuration);
    }

    private void HandleInteraction(float duration)
    {
        if (playerCamera == null || productInteraction == null || furnitureController == null)
            return;

        // A tap while carrying furniture confirms placement when the position is valid.
        if (furnitureController.IsHoldingShelf)
        {
            furnitureController.HoldButton();
            return;
        }

        // A tap while carrying a box drops it.
        if (productInteraction.IsHoldingProduct)
        {
            productInteraction.Interact();
            return;
        }

        if (!Physics.Raycast(
                playerCamera.transform.position,
                playerCamera.transform.forward,
                out RaycastHit hit,
                interactionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return;

        // Detect shelves by their component, not by a mismatched tag.
        ShelfPlacement shelf = hit.collider.GetComponentInParent<ShelfPlacement>();
        if (shelf != null)
        {
            if (duration >= longPressDuration)
                furnitureController.HoldButton();

            return;
        }

        // Only boxes are pickup targets. Individual products are deliberately excluded.
        if (HasTagInParents(hit.collider.transform, "Box"))
            productInteraction.Interact();
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