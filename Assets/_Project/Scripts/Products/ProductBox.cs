using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural carton whose footprint and height are fitted to the product prefab
/// and the actual pack count (not the shelf's layer setting).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class ProductBox : MonoBehaviour
{
    [Header("Product in this carton")]
    [SerializeField] private ProductData productData;
    [SerializeField] private bool useProductPackSize = true;
    [Min(1)][SerializeField] private int customCount = 12;

    [Header("Carton layout")]
    [Min(1)][SerializeField] private int columns = 3;
    [Min(1)][SerializeField] private int rows = 2;
    [Min(0.001f)][SerializeField] private float productSpacing = 0.015f;
    [Min(0.005f)][SerializeField] private float productFootprint = 0.18f;
    [Min(0.005f)][SerializeField] private float productHeight = 0.22f;
    [Min(0.005f)][SerializeField] private float wallThickness = 0.018f;
    [Min(0f)][SerializeField] private float padding = 0.035f;

    [Header("Appearance")]
    [SerializeField] private Color cartonColor = new Color(0.62f, 0.40f, 0.22f);
    [SerializeField] private bool showProductModelsBeforeOpening = false;
    [SerializeField] private bool buildOnAwake = true;

    [Header("Runtime state")]
    [SerializeField] private bool isOpen;

    private readonly List<GameObject> productDisplays = new List<GameObject>();
    private Transform visualRoot;
    private Transform leftFlap;
    private Transform rightFlap;
    private Material cartonMaterial;
    private int remainingCount;
    private Vector3 cartonSize;
    private float fittedProductWidth;
    private float fittedProductDepth;
    private Vector3 fittedProductCenterOffset;
    private bool displaysBuilt;

    private struct ProductDimensions
    {
        public float width, depth, height;
        public Vector3 centerOffset;
    }
    private static readonly Dictionary<EntityId, ProductDimensions> dimensionsCache = new Dictionary<EntityId, ProductDimensions>();

    public ProductData Data => productData;
    public int RemainingCount => remainingCount;
    public bool IsOpen => isOpen;
    public bool IsEmpty => remainingCount <= 0;
    public string ProductName => productData != null ? productData.productName : string.Empty;

    private void Awake()
    {
        if (buildOnAwake) BuildCarton();
    }

    public void SetProduct(ProductData data, int amount = -1)
    {
        productData = data;
        if (amount > 0) { useProductPackSize = false; customCount = amount; }
        else useProductPackSize = true;

        remainingCount = data != null
            ? (useProductPackSize ? Mathf.Max(1, data.packSize) : Mathf.Max(1, customCount))
            : 0;
        if (Application.isPlaying) BuildCarton();
    }

    public void BuildCarton()
    {
        ClearGeneratedVisuals();
        if (productData == null || productData.productPrefab == null)
        {
            Debug.LogWarning($"ProductBox on '{name}' needs a ProductData with Product Prefab assigned.", this);
            remainingCount = 0;
            return;
        }

        remainingCount = useProductPackSize ? Mathf.Max(1, productData.packSize) : Mathf.Max(1, customCount);
        MeasureProductDimensions();

        // Choose a compact, near-square footprint for a normal-looking carton.
        // The product count and real prefab dimensions still determine the final size.
        productSpacing = Mathf.Clamp(productSpacing, 0.004f, 0.012f);
        padding = Mathf.Clamp(padding, 0.012f, 0.025f);
        int bestColumns = 1;
        int bestRows = remainingCount;
        float bestScore = float.MaxValue;

        for (int candidateColumns = 1; candidateColumns <= remainingCount; candidateColumns++)
        {
            int candidateRows = Mathf.CeilToInt((float)remainingCount / candidateColumns);
            float candidateWidth = candidateColumns * fittedProductWidth + (candidateColumns - 1) * productSpacing;
            float candidateDepth = candidateRows * fittedProductDepth + (candidateRows - 1) * productSpacing;
            float ratio = Mathf.Max(candidateWidth, candidateDepth) / Mathf.Max(0.001f, Mathf.Min(candidateWidth, candidateDepth));

            // Prefer a square/short-rectangle footprint without making the carton needlessly large.
            float area = candidateWidth * candidateDepth;
            float shapePenalty = 1f + Mathf.Abs(Mathf.Log(ratio / 1.18f)) * 1.35f;
            float score = area * shapePenalty;
            if (score < bestScore)
            {
                bestScore = score;
                bestColumns = candidateColumns;
                bestRows = candidateRows;
            }
        }

        columns = bestColumns;
        rows = bestRows;
        int perLayer = columns * rows;
        int layers = Mathf.Max(1, Mathf.CeilToInt((float)remainingCount / perLayer));

        float innerX = columns * fittedProductWidth + (columns - 1) * productSpacing;
        float innerZ = rows * fittedProductDepth + (rows - 1) * productSpacing;
        float innerY = layers * productHeight;

        cartonSize = new Vector3(innerX + 2f * (padding + wallThickness),
            innerY + 2f * padding + wallThickness,
            innerZ + 2f * (padding + wallThickness));

        visualRoot = new GameObject("CartonVisuals").transform;
        visualRoot.SetParent(transform, false);
        Shader cartonShader = Shader.Find("Universal Render Pipeline/Lit");
        if (cartonShader == null) cartonShader = Shader.Find("Standard");
        if (cartonShader == null)
        {
            Debug.LogError("ProductBox: no compatible carton shader was found.", this);
            return;
        }
        cartonMaterial = new Material(cartonShader);
        cartonMaterial.color = cartonColor;

        float halfX = cartonSize.x * 0.5f, halfY = cartonSize.y * 0.5f, halfZ = cartonSize.z * 0.5f;
        float t = wallThickness;
        AddPanel("Bottom", new Vector3(cartonSize.x, t, cartonSize.z), new Vector3(0f, -halfY + t * 0.5f, 0f));
        AddPanel("Front", new Vector3(cartonSize.x, cartonSize.y, t), new Vector3(0f, 0f, halfZ - t * 0.5f));
        AddPanel("Back", new Vector3(cartonSize.x, cartonSize.y, t), new Vector3(0f, 0f, -halfZ + t * 0.5f));
        AddPanel("Left", new Vector3(t, cartonSize.y, cartonSize.z - 2f * t), new Vector3(-halfX + t * 0.5f, 0f, 0f));
        AddPanel("Right", new Vector3(t, cartonSize.y, cartonSize.z - 2f * t), new Vector3(halfX - t * 0.5f, 0f, 0f));
        leftFlap = CreateFlap("LeftFlap", -halfX, halfY, cartonSize.x * 0.5f, t, 1f);
        rightFlap = CreateFlap("RightFlap", halfX, halfY, cartonSize.x * 0.5f, t, -1f);

        BoxCollider boxCollider = GetComponent<BoxCollider>();
        boxCollider.center = Vector3.zero;
        boxCollider.size = cartonSize;
        Rigidbody body = GetComponent<Rigidbody>();
        body.mass = Mathf.Max(0.2f, cartonSize.x * cartonSize.y * cartonSize.z * 2f);
        body.interpolation = RigidbodyInterpolation.Interpolate;

        displaysBuilt = false;
        if (showProductModelsBeforeOpening || isOpen)
        {
            BuildProductDisplays(columns, rows, layers, innerX, innerZ, innerY, perLayer);
            displaysBuilt = true;
        }
    }

    private void MeasureProductDimensions()
    {
        GameObject prefab = productData.productPrefab;
        EntityId cacheKey = prefab.GetEntityId();
        if (dimensionsCache.TryGetValue(cacheKey, out ProductDimensions cached))
        {
            fittedProductWidth = cached.width; fittedProductDepth = cached.depth;
            productHeight = cached.height; fittedProductCenterOffset = cached.centerOffset;
            return;
        }

        GameObject sample = Instantiate(prefab);
        sample.hideFlags = HideFlags.HideAndDontSave;
        sample.transform.position = Vector3.zero;
        sample.transform.rotation = Quaternion.Euler(productData.rotation);
        Renderer[] renderers = sample.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;
        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer == null) continue;
            if (!hasBounds) { bounds = itemRenderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(itemRenderer.bounds);
        }

        ProductDimensions dimensions;
        if (hasBounds)
        {
            dimensions.width = Mathf.Max(0.01f, bounds.size.x);
            dimensions.depth = Mathf.Max(0.01f, bounds.size.z);
            dimensions.height = Mathf.Max(0.01f, bounds.size.y);
            dimensions.centerOffset = sample.transform.InverseTransformPoint(bounds.center);
        }
        else
        {
            dimensions.width = productFootprint; dimensions.depth = productFootprint;
            dimensions.height = Mathf.Max(0.01f, productHeight); dimensions.centerOffset = Vector3.zero;
            Debug.LogWarning("Product prefab has no Renderer; using fallback carton dimensions.", productData);
        }
        dimensionsCache[cacheKey] = dimensions;
        fittedProductWidth = dimensions.width; fittedProductDepth = dimensions.depth;
        productHeight = dimensions.height; fittedProductCenterOffset = dimensions.centerOffset;
        if (Application.isPlaying) Destroy(sample); else DestroyImmediate(sample);
    }

    private void BuildProductDisplays(int cols, int rowCount, int layers, float innerX, float innerZ, float innerY, int perLayer)
    {
        for (int i = 0; i < remainingCount; i++)
        {
            GameObject display = Instantiate(productData.productPrefab, visualRoot);
            display.name = $"Inside_{productData.productName}_{i + 1}";
            DisableDisplayPhysicsAndScripts(display);
            int layer = i / perLayer, indexInLayer = i % perLayer;
            int col = indexInLayer % cols, row = indexInLayer / cols;
            Vector3 visualCenter = new Vector3(
                -innerX * 0.5f + fittedProductWidth * 0.5f + col * (fittedProductWidth + productSpacing),
                -cartonSize.y * 0.5f + wallThickness + padding + productHeight * (layer + 0.5f),
                -innerZ * 0.5f + fittedProductDepth * 0.5f + row * (fittedProductDepth + productSpacing));
            Quaternion productRotation = Quaternion.Euler(productData.rotation);
            Vector3 pivotToVisualCenter = productRotation * Vector3.Scale(productData.productPrefab.transform.localScale, fittedProductCenterOffset);
            display.transform.localPosition = visualCenter - pivotToVisualCenter;
            display.transform.localRotation = productRotation;
            display.transform.localScale = productData.productPrefab.transform.localScale;
            display.SetActive(showProductModelsBeforeOpening || isOpen);
            productDisplays.Add(display);
        }
    }

    private Transform CreateFlap(string flapName, float hingeX, float topY, float flapWidth, float thickness, float direction)
    {
        Transform pivot = new GameObject(flapName).transform;
        pivot.SetParent(visualRoot, false);
        pivot.localPosition = new Vector3(hingeX, topY, 0f);
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "CardboardPanel";
        panel.transform.SetParent(pivot, false);
        panel.transform.localPosition = new Vector3(direction * flapWidth * 0.5f, 0f, 0f);
        panel.transform.localScale = new Vector3(flapWidth, thickness, cartonSize.z);
        ApplyCartonMaterial(panel);
        return pivot;
    }

    private void AddPanel(string panelName, Vector3 size, Vector3 localPosition)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = panelName; panel.transform.SetParent(visualRoot, false);
        panel.transform.localPosition = localPosition; panel.transform.localScale = size;
        ApplyCartonMaterial(panel);
    }

    private void ApplyCartonMaterial(GameObject panel)
    {
        Collider panelCollider = panel.GetComponent<Collider>();
        if (panelCollider != null) Destroy(panelCollider);
        Renderer panelRenderer = panel.GetComponent<Renderer>();
        if (panelRenderer != null) panelRenderer.sharedMaterial = cartonMaterial;
    }

    private static void DisableDisplayPhysicsAndScripts(GameObject display)
    {
        foreach (Collider itemCollider in display.GetComponentsInChildren<Collider>(true)) itemCollider.enabled = false;
        foreach (Rigidbody itemBody in display.GetComponentsInChildren<Rigidbody>(true))
        { itemBody.isKinematic = true; itemBody.useGravity = false; }
        foreach (MonoBehaviour behaviour in display.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
    }

    public bool Open()
    {
        if (isOpen || IsEmpty || productData == null) return false;
        isOpen = true;
        if (!displaysBuilt && visualRoot != null)
        {
            int perLayer = Mathf.Max(1, columns * rows);
            int layers = Mathf.Max(1, Mathf.CeilToInt((float)remainingCount / perLayer));
            float innerX = columns * fittedProductWidth + (columns - 1) * productSpacing;
            float innerZ = rows * fittedProductDepth + (rows - 1) * productSpacing;
            float innerY = layers * productHeight;
            BuildProductDisplays(columns, rows, layers, innerX, innerZ, innerY, perLayer);
            displaysBuilt = true;
        }
        foreach (GameObject display in productDisplays) if (display != null) display.SetActive(true);
        if (leftFlap != null) leftFlap.localRotation = Quaternion.Euler(0f, 0f, 125f);
        if (rightFlap != null) rightFlap.localRotation = Quaternion.Euler(0f, 0f, -125f);
        return true;
    }

    public bool Close()
    {
        if (!isOpen) return false;
        isOpen = false;
        foreach (GameObject display in productDisplays) if (display != null) display.SetActive(showProductModelsBeforeOpening);
        if (leftFlap != null) leftFlap.localRotation = Quaternion.identity;
        if (rightFlap != null) rightFlap.localRotation = Quaternion.identity;
        return true;
    }

    public GameObject TakeOne()
    {
        if (!isOpen || IsEmpty || productData == null || productData.productPrefab == null) return null;
        GameObject product = Instantiate(productData.productPrefab);
        if (product == null) return null;
        product.name = productData.productName;
        int last = productDisplays.Count - 1;
        if (last >= 0)
        {
            GameObject display = productDisplays[last];
            productDisplays.RemoveAt(last);
            if (display != null) Destroy(display);
        }
        remainingCount--;
        return product;
    }

    public void GiveBack(GameObject product)
    {
        if (product == null || productData == null) return;
        Destroy(product);
        remainingCount++;
        BuildCarton();
        if (isOpen) Open();
    }

    public void Vanish() { Destroy(gameObject); }

    private void ClearGeneratedVisuals()
    {
        if (visualRoot != null)
        {
            if (Application.isPlaying) Destroy(visualRoot.gameObject); else DestroyImmediate(visualRoot.gameObject);
        }
        productDisplays.Clear(); displaysBuilt = false; leftFlap = null; rightFlap = null;
        if (cartonMaterial != null)
        {
            if (Application.isPlaying) Destroy(cartonMaterial); else DestroyImmediate(cartonMaterial);
            cartonMaterial = null;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        customCount = Mathf.Max(1, customCount); columns = Mathf.Max(1, columns); rows = Mathf.Max(1, rows);
        productFootprint = Mathf.Max(0.005f, productFootprint); productHeight = Mathf.Max(0.005f, productHeight);
        wallThickness = Mathf.Max(0.005f, wallThickness); productSpacing = Mathf.Max(0f, productSpacing);
        padding = Mathf.Max(0f, padding);
    }
#endif
}