using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A self-contained, procedural carton. Assign one ProductData asset and the
/// carton automatically displays that product's prefab inside it.
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
    [Range(0.25f, 1.5f)][SerializeField] private float wallHeightRatio = 0.72f;

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

    public ProductData Data => productData;
    public int RemainingCount => remainingCount;
    public bool IsOpen => isOpen;
    public bool IsEmpty => remainingCount <= 0;
    public string ProductName => productData != null ? productData.productName : string.Empty;

    private void Awake()
    {
        if (buildOnAwake)
            BuildCarton();
    }

    /// <summary>Assigns the product selected by the shop/purchase system.</summary>
    public void SetProduct(ProductData data, int amount = -1)
    {
        productData = data;
        if (amount > 0)
        {
            useProductPackSize = false;
            customCount = amount;
        }
        else
        {
            useProductPackSize = true;
        }

        remainingCount = data != null
            ? (useProductPackSize ? Mathf.Max(1, data.packSize) : Mathf.Max(1, customCount))
            : 0;

        if (Application.isPlaying)
            BuildCarton();
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

        remainingCount = useProductPackSize
            ? Mathf.Max(1, productData.packSize)
            : Mathf.Max(1, customCount);

        MeasureProductDimensions();

        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);

        // A pack can contain more than one layer. The selected product is the
        // actual prefab referenced by the ProductData asset.
        int perLayer = columns * rows;
        int layers = Mathf.CeilToInt((float)remainingCount / perLayer);

        float innerX = columns * fittedProductWidth + (columns - 1) * productSpacing;
        float innerZ = rows * fittedProductDepth + (rows - 1) * productSpacing;
        float innerY = layers * productHeight;

        cartonSize = new Vector3(
            innerX + 2f * (padding + wallThickness),
            innerY + 2f * padding + wallThickness,
            innerZ + 2f * (padding + wallThickness));

        visualRoot = new GameObject("CartonVisuals").transform;
        visualRoot.SetParent(transform, false);

        cartonMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (cartonMaterial.shader == null)
            cartonMaterial.shader = Shader.Find("Standard");
        cartonMaterial.color = cartonColor;

        float halfX = cartonSize.x * 0.5f;
        float halfY = cartonSize.y * 0.5f;
        float halfZ = cartonSize.z * 0.5f;
        float t = wallThickness;

        // Bottom and four cardboard sides.
        AddPanel("Bottom", new Vector3(cartonSize.x, t, cartonSize.z),
            new Vector3(0f, -halfY + t * 0.5f, 0f));
        AddPanel("Front", new Vector3(cartonSize.x, cartonSize.y, t),
            new Vector3(0f, 0f, halfZ - t * 0.5f));
        AddPanel("Back", new Vector3(cartonSize.x, cartonSize.y, t),
            new Vector3(0f, 0f, -halfZ + t * 0.5f));
        AddPanel("Left", new Vector3(t, cartonSize.y, cartonSize.z - 2f * t),
            new Vector3(-halfX + t * 0.5f, 0f, 0f));
        AddPanel("Right", new Vector3(t, cartonSize.y, cartonSize.z - 2f * t),
            new Vector3(halfX - t * 0.5f, 0f, 0f));

        // Two hinged top flaps.
        leftFlap = CreateFlap("LeftFlap", -halfX, halfY, cartonSize.x * 0.5f, t, 1f);
        rightFlap = CreateFlap("RightFlap", halfX, halfY, cartonSize.x * 0.5f, t, -1f);

        // Keep the physical collider in sync with the generated carton.
        BoxCollider boxCollider = GetComponent<BoxCollider>();
        boxCollider.center = Vector3.zero;
        boxCollider.size = cartonSize;

        Rigidbody body = GetComponent<Rigidbody>();
        body.mass = Mathf.Max(0.2f, cartonSize.x * cartonSize.y * cartonSize.z * 2f);
        body.interpolation = RigidbodyInterpolation.Interpolate;

        BuildProductDisplays(columns, rows, layers, innerX, innerZ, innerY, perLayer);
    }


    // Read the actual visible size of the selected product prefab, including its scale
    // and the rotation used when it is displayed inside the carton.
    private void MeasureProductDimensions()
    {
        GameObject sample = Instantiate(productData.productPrefab);
        sample.hideFlags = HideFlags.HideAndDontSave;
        sample.transform.position = Vector3.zero;
        sample.transform.rotation = Quaternion.Euler(productData.rotation);

        Renderer[] renderers = sample.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = itemRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(itemRenderer.bounds);
            }
        }

        if (hasBounds)
        {
            fittedProductWidth = Mathf.Max(0.01f, bounds.size.x);
            fittedProductDepth = Mathf.Max(0.01f, bounds.size.z);
            productHeight = Mathf.Max(0.01f, bounds.size.y);
            // Compensate for prefabs whose pivot is not at the visual center.
            fittedProductCenterOffset = sample.transform.InverseTransformPoint(bounds.center);
        }
        else
        {
            fittedProductWidth = productFootprint;
            fittedProductDepth = productFootprint;
            productHeight = Mathf.Max(0.01f, productHeight);
            fittedProductCenterOffset = Vector3.zero;
            Debug.LogWarning("Product prefab has no Renderer; using the fallback carton dimensions.", productData);
        }

        if (Application.isPlaying)
            Destroy(sample);
        else
            DestroyImmediate(sample);
    }

    private void BuildProductDisplays(int cols, int rowCount, int layers,
        float innerX, float innerZ, float innerY, int perLayer)
    {
        for (int i = 0; i < remainingCount; i++)
        {
            GameObject display = Instantiate(productData.productPrefab, visualRoot);
            display.name = $"Inside_{productData.productName}_{i + 1}";
            DisableDisplayPhysicsAndScripts(display);

            int layer = i / perLayer;
            int indexInLayer = i % perLayer;
            int col = indexInLayer % cols;
            int row = indexInLayer / cols;

            Vector3 visualCenter = new Vector3(
                -innerX * 0.5f + fittedProductWidth * 0.5f + col * (fittedProductWidth + productSpacing),
                -cartonSize.y * 0.5f + wallThickness + padding + productHeight * (layer + 0.5f),
                -innerZ * 0.5f + fittedProductDepth * 0.5f + row * (fittedProductDepth + productSpacing));

            Quaternion productRotation = Quaternion.Euler(productData.rotation);
            Vector3 pivotToVisualCenter = productRotation * Vector3.Scale(
                productData.productPrefab.transform.localScale, fittedProductCenterOffset);
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
        panel.name = panelName;
        panel.transform.SetParent(visualRoot, false);
        panel.transform.localPosition = localPosition;
        panel.transform.localScale = size;
        ApplyCartonMaterial(panel);
    }

    private void ApplyCartonMaterial(GameObject panel)
    {
        Collider panelCollider = panel.GetComponent<Collider>();
        if (panelCollider != null)
            Destroy(panelCollider);

        Renderer panelRenderer = panel.GetComponent<Renderer>();
        if (panelRenderer != null)
            panelRenderer.sharedMaterial = cartonMaterial;
    }

    private static void DisableDisplayPhysicsAndScripts(GameObject display)
    {
        foreach (Collider itemCollider in display.GetComponentsInChildren<Collider>(true))
            itemCollider.enabled = false;

        foreach (Rigidbody itemBody in display.GetComponentsInChildren<Rigidbody>(true))
        {
            itemBody.isKinematic = true;
            itemBody.useGravity = false;
        }

        // The displayed copies are visual-only. Their gameplay scripts must not
        // run while they are still inside the carton.
        foreach (MonoBehaviour behaviour in display.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
    }

    public bool Open()
    {
        if (isOpen || IsEmpty || productData == null)
            return false;

        isOpen = true;
        foreach (GameObject display in productDisplays)
            if (display != null) display.SetActive(true);

        if (leftFlap != null)
            leftFlap.localRotation = Quaternion.Euler(0f, 0f, 125f);
        if (rightFlap != null)
            rightFlap.localRotation = Quaternion.Euler(0f, 0f, -125f);

        return true;
    }

    public bool Close()
    {
        if (!isOpen)
            return false;

        isOpen = false;
        foreach (GameObject display in productDisplays)
            if (display != null) display.SetActive(showProductModelsBeforeOpening);

        if (leftFlap != null) leftFlap.localRotation = Quaternion.identity;
        if (rightFlap != null) rightFlap.localRotation = Quaternion.identity;
        return true;
    }

    /// <summary>
    /// Creates one real product object. The carton count decreases only after
    /// the product instance has been created successfully.
    /// </summary>
    public GameObject TakeOne()
    {
        if (!isOpen || IsEmpty || productData == null || productData.productPrefab == null)
            return null;

        GameObject product = Instantiate(productData.productPrefab);
        if (product == null)
            return null;

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
        if (product == null || productData == null)
            return;

        Destroy(product);
        remainingCount++;
        BuildCarton();
        if (isOpen) Open();
    }

    public void Vanish()
    {
        Destroy(gameObject);
    }

    private void ClearGeneratedVisuals()
    {
        if (visualRoot != null)
        {
            if (Application.isPlaying) Destroy(visualRoot.gameObject);
            else DestroyImmediate(visualRoot.gameObject);
        }

        productDisplays.Clear();
        leftFlap = null;
        rightFlap = null;

        if (cartonMaterial != null)
        {
            if (Application.isPlaying) Destroy(cartonMaterial);
            else DestroyImmediate(cartonMaterial);
            cartonMaterial = null;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        customCount = Mathf.Max(1, customCount);
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        productFootprint = Mathf.Max(0.005f, productFootprint);
        productHeight = Mathf.Max(0.005f, productHeight);
        wallThickness = Mathf.Max(0.005f, wallThickness);
        productSpacing = Mathf.Max(0f, productSpacing);
        padding = Mathf.Max(0f, padding);
    }
#endif
}
