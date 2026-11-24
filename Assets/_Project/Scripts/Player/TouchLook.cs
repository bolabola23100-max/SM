
using UnityEngine;
using UnityEngine.EventSystems;

public class TouchLook : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    private Vector2 lookDelta;

    public void OnPointerDown(PointerEventData eventData)
    {
        lookDelta += eventData.delta;
    }

    public void OnDrag(PointerEventData eventData)
    {
        lookDelta += eventData.delta;
    }

    public Vector2 ConsumeDelta()
    {
        Vector2 delta = lookDelta;
        lookDelta = Vector2.zero;
        return delta;
    }
}
