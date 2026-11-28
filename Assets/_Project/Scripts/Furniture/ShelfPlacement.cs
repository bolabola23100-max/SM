using System.Collections.Generic;
using UnityEngine;

public class ShelfPlacement : MonoBehaviour
{
    [Header("Settings")]
    public float moveDistance = 2.5f;
    public float moveSmoothness = 15f;
    public float rotationStep = 90f;

    [Header("References")]
    public Camera playerCamera;
    public Collider placementCollider;

    [Header("Placement Preview")]
    [SerializeField] private Color validColor = new Color(0f, 1f, 0f, 1f);
    [SerializeField] private Color invalidColor = new Color(1f, 0f, 0f, 1f);

    private bool isHeld;
    private bool canPlace;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float currentRotation;
    private Rigidbody rb;

    private sealed class ProductPose { public Transform product; public Vector3 localPosition; public Quaternion localRotation; public Rigidbody body; public bool wasKinematic; public bool usedGravity; }
    private readonly List<ProductPose> heldProducts = new List<ProductPose>();

    [SerializeField, Min(0.001f)] private float outlineWidth = 0.025f;
    private Renderer[] shelfRenderers;
    private readonly List<LineRenderer> outlineLines = new List<LineRenderer>(12);
    private Material outlineMaterial;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Keep the original materials untouched. A separate outline indicates placement validity.
        shelfRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Update()
    {
        if (isHeld) { MoveShelf(); UpdateHeldProducts(); }
    }

    private void MoveShelf()
    {
        if (playerCamera == null) return;

        Vector3 flatForward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;

        Vector3 targetPosition = playerCamera.transform.position + flatForward * moveDistance;
        targetPosition.y = originalPosition.y;

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSmoothness);
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            Quaternion.Euler(0f, currentRotation, 0f),
            Time.deltaTime * moveSmoothness
        );
    }

    public void PickUp()
    {
        if (isHeld) return;

        originalPosition = transform.position;
        originalRotation = transform.rotation;
        currentRotation = transform.eulerAngles.y;
        isHeld = true;
        canPlace = false;
        CaptureShelfProducts();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        CreateOutlineIfNeeded();
        ApplyPlacementPreview(false);
    }

    public void RotateShelf()
    {
        if (isHeld) currentRotation += rotationStep;
    }

    public void ConfirmPlacement()
    {
        if (!isHeld || !canPlace) return;

        UpdateHeldProducts();
        RestoreProductPhysics();
        heldProducts.Clear();
        isHeld = false;
        SetOutlineVisible(false);

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    public void CancelPlacement()
    {
        if (!isHeld) return;

        transform.SetPositionAndRotation(originalPosition, originalRotation);
        UpdateHeldProducts();
        RestoreProductPhysics();
        heldProducts.Clear();
        isHeld = false;
        canPlace = false;
        SetOutlineVisible(false);
    }

    public void SetPlacementValidity(bool valid)
    {
        if (canPlace == valid && isHeld) return;
        canPlace = valid;
        if (isHeld) ApplyPlacementPreview(valid);
    }

    private void CreateOutlineIfNeeded()
    {
        if (outlineLines.Count > 0) return;
        Bounds bounds = GetCombinedLocalBounds();
        Vector3 min = bounds.min, max = bounds.max;
        Vector3[] p =
        {
            new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z),
            new Vector3(max.x,min.y,max.z), new Vector3(min.x,min.y,max.z),
            new Vector3(min.x,max.y,min.z), new Vector3(max.x,max.y,min.z),
            new Vector3(max.x,max.y,max.z), new Vector3(min.x,max.y,max.z)
        };
        int[,] e = { {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7} };
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        if (shader == null) return;
        outlineMaterial = new Material(shader) { name = "Furniture Placement Outline" };
        outlineMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
        for (int i=0;i<12;i++)
        {
            GameObject edge = new GameObject("Placement Outline Edge");
            edge.transform.SetParent(transform,false);
            LineRenderer line = edge.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0,p[e[i,0]]);
            line.SetPosition(1,p[e[i,1]]);
            line.startWidth = outlineWidth;
            line.endWidth = outlineWidth;
            line.sharedMaterial = outlineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            outlineLines.Add(line);
        }
    }

    private Bounds GetCombinedLocalBounds()
    {
        Bounds result = new Bounds(Vector3.zero,Vector3.one);
        bool found = false;
        foreach (Renderer renderer in shelfRenderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            Bounds b = renderer.bounds;
            for (int i=0;i<8;i++)
            {
                Vector3 world = new Vector3((i&1)==0?b.min.x:b.max.x,(i&2)==0?b.min.y:b.max.y,(i&4)==0?b.min.z:b.max.z);
                Vector3 local = transform.InverseTransformPoint(world);
                if (!found) { result = new Bounds(local,Vector3.zero); found = true; }
                else result.Encapsulate(local);
            }
        }
        if (!found) result = new Bounds(Vector3.zero,Vector3.one);
        result.Expand(outlineWidth*2f);
        return result;
    }

    private void ApplyPlacementPreview(bool valid)
    {
        if (outlineLines.Count == 0) CreateOutlineIfNeeded();
        Color color = valid ? validColor : invalidColor;
        color.a = 1f;
        if (outlineMaterial != null)
        {
            if (outlineMaterial.HasProperty("_BaseColor")) outlineMaterial.SetColor("_BaseColor",color);
            if (outlineMaterial.HasProperty("_Color")) outlineMaterial.SetColor("_Color",color);
        }
        foreach (LineRenderer line in outlineLines)
        {
            if (line == null) continue;
            line.startColor = color;
            line.endColor = color;
            line.enabled = true;
        }
    }

    private void SetOutlineVisible(bool visible)
    {
        foreach (LineRenderer line in outlineLines)
            if (line != null) line.enabled = visible;
    }

    private void OnDestroy()
    {
        if (outlineMaterial != null) Destroy(outlineMaterial);
    }

    private void CaptureShelfProducts()
    {
        heldProducts.Clear();
        HashSet<Transform> unique = new HashSet<Transform>();
        foreach (ShelfZone zone in GetComponentsInChildren<ShelfZone>(true))
        {
            if (zone == null) continue;
            foreach (Transform product in zone.Products)
            {
                if (product == null || !unique.Add(product)) continue;
                ProductPose pose = new ProductPose
                {
                    product = product,
                    localPosition = transform.InverseTransformPoint(product.position),
                    localRotation = Quaternion.Inverse(transform.rotation) * product.rotation,
                    body = product.GetComponent<Rigidbody>()
                };
                if (pose.body != null)
                {
                    pose.wasKinematic = pose.body.isKinematic;
                    pose.usedGravity = pose.body.useGravity;
                    pose.body.isKinematic = true;
                    pose.body.useGravity = false;
                }
                heldProducts.Add(pose);
            }
        }
    }

    private void UpdateHeldProducts()
    {
        foreach (ProductPose pose in heldProducts)
            if (pose != null && pose.product != null)
                pose.product.SetPositionAndRotation(transform.TransformPoint(pose.localPosition), transform.rotation * pose.localRotation);
    }

    private void RestoreProductPhysics()
    {
        foreach (ProductPose pose in heldProducts)
            if (pose != null && pose.product != null && pose.body != null)
            {
                pose.body.isKinematic = pose.wasKinematic;
                pose.body.useGravity = pose.usedGravity;
            }
    }

    public bool IsHeld() => isHeld;
    public bool CanPlace() => canPlace;
}