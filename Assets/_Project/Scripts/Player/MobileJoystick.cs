using UnityEngine;
using UnityEngine.EventSystems;

public class MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform background;
    public RectTransform handle;

    public float HandleRange = 70f;

    public Vector2 InputDirection { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 position;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background,
            eventData.position,
            eventData.pressEventCamera,
            out position
        );

        position = Vector2.ClampMagnitude(position, HandleRange);

        handle.anchoredPosition = position;
        InputDirection = position / HandleRange;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        handle.anchoredPosition = Vector2.zero;
        InputDirection = Vector2.zero;
    }
}
