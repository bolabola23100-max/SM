using UnityEngine;

public class PlacementChecker : MonoBehaviour
{
    [Header("References")]
    public ShelfPlacement shelf;

    [Header("Detection")]
    [Tooltip("Layers occupied by walls, furniture and other obstacles. The tagged floor is always allowed.")]
    public LayerMask blockingLayers = ~0;

    [Header("Required Surface Tags")]
    public string floorTag = "ShelfFloor";
    public string wallTag = "ShelfWall";

    [Header("Settings")]
    [Min(0.02f)] public float checkInterval = 0.05f;
    [Range(0.5f, 0.98f)] public float floorSampleInset = 0.85f;
    public float floorRayStartOffset = 0.25f;
    public float floorRayExtraDistance = 3f;
    [Range(0.8f, 1f)] public float overlapInset = 0.96f;

    private float timer;
    private Collider shelfCollider;

    private void Awake()
    {
        if (shelf == null)
            shelf = GetComponent<ShelfPlacement>();

        if (shelf == null)
        {
            Debug.LogError("PlacementChecker: ShelfPlacement reference is missing.", this);
            enabled = false;
            return;
        }

        shelfCollider = shelf.placementCollider;
        if (shelfCollider == null)
        {
            Debug.LogError("PlacementChecker: Assign the shelf's BoxCollider as Placement Collider.", this);
            enabled = false;
            return;
        }
    }

    private void Update()
    {
        if (shelf == null || shelfCollider == null || !shelf.IsHeld())
        {
            timer = 0f;
            return;
        }

        timer += Time.deltaTime;
        if (timer >= checkInterval)
        {
            timer = 0f;
            CheckPlacement();
        }
    }

    private void CheckPlacement()
    {
        Physics.SyncTransforms();

        Bounds bounds = shelfCollider.bounds;
        Vector3 center;
        Vector3 halfExtents;
        Quaternion orientation;

        if (shelfCollider is BoxCollider box)
        {
            Vector3 scale = box.transform.lossyScale;
            Vector3 absScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            center = box.transform.TransformPoint(box.center);
            halfExtents = Vector3.Scale(box.size * 0.5f, absScale) * overlapInset;
            orientation = box.transform.rotation;
        }
        else
        {
            center = bounds.center;
            halfExtents = bounds.extents * overlapInset;
            orientation = Quaternion.identity;
        }

        if (!HasFloorUnderShelf(center, halfExtents, orientation, bounds))
        {
            shelf.SetPlacementValidity(false);
            return;
        }

        Collider[] hits = Physics.OverlapBox(
            center, halfExtents, orientation, blockingLayers, QueryTriggerInteraction.Ignore
        );

        foreach (Collider hit in hits)
        {
            if (hit == null || IsPartOfShelf(hit.transform))
                continue;

            // A collider tagged as the floor (even if the tag is on its parent) is allowed.
            if (HasTagInParents(hit.transform, floorTag))
                continue;

            // Any wall is forbidden, regardless of its layer.
            if (HasTagInParents(hit.transform, wallTag))
            {
                shelf.SetPlacementValidity(false);
                return;
            }

            // Any other collider included in Blocking Layers blocks placement.
            shelf.SetPlacementValidity(false);
            return;
        }

        shelf.SetPlacementValidity(true);
    }

    private bool HasFloorUnderShelf(Vector3 center, Vector3 halfExtents, Quaternion orientation, Bounds bounds)
    {
        Vector3[] offsets =
        {
            new Vector3(-halfExtents.x * floorSampleInset, 0f, -halfExtents.z * floorSampleInset),
            new Vector3(-halfExtents.x * floorSampleInset, 0f,  halfExtents.z * floorSampleInset),
            new Vector3( halfExtents.x * floorSampleInset, 0f, -halfExtents.z * floorSampleInset),
            new Vector3( halfExtents.x * floorSampleInset, 0f,  halfExtents.z * floorSampleInset),
            Vector3.zero
        };

        float rayDistance = bounds.size.y + floorRayStartOffset + floorRayExtraDistance;

        foreach (Vector3 offset in offsets)
        {
            Vector3 point = center + orientation * offset;
            Vector3 origin = new Vector3(point.x, bounds.max.y + floorRayStartOffset, point.z);

            RaycastHit[] hits = Physics.RaycastAll(
                origin, Vector3.down, rayDistance, ~0, QueryTriggerInteraction.Ignore
            );

            bool foundFloor = false;
            float nearestDistance = float.MaxValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || IsPartOfShelf(hit.collider.transform))
                    continue;

                if (hit.distance < nearestDistance && HasTagInParents(hit.collider.transform, floorTag))
                {
                    nearestDistance = hit.distance;
                    foundFloor = true;
                }
            }

            if (!foundFloor) return false;
        }

        return true;
    }

    private bool IsPartOfShelf(Transform target)
    {
        return target == shelf.transform || target.IsChildOf(shelf.transform);
    }

    private static bool HasTagInParents(Transform target, string tag)
    {
        Transform current = target;
        while (current != null)
        {
            if (current.CompareTag(tag)) return true;
            current = current.parent;
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (shelf == null || shelf.placementCollider == null) return;

        Gizmos.color = Color.cyan;
        if (shelf.placementCollider is BoxCollider box)
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = old;
        }
        else
        {
            Bounds b = shelf.placementCollider.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}