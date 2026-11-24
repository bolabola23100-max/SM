using UnityEngine;

public class PlacementChecker : MonoBehaviour
{
    [Header("References")]
    public ShelfPlacement shelf;

    [Header("Detection")]
    public LayerMask blockingLayers;

    [Header("Settings")]
    public float checkInterval = 0.05f;

    private float timer;

    private Collider shelfCollider;

    void Start()
    {
        shelfCollider = shelf.placementCollider;
    }

    void Update()
    {
        if (shelf == null || !shelf.IsHeld())
            return;

        timer += Time.deltaTime;

        if (timer >= checkInterval)
        {
            timer = 0f;
            CheckPlacement();
        }
    }

    void CheckPlacement()
    {
        Bounds bounds = shelfCollider.bounds;

        Collider[] hits = Physics.OverlapBox(
            bounds.center,
            bounds.extents * 0.95f,
            shelf.transform.rotation,
            blockingLayers,
            QueryTriggerInteraction.Ignore
        );

        bool valid = true;

        foreach (Collider hit in hits)
        {
            if (hit == shelfCollider)
                continue;

            if (hit.transform.IsChildOf(shelf.transform))
                continue;

            valid = false;
            break;
        }

        shelf.SetPlacementValidity(valid);
    }

    void OnDrawGizmosSelected()
    {
        if (shelf == null || shelf.placementCollider == null)
            return;

        Gizmos.color = Color.green;

        Bounds bounds = shelf.placementCollider.bounds;

        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
}