using UnityEngine;

public class PlacementChecker : MonoBehaviour
{
    [Header("References")]
    public ShelfPlacement shelf;

    [Header("Detection")]
    [Tooltip("Layers that should prevent shelf placement, such as Furniture and Environment.")]
    public LayerMask blockingLayers;

    [Header("Required Surface Tags")]
    public string floorTag = "ShelfFloor";
    public string wallTag = "ShelfWall";

    [Header("Settings")]
    public float checkInterval = 0.05f;
    [Range(0.5f, 0.95f)] public float floorSampleInset = 0.8f;
    public float floorRayStartOffset = 0.5f;
    public float floorRayExtraDistance = 2f;

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
        if (!shelfCollider.enabled)
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
            Bounds bounds = shelfCollider.bounds;
            center = bounds.center;
            halfExtents = bounds.extents * 0.95f;
            orientation = Quaternion.identity;
        }

        Physics.SyncTransforms();

        // The shelf must have the supermarket floor underneath its footprint.
        if (!HasFloorUnderShelf(center, halfExtents, orientation))
        {
            shelf.SetPlacementValidity(false);
            return;
        }

        // Check all colliders. The floor is allowed; tagged walls and blocking layers are not.
        Collider[] hits = Physics.OverlapBox(
            center,
            halfExtents,
            orientation,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        bool valid = true;

        foreach (Collider hit in hits)
        {
            if (hit == null || hit == shelfCollider)
                continue;

            if (hit.transform == shelf.transform || hit.transform.IsChildOf(shelf.transform))
                continue;

            if (hit.CompareTag(floorTag))
                continue;

            bool isWall = hit.CompareTag(wallTag);
            bool isBlockingLayer = (blockingLayers.value & (1 << hit.gameObject.layer)) != 0;

            if (isWall || isBlockingLayer)
            {
                valid = false;
                break;
            }
        }

        shelf.SetPlacementValidity(valid);
    }

    private bool HasFloorUnderShelf(Vector3 center, Vector3 halfExtents, Quaternion orientation)
    {
        Vector3[] localOffsets =
        {
            new Vector3(-halfExtents.x * floorSampleInset, 0f, -halfExtents.z * floorSampleInset),
            new Vector3(-halfExtents.x * floorSampleInset, 0f,  halfExtents.z * floorSampleInset),
            new Vector3( halfExtents.x * floorSampleInset, 0f, -halfExtents.z * floorSampleInset),
            new Vector3( halfExtents.x * floorSampleInset, 0f,  halfExtents.z * floorSampleInset),
            Vector3.zero
        };

        Bounds bounds = shelfCollider.bounds;
        float rayDistance = bounds.size.y + floorRayExtraDistance;

        foreach (Vector3 localOffset in localOffsets)
        {
            Vector3 sample = center + orientation * localOffset;
            Vector3 rayOrigin = new Vector3(
                sample.x,
                bounds.max.y + floorRayStartOffset,
                sample.z
            );

            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                rayDistance,
                ~0,
                QueryTriggerInteraction.Ignore
            );

            bool foundFloor = false;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider != null && hit.collider.CompareTag(floorTag))
                {
                    foundFloor = true;
                    break;
                }
            }

            if (!foundFloor)
                return false;
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (shelf == null || shelf.placementCollider == null)
            return;

        Gizmos.color = Color.cyan;

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