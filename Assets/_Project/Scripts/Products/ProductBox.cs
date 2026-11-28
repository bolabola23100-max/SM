using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural carton fitted to the product prefab and its pack size.
/// - The count is never reset by a rebuild (capacity vs remainingCount).
/// - Static carton geometry is merged into ONE mesh (cached and shared between cartons).
/// - Materials and meshes are shared; nothing is allocated per carton except a few GameObjects.
/// - Products inside are pre-built "slots" that are only toggled on/off (no Instantiate/Destroy on take/give back).
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
    [Min(0.001f)][SerializeField] private float productSpacing = 0.01f;
    [Min(0.005f)][SerializeField] private float wallThickness = 0.018f;
    [Min(0f)][SerializeField] private float padding = 0.02f;
    [Min(0.05f)][SerializeField] private float maxCartonWidth = 0.70f;
    [Tooltip("Used only if the product prefab has no MeshFilter/MeshRenderer.")]
    [Min(0.005f)][SerializeField] private float productFootprint = 0.18f;
    [Tooltip("Used only if the product prefab has no MeshFilter/MeshRenderer.")]
    [Min(0.005f)][SerializeField] private float productHeight = 0.22f;

    [Header("Flaps")]
    [Min(0.01f)][SerializeField] private float flapOpenDuration = 0.22f;
    [Range(60f, 170f)][SerializeField] private float flapOpenAngle = 125f;

    [Header("Physics")]
    [Min(0f)][SerializeField] private float cartonMass = 0.15f;
    [Min(0f)][SerializeField] private float unitMass = 0.05f;

    [Header("Rendering")]
    [Tooltip("Assign a Lit shader (e.g. URP/Lit) so it is included in mobile builds. Shader.Find is only a fallback.")]
    [SerializeField] private Shader cartonShader;
    [SerializeField] private bool showProductModelsBeforeOpening = false;
    [SerializeField] private bool buildOnAwake = true;

    [Header("Appearance")]
    [SerializeField] private Color cartonColor = new Color(0.62f, 0.40f, 0.22f);
    [SerializeField] private Color cartonFrameColor = new Color(0.30f, 0.16f, 0.08f);
    [Min(0.005f)][SerializeField] private float frameWidth = 0.025f;

    [Header("Carton Details")]
    [SerializeField] private Color creaseColor = new Color(0.48f, 0.30f, 0.16f);
    [Min(0.001f)][SerializeField] private float creaseThickness = 0.002f;
    [SerializeField] private bool showShippingLabel = true;
    [SerializeField] private Color labelColor = new Color(0.88f, 0.84f, 0.76f);

    [Header("Carton Tape")]
    [SerializeField] private Color tapeColor = new Color(0.94f, 0.94f, 0.90f);
    [Min(0.005f)][SerializeField] private float tapeWidth = 0.055f;
    [Min(0.002f)][SerializeField] private float tapeThickness = 0.003f;

    [Header("Runtime state")]
    [SerializeField] private bool isOpen;

    // ------------------------------------------------------------------ events
    public event System.Action<ProductBox> Opened;
    public event System.Action<ProductBox> Emptied;
    public event System.Action<ProductBox, int> CountChanged;

    // ------------------------------------------------------------------ runtime state
    private int remainingCount;
    private int capacity;
    private bool countInitialized;
    private bool tapeCut;
    private bool displaysBuilt;

    private Vector3 cartonSize;
    private CartonLayout layout;
    private ProductVisual productVisual;

    private Transform visualRoot;
    private Transform leftFlap;
    private Transform rightFlap;
    private GameObject tapeObject;
    private readonly List<GameObject> slots = new List<GameObject>();
    private Coroutine flapAnimation;

    // ------------------------------------------------------------------ shared caches (static)
    private const int SlotCarton = 0, SlotFrame = 1, SlotDetail = 2, SlotLabel = 3;

    private struct CartonLayout
    {
        public int columns, rows, layers;
        public float innerX, innerZ;
        public Vector3 size;
    }

    private sealed class ProductPart
    {
        public Mesh mesh;
        public Material[] materials;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    private sealed class ProductVisual
    {
        public readonly List<ProductPart> parts = new List<ProductPart>();
        public float width, depth, height;
        public Vector3 center;
    }

    private sealed class VisualSet
    {
        public Mesh body;
        public int[] slotIds;
        public bool hasSprite;
        public Sprite sprite;
        public Mesh flapFrame;          // brown frame (flap-local space)
        public Vector3 flapImagePos;    // picture position (flap-local space)
        public float flapImageScale;
    }

    private static readonly Dictionary<(EntityId, Vector3), ProductVisual> productCache = new Dictionary<(EntityId, Vector3), ProductVisual>();
    private static readonly Dictionary<string, CartonLayout> layoutCache = new Dictionary<string, CartonLayout>();
    private static readonly Dictionary<string, VisualSet> visualCache = new Dictionary<string, VisualSet>();
    private static readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
    private static Mesh cubeMesh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        productCache.Clear();
        layoutCache.Clear();
        visualCache.Clear();
        materialCache.Clear();
        cubeMesh = null;
    }

    // ------------------------------------------------------------------ public API
    public ProductData Data => productData;
    public int RemainingCount => remainingCount;
    public int Capacity => capacity;
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

        countInitialized = false;
        remainingCount = data != null ? ComputeCapacity() : 0;
        if (Application.isPlaying) BuildCarton();
    }

    /// <summary>Sets how many products are left (e.g. when loading a save).</summary>
    public void SetRemainingCount(int count)
    {
        if (productData == null) return;
        remainingCount = Mathf.Clamp(count, 0, Mathf.Max(capacity, 0));
        countInitialized = true;
        RefreshSlotVisibility();
        UpdateMass();
        CountChanged?.Invoke(this, remainingCount);
    }

    public bool Open()
    {
        if (isOpen || IsEmpty || productData == null || visualRoot == null) return false;
        isOpen = true;
        tapeCut = true;
        if (tapeObject != null) tapeObject.SetActive(false);
        BuildProductSlots();
        RefreshSlotVisibility();
        AnimateFlaps(true);
        Opened?.Invoke(this);
        return true;
    }

    public bool Close()
    {
        if (!isOpen) return false;
        isOpen = false;
        RefreshSlotVisibility();
        AnimateFlaps(false);
        return true;
    }

    /// <summary>Takes one product out. It is spawned at the top of the carton.</summary>
    public GameObject TakeOne()
    {
        if (productData == null || productData.productPrefab == null) return null;
        Vector3 pos = transform.position + transform.up * (cartonSize.y * 0.5f);
        return TakeOne(pos, productData.productPrefab.transform.rotation);
    }

    public GameObject TakeOne(Vector3 position, Quaternion rotation)
    {
        if (!isOpen || IsEmpty || productData == null || productData.productPrefab == null) return null;

        GameObject product = Instantiate(productData.productPrefab, position, rotation);
        product.name = productData.productName;

        remainingCount--;
        if (remainingCount >= 0 && remainingCount < slots.Count && slots[remainingCount] != null)
            slots[remainingCount].SetActive(false);

        UpdateMass();
        CountChanged?.Invoke(this, remainingCount);
        if (IsEmpty) Emptied?.Invoke(this);
        return product;
    }

    public bool TryTakeOne(out GameObject product)
    {
        product = TakeOne();
        return product != null;
    }

    /// <summary>Puts a product back. Returns false (and does nothing) if the carton is already full.</summary>
    public bool GiveBack(GameObject product)
    {
        if (product == null || productData == null || remainingCount >= capacity) return false;
        Destroy(product);

        if (remainingCount < slots.Count && slots[remainingCount] != null)
            slots[remainingCount].SetActive(isOpen || showProductModelsBeforeOpening);
        remainingCount++;

        UpdateMass();
        CountChanged?.Invoke(this, remainingCount);
        return true;
    }

    public void Vanish() { Destroy(gameObject); }

    // ------------------------------------------------------------------ build
    public void BuildCarton()
    {
        ClearGeneratedVisuals();

        if (productData == null || productData.productPrefab == null)
        {
            Debug.LogWarning($"ProductBox on '{name}' needs a ProductData with Product Prefab assigned.", this);
            remainingCount = 0;
            capacity = 0;
            countInitialized = false;
            return;
        }

        Shader litShader = ResolveCartonShader();
        if (litShader == null)
        {
            Debug.LogError("ProductBox: no compatible carton shader found. Assign one in the inspector.", this);
            return;
        }

        capacity = ComputeCapacity();
        if (!countInitialized || remainingCount > capacity)
        {
            remainingCount = capacity;
            countInitialized = true;
        }
        if (isOpen) tapeCut = true;

        productVisual = GetProductVisual();
        string layoutKey = LayoutKey();
        if (!layoutCache.TryGetValue(layoutKey, out layout))
        {
            layout = FindBestLayout(capacity);
            layoutCache[layoutKey] = layout;
        }
        cartonSize = layout.size;

        Sprite sprite = productData.productIcon;
        VisualSet set = GetVisualSet(layoutKey, sprite);

        Material matCarton = GetColorMaterial(litShader, cartonColor);
        Material matFrame = GetColorMaterial(litShader, cartonFrameColor);
        Material matDetail = GetColorMaterial(litShader, creaseColor);
        Material matLabel = GetColorMaterial(litShader, labelColor);
        Material matTape = GetColorMaterial(litShader, tapeColor);
        Material[] bySlot = { matCarton, matFrame, matDetail, matLabel };

        visualRoot = new GameObject("CartonVisuals").transform;
        visualRoot.SetParent(transform, false);

        // One merged mesh for walls, frame, seams and label.
        Material[] bodyMats = new Material[set.slotIds.Length];
        for (int i = 0; i < bodyMats.Length; i++) bodyMats[i] = bySlot[set.slotIds[i]];
        CreateMeshObject("Body", visualRoot, set.body, bodyMats);

        float halfX = cartonSize.x * 0.5f, halfY = cartonSize.y * 0.5f;
        float fw = FrameWidth(cartonSize);
        float flapW = cartonSize.x * 0.5f;
        float flapLen = cartonSize.z - 0.7f * fw;
        float pivotY = halfY - wallThickness * 0.5f; // flap top surface is flush with the carton top

        leftFlap = CreateFlap("LeftFlap", new Vector3(-halfX, pivotY, 0f), flapW, flapLen, 1f, matCarton);
        rightFlap = CreateFlap("RightFlap", new Vector3(halfX, pivotY, 0f), flapW, flapLen, -1f, matCarton);

        // Tape on top of the closed flaps. It disappears when the carton is opened.
        float tapeW = Mathf.Min(tapeWidth, cartonSize.x * 0.35f);
        tapeObject = CreateCube("Tape", visualRoot, matTape,
            new Vector3(tapeW, tapeThickness, cartonSize.z * 0.92f),
            new Vector3(0f, halfY + tapeThickness * 0.5f, 0f));
        tapeObject.SetActive(!tapeCut);

        // Framed product picture lying on the left flap (follows the flap when it opens).
        if (set.hasSprite)
        {
            CreateMeshObject("ImageFrame", leftFlap, set.flapFrame, new[] { matFrame });

            GameObject img = new GameObject("FlapImage");
            img.transform.SetParent(leftFlap, false);
            img.transform.localPosition = set.flapImagePos;
            img.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
            img.transform.localScale = Vector3.one * set.flapImageScale;
            SpriteRenderer sr = img.AddComponent<SpriteRenderer>();
            sr.sprite = set.sprite;
            sr.color = Color.white;
        }

        BoxCollider boxCollider = GetComponent<BoxCollider>();
        boxCollider.center = Vector3.zero;
        boxCollider.size = cartonSize;
        Rigidbody body = GetComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        UpdateMass();

        displaysBuilt = false;
        if (showProductModelsBeforeOpening || isOpen) BuildProductSlots();
        RefreshSlotVisibility();
        ApplyFlapPose(isOpen);
    }

    private int ComputeCapacity()
    {
        if (productData == null) return 0;
        return useProductPackSize ? Mathf.Max(1, productData.packSize) : Mathf.Max(1, customCount);
    }

    private string LayoutKey()
    {
        return $"{productData.productPrefab.GetEntityId()}|{productData.rotation}|{capacity}|{productSpacing}|{padding}|{wallThickness}|{maxCartonWidth}";
    }

    private float FrameWidth(Vector3 size) { return Mathf.Min(frameWidth, size.x * 0.08f); }

    // ------------------------------------------------------------------ layout
    private CartonLayout FindBestLayout(int count)
    {
        float w = productVisual.width, d = productVisual.depth, h = productVisual.height;
        float shell = 2f * (padding + wallThickness);

        CartonLayout best = new CartonLayout();
        float bestScore = float.MaxValue;

        for (int layerCount = 1; layerCount <= count; layerCount++)
        {
            int unitsPerLayer = Mathf.CeilToInt((float)count / layerCount);
            for (int rowCount = 1; rowCount <= unitsPerLayer; rowCount++)
            {
                int colCount = Mathf.CeilToInt((float)unitsPerLayer / rowCount);
                // Real number of layers for this grid (the grid can hold more than unitsPerLayer).
                int layersNeeded = Mathf.CeilToInt((float)count / (colCount * rowCount));

                float ix = colCount * w + (colCount - 1) * productSpacing;
                float iz = rowCount * d + (rowCount - 1) * productSpacing;
                float x = ix + shell, z = iz + shell;
                if (x > maxCartonWidth || z > maxCartonWidth) continue;

                float y = layersNeeded * h + 2f * padding + wallThickness;
                float volume = x * z * y;
                float ratio = Mathf.Max(x, z, y) / Mathf.Max(0.001f, Mathf.Min(x, z, y));
                float score = volume * (1f + Mathf.Max(0f, ratio - 1.4f) * 0.45f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best.columns = colCount; best.rows = rowCount; best.layers = layersNeeded;
                    best.innerX = ix; best.innerZ = iz;
                    best.size = new Vector3(x, y, z);
                }
            }
        }

        if (bestScore == float.MaxValue)
        {
            best.columns = 1; best.rows = 1; best.layers = count;
            best.innerX = w; best.innerZ = d;
            best.size = new Vector3(w + shell, count * h + 2f * padding + wallThickness, d + shell);
        }
        return best;
    }

    private Vector3 SlotCenter(int index)
    {
        int perLayer = Mathf.Max(1, layout.columns * layout.rows);
        int layer = index / perLayer, inLayer = index % perLayer;
        int col = inLayer % layout.columns, row = inLayer / layout.columns;
        float w = productVisual.width, d = productVisual.depth, h = productVisual.height;
        return new Vector3(
            -layout.innerX * 0.5f + w * 0.5f + col * (w + productSpacing),
            -cartonSize.y * 0.5f + wallThickness + padding + h * (layer + 0.5f),
            -layout.innerZ * 0.5f + d * 0.5f + row * (d + productSpacing));
    }

    // ------------------------------------------------------------------ product measuring (from meshes, no Instantiate)
    private ProductVisual GetProductVisual()
    {
        GameObject prefab = productData.productPrefab;
        var key = (prefab.GetEntityId(), productData.rotation);
        if (productCache.TryGetValue(key, out ProductVisual cached)) return cached;

        ProductVisual pv = new ProductVisual();
        Transform root = prefab.transform;
        Matrix4x4 baseMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(productData.rotation), root.localScale);

        Bounds bounds = new Bounds();
        bool hasBounds = false;

        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null) continue;

            Matrix4x4 m = baseMatrix * RelativeMatrix(mf.transform, root);

            Bounds mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!hasBounds) { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
                else bounds.Encapsulate(p);
            }

            pv.parts.Add(new ProductPart
            {
                mesh = mf.sharedMesh,
                materials = mr.sharedMaterials,
                position = m.GetColumn(3),
                rotation = m.rotation,
                scale = m.lossyScale
            });
        }

        if (hasBounds)
        {
            pv.width = Mathf.Max(0.01f, bounds.size.x);
            pv.depth = Mathf.Max(0.01f, bounds.size.z);
            pv.height = Mathf.Max(0.01f, bounds.size.y);
            pv.center = bounds.center;
        }
        else
        {
            pv.width = productFootprint;
            pv.depth = productFootprint;
            pv.height = Mathf.Max(0.01f, productHeight);
            pv.center = Vector3.zero;
            Debug.LogWarning("Product prefab has no MeshFilter/MeshRenderer; using fallback carton dimensions.", productData);
        }

        productCache[key] = pv;
        return pv;
    }

    private static Matrix4x4 RelativeMatrix(Transform t, Transform root)
    {
        Matrix4x4 m = Matrix4x4.identity;
        while (t != null && t != root)
        {
            m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * m;
            t = t.parent;
        }
        return m;
    }

    // ------------------------------------------------------------------ product slots
    private void BuildProductSlots()
    {
        if (displaysBuilt || visualRoot == null || productVisual == null) return;
        displaysBuilt = true;
        slots.Clear();

        for (int i = 0; i < capacity; i++)
        {
            GameObject slot = new GameObject($"Inside_{productData.productName}_{i + 1}");
            slot.transform.SetParent(visualRoot, false);
            slot.transform.localPosition = SlotCenter(i) - productVisual.center;

            foreach (ProductPart part in productVisual.parts)
            {
                GameObject child = new GameObject("Part");
                child.transform.SetParent(slot.transform, false);
                child.transform.localPosition = part.position;
                child.transform.localRotation = part.rotation;
                child.transform.localScale = part.scale;
                child.AddComponent<MeshFilter>().sharedMesh = part.mesh;
                MeshRenderer mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterials = part.materials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            slot.SetActive(false);
            slots.Add(slot);
        }
    }

    private void RefreshSlotVisibility()
    {
        bool visible = isOpen || showProductModelsBeforeOpening;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null) slots[i].SetActive(visible && i < remainingCount);
        }
    }

    // ------------------------------------------------------------------ cached carton visuals (mesh)
    private VisualSet GetVisualSet(string layoutKey, Sprite sprite)
    {
        string spriteId = sprite != null ? sprite.GetEntityId().ToString() : "none";
        string key = $"{layoutKey}|{spriteId}|{frameWidth}|{creaseThickness}|{showShippingLabel}|{tapeWidth}";
        if (visualCache.TryGetValue(key, out VisualSet cached) && cached.body != null) return cached;

        VisualSet set = BuildVisualSet(cartonSize, sprite);
        visualCache[key] = set;
        return set;
    }

    private VisualSet BuildVisualSet(Vector3 size, Sprite sprite)
    {
        var slotLists = new List<CombineInstance>[4];
        for (int i = 0; i < slotLists.Length; i++) slotLists[i] = new List<CombineInstance>();

        float halfX = size.x * 0.5f, halfY = size.y * 0.5f, halfZ = size.z * 0.5f;
        float t = wallThickness;

        // Walls
        AddCube(slotLists[SlotCarton], new Vector3(size.x, t, size.z), new Vector3(0f, -halfY + t * 0.5f, 0f));
        AddCube(slotLists[SlotCarton], new Vector3(size.x, size.y, t), new Vector3(0f, 0f, halfZ - t * 0.5f));
        AddCube(slotLists[SlotCarton], new Vector3(size.x, size.y, t), new Vector3(0f, 0f, -halfZ + t * 0.5f));
        AddCube(slotLists[SlotCarton], new Vector3(t, size.y, size.z - 2f * t), new Vector3(-halfX + t * 0.5f, 0f, 0f));
        AddCube(slotLists[SlotCarton], new Vector3(t, size.y, size.z - 2f * t), new Vector3(halfX - t * 0.5f, 0f, 0f));

        // Frame: 12 bars (4 horizontal on front/back faces... 4 on side faces... 4 vertical corners), no duplicates.
        float w = FrameWidth(size);
        float yTop = halfY - w * 0.5f, yBottom = -halfY + w * 0.5f;
        float zFront = halfZ + w * 0.15f, zBack = -halfZ - w * 0.15f;
        float xLeft = -halfX - w * 0.15f, xRight = halfX + w * 0.15f;
        var frame = slotLists[SlotFrame];

        foreach (float z in new[] { zFront, zBack })
        {
            AddCube(frame, new Vector3(size.x, w, w), new Vector3(0f, yTop, z));
            AddCube(frame, new Vector3(size.x, w, w), new Vector3(0f, yBottom, z));
        }
        foreach (float x in new[] { xLeft, xRight })
        {
            AddCube(frame, new Vector3(w, w, size.z), new Vector3(x, yTop, 0f));
            AddCube(frame, new Vector3(w, w, size.z), new Vector3(x, yBottom, 0f));
            AddCube(frame, new Vector3(w, size.y, w), new Vector3(x, 0f, zFront));
            AddCube(frame, new Vector3(w, size.y, w), new Vector3(x, 0f, zBack));
        }

        // Fold seams (front and back faces)
        float seam = Mathf.Max(0.001f, creaseThickness);
        AddCube(slotLists[SlotDetail], new Vector3(size.x * 0.72f, seam, seam), new Vector3(0f, halfY * 0.35f, halfZ + seam * 0.5f));
        AddCube(slotLists[SlotDetail], new Vector3(size.x * 0.72f, seam, seam), new Vector3(0f, halfY * 0.35f, -halfZ - seam * 0.5f));

        // Shipping label
        if (showShippingLabel)
        {
            float labelW = Mathf.Min(size.x * 0.32f, 0.16f);
            float labelH = Mathf.Min(size.y * 0.22f, 0.10f);
            AddCube(slotLists[SlotLabel], new Vector3(labelW, labelH, seam * 2f),
                new Vector3(halfX * 0.42f, halfY * 0.28f, halfZ + seam));
        }

        VisualSet set = new VisualSet();

        // Product picture (framed) on the top-left flap. Coordinates are in the left flap's local space:
        // x runs from the outer hinge (0) to the carton centre (flapW), z along the carton depth.
        if (sprite != null && sprite.bounds.size.x > 0f && sprite.bounds.size.y > 0f)
        {
            Vector2 sSize = sprite.bounds.size;
            float flapW = size.x * 0.5f;
            float flapLen = size.z - 0.7f * w;
            float tapeW = Mathf.Min(tapeWidth, size.x * 0.35f);
            float framePad = Mathf.Max(0.008f, frameWidth * 0.55f);
            float frameT = Mathf.Max(0.006f, frameWidth * 0.35f);

            float xMin = w + 0.015f;                         // keep clear of the outer edge
            float xMax = flapW - tapeW * 0.5f - 0.01f;       // keep clear of the tape
            float regionW = Mathf.Max(0.03f, xMax - xMin);
            float maxW = Mathf.Max(0.02f, regionW - framePad * 2f);
            float maxD = Mathf.Max(0.02f, flapLen * 0.62f - framePad * 2f);
            float scale = Mathf.Min(maxW / sSize.x, maxD / sSize.y);
            float imgW = sSize.x * scale, imgD = sSize.y * scale;

            float cx = (xMin + xMax) * 0.5f;
            float topY = t * 0.5f;                           // flap top surface (flap-local)
            float barY = topY + frameT * 0.5f;

            var fl = new List<CombineInstance>();
            AddCube(fl, new Vector3(imgW + framePad * 2f, frameT, frameT), new Vector3(cx, barY, imgD * 0.5f + framePad));
            AddCube(fl, new Vector3(imgW + framePad * 2f, frameT, frameT), new Vector3(cx, barY, -imgD * 0.5f - framePad));
            AddCube(fl, new Vector3(frameT, frameT, imgD + framePad * 2f), new Vector3(cx - imgW * 0.5f - framePad, barY, 0f));
            AddCube(fl, new Vector3(frameT, frameT, imgD + framePad * 2f), new Vector3(cx + imgW * 0.5f + framePad, barY, 0f));
            Mesh fm = new Mesh { name = "FlapImageFrame" };
            fm.CombineMeshes(fl.ToArray(), true, true);
            fm.hideFlags = HideFlags.DontUnloadUnusedAsset;

            set.hasSprite = true;
            set.sprite = sprite;
            set.flapFrame = fm;
            set.flapImageScale = scale;
            set.flapImagePos = new Vector3(cx, topY + 0.0015f, 0f);
        }

        // Merge everything into one mesh with one submesh per used material slot.
        var usedSlots = new List<int>();
        var finals = new List<CombineInstance>();
        var temps = new List<Mesh>();
        for (int s = 0; s < slotLists.Length; s++)
        {
            if (slotLists[s].Count == 0) continue;
            Mesh m = new Mesh();
            m.CombineMeshes(slotLists[s].ToArray(), true, true);
            temps.Add(m);
            finals.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
            usedSlots.Add(s);
        }

        Mesh body = new Mesh { name = "CartonBody" };
        body.CombineMeshes(finals.ToArray(), false, false);
        body.hideFlags = HideFlags.DontUnloadUnusedAsset;
        foreach (Mesh m in temps) SafeDestroy(m);

        set.body = body;
        set.slotIds = usedSlots.ToArray();
        return set;
    }

    // ------------------------------------------------------------------ flaps
    private Transform CreateFlap(string flapName, Vector3 pivotPosition, float flapWidth, float flapLength, float direction, Material mat)
    {
        Transform pivot = new GameObject(flapName).transform;
        pivot.SetParent(visualRoot, false);
        pivot.localPosition = pivotPosition;

        CreateCube("CardboardPanel", pivot, mat,
            new Vector3(flapWidth, wallThickness, flapLength),
            new Vector3(direction * flapWidth * 0.5f, 0f, 0f));
        return pivot;
    }

    private void AnimateFlaps(bool open)
    {
        if (flapAnimation != null) { StopCoroutine(flapAnimation); flapAnimation = null; }
        if (!isActiveAndEnabled) { ApplyFlapPose(open); return; }
        flapAnimation = StartCoroutine(FlapTween(open));
    }

    private void ApplyFlapPose(bool open)
    {
        float angle = open ? flapOpenAngle : 0f;
        if (leftFlap != null) leftFlap.localRotation = Quaternion.Euler(0f, 0f, angle);
        if (rightFlap != null) rightFlap.localRotation = Quaternion.Euler(0f, 0f, -angle);
    }

    private IEnumerator FlapTween(bool open)
    {
        Quaternion ls = leftFlap != null ? leftFlap.localRotation : Quaternion.identity;
        Quaternion rs = rightFlap != null ? rightFlap.localRotation : Quaternion.identity;
        float angle = open ? flapOpenAngle : 0f;
        Quaternion le = Quaternion.Euler(0f, 0f, angle);
        Quaternion re = Quaternion.Euler(0f, 0f, -angle);

        float elapsed = 0f, duration = Mathf.Max(0.01f, flapOpenDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            if (leftFlap != null) leftFlap.localRotation = Quaternion.Slerp(ls, le, k);
            if (rightFlap != null) rightFlap.localRotation = Quaternion.Slerp(rs, re, k);
            yield return null;
        }
        ApplyFlapPose(open);
        flapAnimation = null;
    }

    // ------------------------------------------------------------------ helpers: objects, meshes, materials
    private static Mesh GetCubeMesh()
    {
        if (cubeMesh != null) return cubeMesh;
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        SafeDestroy(temp);
        return cubeMesh;
    }

    private static void AddCube(List<CombineInstance> list, Vector3 size, Vector3 position)
    {
        list.Add(new CombineInstance
        {
            mesh = GetCubeMesh(),
            transform = Matrix4x4.TRS(position, Quaternion.identity, size)
        });
    }

    private static GameObject CreateCube(string objectName, Transform parent, Material mat, Vector3 size, Vector3 localPosition)
    {
        GameObject go = CreateMeshObject(objectName, parent, GetCubeMesh(), new[] { mat });
        go.transform.localPosition = localPosition;
        go.transform.localScale = size;
        return go;
    }

    private static GameObject CreateMeshObject(string objectName, Transform parent, Mesh mesh, Material[] mats)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        return go;
    }

    private Shader ResolveCartonShader()
    {
        if (cartonShader != null) return cartonShader;
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Standard");
        return s;
    }

    private static Material GetColorMaterial(Shader shader, Color color)
    {
        Color32 c = color;
        string key = $"{shader.name}|c|{c.r},{c.g},{c.b},{c.a}";
        if (materialCache.TryGetValue(key, out Material cached) && cached != null) return cached;

        Material m = new Material(shader) { name = "Carton_" + key, enableInstancing = true };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
        m.hideFlags = HideFlags.DontUnloadUnusedAsset;
        materialCache[key] = m;
        return m;
    }

    private static void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }

    private void UpdateMass()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null) body.mass = Mathf.Max(0.2f, cartonMass + remainingCount * unitMass);
    }

    private void ClearGeneratedVisuals()
    {
        if (flapAnimation != null) { StopCoroutine(flapAnimation); flapAnimation = null; }

        if (visualRoot != null)
        {
            // Detach first so the old visuals don't linger for a frame next to the new ones.
            visualRoot.gameObject.SetActive(false);
            visualRoot.SetParent(null);
            SafeDestroy(visualRoot.gameObject);
        }
        visualRoot = null;
        leftFlap = null;
        rightFlap = null;
        tapeObject = null;
        slots.Clear();
        displaysBuilt = false;
        // Meshes and materials are shared through static caches: never destroyed per carton.
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        customCount = Mathf.Max(1, customCount);
        productFootprint = Mathf.Max(0.005f, productFootprint);
        productHeight = Mathf.Max(0.005f, productHeight);
        wallThickness = Mathf.Max(0.005f, wallThickness);
        productSpacing = Mathf.Max(0.001f, productSpacing);
        padding = Mathf.Max(0f, padding);
    }
#endif
}