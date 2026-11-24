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

    private void Awake()
    {
        if (shelf == null)
        {
            Debug.LogError("PlacementChecker: ShelfPlacement reference is missing.", this);
            enabled = false;
            return;
        }

        shelfCollider = shelf.placementCollider;
        if (shelfCollider == null)
        {
            Debug.LogError("PlacementChecker: Placement Collider reference is missing on the shelf.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (shelf == null || shelfCollider == null)
            return;

        if (!shelf.IsHeld())
        {
            timer = 0f;
            return;
        }

        timer += Time.deltaTime;
        if (timer >= Mathf.Max(0.02f, checkInterval))
        {
            timer = 0f;
            CheckPlacement();
        }
    }

    private void CheckPlacement()
    {
        if (shelfCollider == null || !shelfCollider.enabled)
        {
            shelf.SetPlacementValidity(false);
            return;
        }

        Vector3 center;
        Vector3 halfExtents;
        Quaternion orientation;

        if (shelfCollider is BoxCollider box)
        {
            Vector3 scale = box.transform.lossyScale;
            Vector3 absoluteScale = new Vector3(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)
            );

            center = box.transform.TransformPoint(box.center);
            halfExtents = Vector3.Scale(box.size * 0.5f, absoluteScale) * 0.95f;
            orientation = box.transform.rotation;
        }
        else
        {
            // For non-box colliders, use the world-aligned bounds without applying rotation twice.
            Bounds bounds = shelfCollider.bounds;
            center = bounds.center;
            halfExtents = bounds.extents * 0.95f;
            orientation = Quaternion.identity;
        }

        // The shelf is moved through Transform, so synchronize before querying physics.
        Physics.SyncTransforms();

        Collider[] hits = Physics.OverlapBox(
            center,
            halfExtents,
            orientation,
            blockingLayers,
            QueryTriggerInteraction.Ignore
        );

        bool valid = true;
        foreach (Collider hit in hits)
        {
            if (hit == null || hit == shelfCollider)
                continue;

            if (hit.transform == shelf.transform || hit.transform.IsChildOf(shelf.transform))
                continue;

            valid = false;
            break;
        }

        shelf.SetPlacementValidity(valid);
    }

    private void OnDrawGizmosSelected()
    {
        if (shelf == null || shelf.placementCollider == null)
            return;

        Gizmos.color = Color.green;

        if (shelf.placementCollider is BoxCollider box)
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previousMatrix;
        }
        else
        {
            Bounds bounds = shelf.placementCollider.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}