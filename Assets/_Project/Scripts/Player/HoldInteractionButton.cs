using UnityEngine;
using UnityEngine.EventSystems;

public class HoldInteractionButton :
    MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler
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

        float pressDuration =
            Time.unscaledTime - pressStartTime;

        HandleInteraction(pressDuration);
    }

    void HandleInteraction(float duration)
    {
        // اللاعب ماسك رف بالفعل
        if (furnitureController.IsHoldingShelf)
        {
            furnitureController.HoldButton();
            return;
        }

        // اللاعب ماسك منتج بالفعل
        if (productInteraction.IsHoldingProduct)
        {
            productInteraction.Interact();
            return;
        }

        // تحديد العنصر أمام اللاعب
        Ray ray = new Ray(
            playerCamera.transform.position,
            playerCamera.transform.forward
        );

        if (!Physics.Raycast(
            ray,
            out RaycastHit hit,
            interactionDistance))
            return;

        bool isShelf = HasTagInParents(hit.transform, "Shelf");

        bool isBox = HasTagInParents(hit.transform, "Box");

        bool isProduct = HasTagInParents(hit.transform, "Product");

        // الرف يحتاج ضغط مطول
        if (isShelf)
        {
            if (duration >= longPressDuration)
            {
                furnitureController.HoldButton();
            }

            return;
        }

        // الكرتونة والمنتج بضغطة واحدة
        if (isBox || isProduct)
        {
            productInteraction.Interact();
        }
    }

    bool HasTagInParents(Transform target, string tag)
    {
        Transform current = target;

        while (current != null)
        {
            if (current.tag == tag)
                return true;

            current = current.parent;
        }

        return false;
    }
}