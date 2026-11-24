using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HoldInteractionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("References")]
    public Camera playerCamera;
    public ProductInteraction productInteraction;
    public FurnitureController furnitureController;
    public Image holdProgressImage;

    [Header("Settings")]
    public float longPressDuration = 1f;
    public float interactionDistance = 4f;

    private bool isPressing;
    private bool holdTriggered;
    private bool startedWhileHoldingShelf;
    private float pressStartTime;
    private ShelfPlacement targetShelf;

    private void Awake() => SetProgress(0f);

    private void Update()
    {
        if (!isPressing || startedWhileHoldingShelf || targetShelf == null || holdTriggered
            || (productInteraction != null && productInteraction.IsHoldingProduct))
            return;

        if (GetShelfUnderAim() != targetShelf)
        {
            targetShelf = null;
            SetProgress(0f);
            return;
        }

        float progress = longPressDuration <= 0f
            ? 1f
            : Mathf.Clamp01((Time.unscaledTime - pressStartTime) / longPressDuration);

        SetProgress(progress);

        // Pick up automatically when the hold timer finishes; no finger release needed.
        if (progress >= 1f)
        {
            furnitureController.HoldButton();
            holdTriggered = true;
            targetShelf = null;
            SetProgress(0f);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (playerCamera == null || productInteraction == null || furnitureController == null)
            return;

        isPressing = true;
        holdTriggered = false;
        pressStartTime = Time.unscaledTime;
        startedWhileHoldingShelf = furnitureController.IsHoldingShelf;
        targetShelf = null;
        SetProgress(0f);

        if (startedWhileHoldingShelf || productInteraction.IsHoldingProduct)
            return;

        targetShelf = GetShelfUnderAim();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isPressing)
            return;

        isPressing = false;
        SetProgress(0f);

        if (holdTriggered)
        {
            targetShelf = null;
            return;
        }

        targetShelf = null;

        // When carrying a shelf, a tap confirms its placement.
        if (startedWhileHoldingShelf)
        {
            furnitureController.HoldButton();
            return;
        }

        // While carrying a carton, a tap only opens it. It never drops or places it.
        if (productInteraction.IsHoldingProduct)
        {
            productInteraction.Interact();
            return;
        }

        // A box is picked up with a normal tap. Shelf pickup still requires a long press.
        if (IsBoxUnderAim())
            productInteraction.Interact();
    }

    private ShelfPlacement GetShelfUnderAim()
    {
        if (playerCamera == null)
            return null;

        if (!Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                out RaycastHit hit, interactionDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return null;

        ShelfPlacement shelf = hit.collider.GetComponentInParent<ShelfPlacement>();
        return shelf == null || shelf.IsHeld() ? null : shelf;
    }

    private bool IsBoxUnderAim()
    {
        if (playerCamera == null)
            return false;

        if (!Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                out RaycastHit hit, interactionDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return false;

        Transform current = hit.collider.transform;
        while (current != null)
        {
            if (current.CompareTag("Box"))
                return true;
            current = current.parent;
        }

        return false;
    }

    private void SetProgress(float value)
    {
        if (holdProgressImage == null)
            return;

        holdProgressImage.fillAmount = value;
        holdProgressImage.enabled = value > 0f;
    }
}
