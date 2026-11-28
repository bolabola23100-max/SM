using System.Collections.Generic;
using System.Collections;
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
    [Min(0.05f)][SerializeField] private float maxCartonWidth = 0.70f;
    [Min(0.01f)][SerializeField] private float flapOpenDuration = 0.22f;

    [Header("Appearance")]
    [SerializeField] private Color cartonColor = new Color(0.62f, 0.40f, 0.22f);
    [SerializeField] private Color cartonFrameColor = new Color(0.30f, 0.16f, 0.08f);
    [Min(0.005f)][SerializeField] private float frameWidth = 0.025f;
    [SerializeField] private bool showProductModelsBeforeOpening = false;
    [SerializeField] private bool buildOnAwake = true;

    [Header("Carton Tape")]
    [SerializeField] private Color tapeColor = new Color(0.94f, 0.94f, 0.90f);
    [Min(0.005f)][SerializeField] private float tapeWidth = 0.055f;
    [Min(0.001f)][SerializeField] private float tapeThickness = 0.003f;

    [Header("Runtime state")]
    [SerializeField] private bool isOpen;

    private readonly List<GameObject> productDisplays = new List<GameObject>();
    private Transform visualRoot;
    private Transform leftFlap;
    private Transform rightFlap;
    private Material cartonMaterial;
    private Material cartonFrameMaterial;\n    private Material tapeMaterial;
    private int remainingCount;
    private Vector3 cartonSize;
    private float fittedProductWidth;
    private float fittedProductDepth;
    private Vector3 fittedProductCenterOffset;
    private bool displaysBuilt;
    private Coroutine flapAnimation;

    private struct ProductDimensions
    {
        public float width, depth, height;
        public Vector3 centerOffset;
    }
    private static readonly Dictionary<EntityId, ProductDimensions> dimensionsCache = new Dictionary<EntityId, ProductDimensions>();
    private struct CartonLayout { public int columns, rows, layers; public float innerX, innerZ, innerY; public Vector3 size; }
    private static readonly Dictionary<string, CartonLayout> layoutCache = new Dictionary<string, CartonLayout>();

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

        productSpacing = Mathf.Clamp(productSpacing, 0.004f, 0.012f);
        padding = Mathf.Clamp(padding, 0.012f, 0.025f);
        string key = productData.productPrefab.GetEntityId().ToString() + ":" + remainingCount;
        CartonLayout layout;
        if (!layoutCache.TryGetValue(key, out layout))
        {
            layout = FindBestLayout(remainingCount);
            layoutCache[key] = layout;
        }
        columns = layout.columns; rows = layout.rows;
        int perLayer = columns * rows, layers = layout.layers;
        float innerX = layout.innerX, innerZ = layout.innerZ, innerY = layout.innerY;
        cartonSize = layout.size;

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
        cartonFrameMaterial = new Material(cartonMaterial);
        cartonFrameMaterial.color = cartonFrameColor;\n        tapeMaterial = new Material(cartonMaterial);\n        tapeMaterial.color = tapeColor;

        float halfX = cartonSize.x * 0.5f, halfY = cartonSize.y * 0.5f, halfZ = cartonSize.z * 0.5f;
        float t = wallThickness;
        AddPanel("Bottom", new Vector3(cartonSize.x, t, cartonSize.z), new Vector3(0f, -halfY + t * 0.5f, 0f));
        AddPanel("Front", new Vector3(cartonSize.x, cartonSize.y, t), new Vector3(0f, 0f, halfZ - t * 0.5f));
        AddPanel("Back", new Vector3(cartonSize.x, cartonSize.y, t), new Vector3(0f, 0f, -halfZ + t * 0.5f));
        AddPanel("Left", new Vector3(t, cartonSize.y, cartonSize.z - 2f * t), new Vector3(-halfX + t * 0.5f, 0f, 0f));
        AddPanel("Right", new Vector3(t, cartonSize.y, cartonSize.z - 2f * t), new Vector3(halfX - t * 0.5f, 0f, 0f));
        leftFlap = CreateFlap("LeftFlap", -halfX, halfY, cartonSize.x * 0.5f, t, 1f);
        rightFlap = CreateFlap("RightFlap", halfX, halfY, cartonSize.x * 0.5f, t, -1f);

        // Dark, slightly raised cardboard border strips around the four outer walls.
        AddWallFrame(halfX, halfY, halfZ, t);\n        AddCartonTape(halfX, halfY, halfZ);

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

    private CartonLayout FindBestLayout(int count)
    {
        CartonLayout best = new CartonLayout();
        float bestScore = float.MaxValue;
        for (int layerCount = 1; layerCount <= count; layerCount++)
        {
            int unitsPerLayer = Mathf.CeilToInt((float)count / layerCount);
            for (int rowCount = 1; rowCount <= unitsPerLayer; rowCount++)
            {
                int colCount = Mathf.CeilToInt((float)unitsPerLayer / rowCount);
                float ix = colCount * fittedProductWidth + (colCount - 1) * productSpacing;
                float iz = rowCount * fittedProductDepth + (rowCount - 1) * productSpacing;
                float x = ix + 2f * (padding + wallThickness), z = iz + 2f * (padding + wallThickness);
                if (x > maxCartonWidth) continue;
                float y = layerCount * productHeight + 2f * padding + wallThickness;
                float volume = x * z * y;
                float ratio = Mathf.Max(x, z, y) / Mathf.Max(0.001f, Mathf.Min(x, z, y));
                float score = volume * (1f + Mathf.Max(0f, ratio - 1.4f) * 0.45f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best.columns = colCount; best.rows = rowCount; best.layers = layerCount;
                    best.innerX = ix; best.innerZ = iz; best.innerY = layerCount * productHeight;
                    best.size = new Vector3(x, y, z);
                }
            }
        }
        if (bestScore == float.MaxValue)
        {
            best.columns = 1; best.rows = 1; best.layers = count;
            best.innerX = fittedProductWidth; best.innerZ = fittedProductDepth;
            best.innerY = count * productHeight;
            best.size = new Vector3(fittedProductWidth + 2f * (padding + wallThickness),
                best.innerY + 2f * padding + wallThickness, fittedProductDepth + 2f * (padding + wallThickness));
        }
        return best;
    }

    private void AnimateFlaps(bool open)
    {
        if (flapAnimation != null) StopCoroutine(flapAnimation);
        flapAnimation = StartCoroutine(FlapTween(open));
    }

    private IEnumerator FlapTween(bool open)
    {
        Quaternion ls = leftFlap != null ? leftFlap.localRotation : Quaternion.identity;
        Quaternion rs = rightFlap != null ? rightFlap.localRotation : Quaternion.identity;
        Quaternion le = open ? Quaternion.Euler(0f, 0f, 125f) : Quaternion.identity;
        Quaternion re = open ? Quaternion.Euler(0f, 0f, -125f) : Quaternion.identity;
        float elapsed = 0f, duration = Mathf.Max(0.01f, flapOpenDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            if (leftFlap != null) leftFlap.localRotation = Quaternion.Slerp(ls, le, t);
            if (rightFlap != null) rightFlap.localRotation = Quaternion.Slerp(rs, re, t);
            yield return null;
        }
        if (leftFlap != null) leftFlap.localRotation = le;
        if (rightFlap != null) rightFlap.localRotation = re;
        flapAnimation = null;
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

            display.transform.localPosition = Vector3.zero;
            display.transform.localRotation = Quaternion.Euler(productData.rotation);
            display.transform.localScale = productData.productPrefab.transform.localScale;

            // Align using the actual instantiated renderers' bounds, not the prefab pivot.
            Renderer[] renderers = display.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            foreach (Renderer itemRenderer in renderers)
            {
                if (itemRenderer == null) continue;
                if (!hasBounds) { bounds = itemRenderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(itemRenderer.bounds);
            }
            if (hasBounds)
            {
                Vector3 centerInCartonSpace = visualRoot.InverseTransformPoint(bounds.center);
                display.transform.localPosition += visualCenter - centerInCartonSpace;
            }
            else
            {
                display.transform.localPosition = visualCenter;
            }

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

    private void AddWallFrame(float halfX, float halfY, float halfZ, float wallT)
    {
        float w = Mathf.Min(frameWidth, cartonSize.x * 0.08f);
        float yBottom = -halfY + w * 0.5f;
        float yTop = halfY - w * 0.5f;
        float zFront = halfZ + w * 0.15f;
        float zBack = -halfZ - w * 0.15f;
        float xLeft = -halfX - w * 0.15f;
        float xRight = halfX + w * 0.15f;

        // Front and back face frames: horizontal top/bottom and vertical sides.
        foreach (float z in new float[] { zFront, zBack })
        {
            CreateTrimBar(new Vector3(cartonSize.x, w, w), new Vector3(0f, yTop, z));
            CreateTrimBar(new Vector3(cartonSize.x, w, w), new Vector3(0f, yBottom, z));
            CreateTrimBar(new Vector3(w, cartonSize.y, w), new Vector3(xLeft, 0f, z));
            CreateTrimBar(new Vector3(w, cartonSize.y, w), new Vector3(xRight, 0f, z));
        }
        // Side face frames, with top and bottom strips and front/back vertical edges.
        foreach (float x in new float[] { xLeft, xRight })
        {
            CreateTrimBar(new Vector3(w, w, cartonSize.z), new Vector3(x, yTop, 0f));
            CreateTrimBar(new Vector3(w, w, cartonSize.z), new Vector3(x, yBottom, 0f));
            CreateTrimBar(new Vector3(w, cartonSize.y, w), new Vector3(x, 0f, zFront));
            CreateTrimBar(new Vector3(w, cartonSize.y, w), new Vector3(x, 0f, zBack));
        }
    }

    private void AddCartonTape(float halfX, float halfY, float halfZ)
    {
        float tapeW = Mathf.Min(tapeWidth, cartonSize.x * 0.35f);
        // Tape runs centered over the carton top and down both end faces.
        CreateTapeSegment(new Vector3(tapeW, tapeThickness, cartonSize.z),
            new Vector3(0f, halfY + tapeThickness * 0.5f, 0f));
        CreateTapeSegment(new Vector3(tapeW, cartonSize.y, tapeThickness),
            new Vector3(0f, 0f, halfZ + tapeThickness * 0.5f));
        CreateTapeSegment(new Vector3(tapeW, cartonSize.y, tapeThickness),
            new Vector3(0f, 0f, -halfZ - tapeThickness * 0.5f));
    }

    private void CreateTapeSegment(Vector3 size, Vector3 localPosition)
    {
        GameObject tape = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tape.name = "WhiteCartonTape";
        tape.transform.SetParent(visualRoot, false);
        tape.transform.localPosition = localPosition;
        tape.transform.localScale = size;
        Collider col = tape.GetComponent<Collider>();
        if (col != null) Destroy(col);
        Renderer rend = tape.GetComponent<Renderer>();
        if (rend != null && tapeMaterial != null) rend.sharedMaterial = tapeMaterial;
    }

    private void CreateTrimBar(Vector3 size, Vector3 localPosition)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "DarkCartonFrame";
        bar.transform.SetParent(visualRoot, false);
        bar.transform.localPosition = localPosition;
        bar.transform.localScale = size;
        Collider col = bar.GetComponent<Collider>();
        if (col != null) Destroy(col);
        Renderer rend = bar.GetComponent<Renderer>();
        if (rend != null && cartonFrameMaterial != null) rend.sharedMaterial = cartonFrameMaterial;
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
        AnimateFlaps(true);
        return true;
    }

    public bool Close()
    {
        if (!isOpen) return false;
        isOpen = false;
        foreach (GameObject display in productDisplays) if (display != null) display.SetActive(showProductModelsBeforeOpening);
        AnimateFlaps(false);
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