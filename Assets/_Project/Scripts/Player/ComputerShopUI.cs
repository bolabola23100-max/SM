using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// World-space supply shop displayed on the computer monitor.
/// Players add cartons to a cart, review the cart, then place an order.
/// Successful orders spawn real ProductBox objects at the delivery point.
/// </summary>
public class ComputerShopUI : MonoBehaviour
{
    [Header("Shop Data")]
    [SerializeField] private ProductDatabase productDatabase;
    [SerializeField] private ProductBox productBoxPrefab;
    [SerializeField] private Transform deliveryPoint;

    [Header("Money")]
    [Min(0f)][SerializeField] private float startingBalance = 1000f;

    [Header("Monitor UI")]
    [SerializeField] private ComputerInteraction computerInteraction;
    [Tooltip("Assign the flat front surface of the monitor. If empty, ComputerInteraction.Screen Surface is used.")]
    [SerializeField] private Transform screenSurface;
    [Min(0.1f)][SerializeField] private float screenWorldWidth = 1.5f;
    [SerializeField] private float surfaceOffset = 0.045f;
    [SerializeField] private Vector2 panelSize = new Vector2(1200f, 800f);

    [Header("Click Fixes")]
    [Tooltip("Objects to hide while the computer is focused (e.g. mobile joystick/look-pad canvases " +
             "with a full-screen Raycast Target image that steal the clicks).")]
    [SerializeField] private GameObject[] hideWhileFocused;
    [Tooltip("Unlock and show the cursor while focused. Turn on only if ComputerInteraction does not already do it.")]
    [SerializeField] private bool unlockCursorWhileFocused = false;
    [Tooltip("Higher = a finger can wobble more before a tap turns into a drag (touch screens).")]
    [SerializeField] private int dragThreshold = 40;
    [Tooltip("Logs what is under the pointer on every click. Turn off when everything works.")]
    [SerializeField] private bool debugPointer = false;

    [Header("Delivery Layout")]
    [Tooltip("How many cartons per row at the delivery point.")]
    [SerializeField] private int cartonsPerRow = 3;
    [SerializeField] private float cartonSpacingX = 0.8f;
    [SerializeField] private float cartonSpacingZ = 0.8f;
    [Tooltip("Small lift so cartons never spawn inside the floor.")]
    [SerializeField] private float spawnLift = 0.15f;

    private sealed class OrderEntry
    {
        public string productName;
        public int quantity;
        public float total;
        public string time;
    }

    private readonly Dictionary<ProductData, int> selectedQuantities = new Dictionary<ProductData, int>();
    private readonly Dictionary<ProductData, int> cart = new Dictionary<ProductData, int>();
    private readonly List<OrderEntry> orderHistory = new List<OrderEntry>();
    private readonly Dictionary<ProductData, Text> selectedQuantityLabels = new Dictionary<ProductData, Text>();
    private readonly List<RaycastResult> debugHits = new List<RaycastResult>();

    private Canvas canvas;
    private GraphicRaycaster raycaster;
    private RectTransform pageRoot;
    private Text balanceLabel;
    private Text cartCountLabel;
    private Text statusLabel;
    private RectTransform contentArea;
    private float balance;
    private bool wasFocused;
    private bool hasMonitorSurface;
    private Transform monitorSurfaceTransform;
    private string currentPage = "Store";

    private CursorLockMode previousLockState;
    private bool previousCursorVisible;
    private int previousDragThreshold;

    private readonly Color ink = new Color(0.08f, 0.11f, 0.14f);
    private readonly Color muted = new Color(0.32f, 0.37f, 0.42f);
    private readonly Color green = new Color(0.04f, 0.52f, 0.38f);
    private readonly Color pale = new Color(0.93f, 0.95f, 0.97f);

    private void Awake()
    {
        balance = startingBalance;
        EnsureEventSystem();
        BuildInterface();
        if (canvas != null)
            canvas.gameObject.SetActive(false);
    }

    private void Update()
    {
        bool focused = computerInteraction != null && computerInteraction.IsFocused;

        if (focused != wasFocused)
        {
            wasFocused = focused;
            if (focused)
                OnFocusEnter();
            else
                OnFocusExit();
        }

        if (focused && debugPointer)
            DebugPointerClick();
    }

    private void OnDisable()
    {
        // Make sure we never leave the game with hidden controls or a stuck cursor.
        if (wasFocused)
        {
            wasFocused = false;
            OnFocusExit();
        }
    }

    private void OnFocusEnter()
    {
        if (canvas == null || !hasMonitorSurface)
            return;

        // 1) Always give the world-space canvas a valid event camera.
        //    A null/wrong camera = raycasts never hit anything = dead buttons.
        Camera cam = computerInteraction.playerCamera != null
            ? computerInteraction.playerCamera
            : Camera.main;
        canvas.worldCamera = cam;

        // 2) Do not ignore graphics that face away from the camera
        //    (the canvas is rotated 180 degrees on the monitor).
        if (raycaster != null)
            raycaster.ignoreReversedGraphics = false;

        // 3) Make taps tolerant to small finger movement inside the ScrollRect.
        if (EventSystem.current != null)
        {
            previousDragThreshold = EventSystem.current.pixelDragThreshold;
            EventSystem.current.pixelDragThreshold = dragThreshold;
        }

        // 4) Hide overlay UI that sits on top of world-space canvases and eats clicks.
        if (hideWhileFocused != null)
        {
            foreach (GameObject go in hideWhileFocused)
                if (go != null)
                    go.SetActive(false);
        }

        // 5) Optional: free the cursor so the pointer position can move.
        if (unlockCursorWhileFocused)
        {
            previousLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        canvas.gameObject.SetActive(true);
        ShowPage(currentPage);
    }

    private void OnFocusExit()
    {
        if (canvas != null)
            canvas.gameObject.SetActive(false);

        if (EventSystem.current != null)
            EventSystem.current.pixelDragThreshold = previousDragThreshold;

        if (hideWhileFocused != null)
        {
            foreach (GameObject go in hideWhileFocused)
                if (go != null)
                    go.SetActive(true);
        }

        if (unlockCursorWhileFocused)
        {
            Cursor.lockState = previousLockState;
            Cursor.visible = previousCursorVisible;
        }
    }

    private void DebugPointerClick()
    {
        bool mouseDown = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool touchDown = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
        if (!mouseDown && !touchDown)
            return;

        Vector2 pos = touchDown
            ? Touchscreen.current.primaryTouch.position.ReadValue()
            : Mouse.current.position.ReadValue();

        PointerEventData data = new PointerEventData(EventSystem.current) { position = pos };
        debugHits.Clear();
        EventSystem.current.RaycastAll(data, debugHits);

        if (debugHits.Count == 0)
        {
            Debug.Log("[ComputerShopUI] Click at " + pos + " -> NO UI hit. " +
                "Check worldCamera, canvas direction, and cursor lock.");
        }
        else
        {
            Debug.Log("[ComputerShopUI] Click at " + pos + " -> first hit: " + debugHits[0].gameObject.name +
                " (total " + debugHits.Count + ")");
        }
    }

    private void EnsureEventSystem()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
            DontDestroyOnLoad(eventSystemObject);
        }

        // This project uses the New Input System (Active Input Handling = Input System).
        // StandaloneInputModule does not receive its input in that mode.
        BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
        foreach (BaseInputModule module in modules)
        {
            if (!(module is InputSystemUIInputModule))
                Destroy(module);
        }

        InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
            inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

        inputModule.AssignDefaultActions();
        eventSystem.enabled = true;
        inputModule.enabled = true;
    }

    private void BuildInterface()
    {
        Transform monitorSurface = screenSurface != null
            ? screenSurface
            : (computerInteraction != null ? computerInteraction.screenSurface : null);

        GameObject canvasObject = new GameObject("ComputerShopCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        canvas = canvasObject.GetComponent<Canvas>();
        raycaster = canvasObject.GetComponent<GraphicRaycaster>();
        raycaster.ignoreReversedGraphics = false;
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = computerInteraction != null && computerInteraction.playerCamera != null
            ? computerInteraction.playerCamera
            : Camera.main;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 50;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = panelSize;
        canvasRect.localScale = Vector3.one * (screenWorldWidth / Mathf.Max(1f, panelSize.x));

        if (monitorSurface != null)
        {
            hasMonitorSurface = true;
            monitorSurfaceTransform = monitorSurface;
            canvasRect.SetParent(monitorSurface, false);
            canvasRect.localPosition = Vector3.forward * surfaceOffset;
            canvasRect.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
        else
        {
            Debug.LogError("ComputerShopUI: Assign Screen Surface to the flat front of the computer monitor.");
            canvasObject.SetActive(false);
            return;
        }

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        Image background = CreatePanel(canvasObject.transform, "ShopBackground",
            new Vector2(0.5f, 0.5f), Vector2.zero, panelSize, new Color(0.03f, 0.32f, 0.78f, 1f));
        pageRoot = background.rectTransform;

        CreateText(pageRoot, "SUPERMARKET  /  SUPPLY STORE", 40, FontStyle.Bold,
            new Vector2(0.035f, 0.91f), new Vector2(0.60f, 0.99f),
            TextAnchor.MiddleLeft, ink);

        balanceLabel = CreateText(pageRoot, "", 30, FontStyle.Bold,
            new Vector2(0.66f, 0.91f), new Vector2(0.965f, 0.99f),
            TextAnchor.MiddleRight, green);

        CreateButton(pageRoot, "STORE", new Vector2(0.035f, 0.835f), new Vector2(0.19f, 0.90f),
            new Color(1f, 0.82f, 0.12f), new Color(0.04f, 0.10f, 0.20f), () => ShowPage("Store"));
        CreateButton(pageRoot, "CART", new Vector2(0.205f, 0.835f), new Vector2(0.36f, 0.90f),
            new Color(0.85f, 0.89f, 0.92f), ink, () => ShowPage("Cart"));
        CreateButton(pageRoot, "MY ORDERS", new Vector2(0.375f, 0.835f), new Vector2(0.53f, 0.90f),
            new Color(0.85f, 0.89f, 0.92f), ink, () => ShowPage("Orders"));

        cartCountLabel = CreateText(pageRoot, "", 24, FontStyle.Bold,
            new Vector2(0.55f, 0.835f), new Vector2(0.75f, 0.90f),
            TextAnchor.MiddleLeft, green);

        contentArea = CreatePanel(pageRoot, "PageContent",
            new Vector2(0.5f, 0.47f), Vector2.zero, new Vector2(panelSize.x * 0.93f, panelSize.y * 0.65f),
            new Color(0.78f, 0.89f, 1f, 1f)).rectTransform;

        statusLabel = CreateText(pageRoot, "", 22, FontStyle.Bold,
            new Vector2(0.035f, 0.035f), new Vector2(0.72f, 0.105f),
            TextAnchor.MiddleLeft, new Color(0.55f, 0.34f, 0.05f));

        CreateButton(pageRoot, "EXIT COMPUTER", new Vector2(0.76f, 0.035f), new Vector2(0.965f, 0.11f),
            new Color(0.18f, 0.25f, 0.31f), Color.white,
            () => { if (computerInteraction != null) computerInteraction.ExitComputer(); });

        RefreshHeader();
        ShowPage("Store");
    }

    private void ShowPage(string page)
    {
        currentPage = page;
        ClearContent();
        SetStatus(string.Empty);
        RefreshHeader();

        if (page == "Cart")
            BuildCartPage();
        else if (page == "Orders")
            BuildOrdersPage();
        else
            BuildStorePage();
    }

    private void BuildStorePage()
    {
        CreateText(contentArea, "AVAILABLE PRODUCTS", 32, FontStyle.Bold,
            new Vector2(0.025f, 0.91f), new Vector2(0.70f, 0.99f), TextAnchor.MiddleLeft, ink);

        CreateScrollArea(contentArea, new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.89f),
            out RectTransform content);
        if (productDatabase == null || productDatabase.Products.Count == 0)
        {
            CreateText(content, "NO PRODUCTS FOUND IN PRODUCT DATABASE.", 28, FontStyle.Bold,
                new Vector2(0.05f, 0.7f), new Vector2(0.95f, 0.9f), TextAnchor.MiddleCenter, muted);
            return;
        }

        selectedQuantityLabels.Clear();

        foreach (ProductData data in productDatabase.Products)
        {
            if (data == null || data.productPrefab == null)
                continue;

            if (!selectedQuantities.ContainsKey(data))
                selectedQuantities[data] = 1;

            CreateStoreProductRow(content, data);
        }
    }

    private void CreateStoreProductRow(RectTransform parent, ProductData data)
    {
        Image row = CreatePanel(parent, "StoreProduct_" + data.productId,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

        // The row is only a background. It must never compete with its child
        // buttons for pointer/touch raycasts inside the ScrollRect viewport.
        row.raycastTarget = false;

        LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
        element.minHeight = 96f;
        element.preferredHeight = 96f;

        RectTransform r = row.rectTransform;
        CreateText(r, data.productName + "  |  " + data.category, 28, FontStyle.Bold,
            new Vector2(0.025f, 0.55f), new Vector2(0.52f, 0.94f), TextAnchor.MiddleLeft, ink);
        CreateText(r, "CARTON PRICE: " + data.PackCost.ToString("0.##") + "  •  " + data.packSize + " units",
            22, FontStyle.Normal, new Vector2(0.025f, 0.08f), new Vector2(0.52f, 0.50f),
            TextAnchor.MiddleLeft, muted);

        CreateButton(r, "−", new Vector2(0.56f, 0.20f), new Vector2(0.62f, 0.80f),
            new Color(0.86f, 0.89f, 0.92f), ink, () => ChangeSelectedQuantity(data, -1));
        Text quantity = CreateText(r, selectedQuantities[data].ToString(), 28, FontStyle.Bold,
            new Vector2(0.625f, 0.20f), new Vector2(0.69f, 0.80f),
            TextAnchor.MiddleCenter, ink);
        selectedQuantityLabels[data] = quantity;
        CreateButton(r, "+", new Vector2(0.695f, 0.20f), new Vector2(0.755f, 0.80f),
            new Color(0.86f, 0.89f, 0.92f), ink, () => ChangeSelectedQuantity(data, 1));

        CreateButton(r, "ADD TO CART", new Vector2(0.78f, 0.16f), new Vector2(0.975f, 0.84f),
            green, Color.white, () => AddToCart(data));
    }

    private void ChangeSelectedQuantity(ProductData data, int delta)
    {
        selectedQuantities[data] = Mathf.Clamp(selectedQuantities[data] + delta, 1, 99);
        if (selectedQuantityLabels.TryGetValue(data, out Text label) && label != null)
            label.text = selectedQuantities[data].ToString();
    }

    private void AddToCart(ProductData data)
    {
        int amount = selectedQuantities.TryGetValue(data, out int value) ? value : 1;
        cart[data] = (cart.TryGetValue(data, out int old) ? old : 0) + amount;
        RefreshHeader();
        SetStatus("ADDED " + amount + " CARTON(S) OF " + data.productName + " TO CART.");
    }

    private void BuildCartPage()
    {
        CreateText(contentArea, "SHOPPING CART", 32, FontStyle.Bold,
            new Vector2(0.025f, 0.91f), new Vector2(0.70f, 0.99f), TextAnchor.MiddleLeft, ink);

        CreateButton(contentArea, "VIEW MY ORDERS", new Vector2(0.76f, 0.91f), new Vector2(0.975f, 0.99f),
            new Color(1f, 0.82f, 0.12f), new Color(0.04f, 0.10f, 0.20f), () => ShowPage("Orders"));

        if (cart.Count == 0)
        {
            CreateText(contentArea, "YOUR CART IS EMPTY. GO TO STORE TO ADD PRODUCTS.",
                27, FontStyle.Bold, new Vector2(0.06f, 0.48f), new Vector2(0.94f, 0.72f),
                TextAnchor.MiddleCenter, muted);
            return;
        }

        CreateScrollArea(contentArea, new Vector2(0.02f, 0.20f), new Vector2(0.98f, 0.88f),
            out RectTransform content);

        foreach (KeyValuePair<ProductData, int> entry in cart)
        {
            if (entry.Key != null)
                CreateCartRow(content, entry.Key, entry.Value);
        }

        float total = CalculateCartTotal();
        CreateText(contentArea, "CART TOTAL: " + total.ToString("0.##"),
            20, FontStyle.Bold, new Vector2(0.03f, 0.08f), new Vector2(0.55f, 0.18f),
            TextAnchor.MiddleLeft, ink);
        CreateButton(contentArea, "PLACE ORDER", new Vector2(0.70f, 0.055f), new Vector2(0.975f, 0.18f),
            green, Color.white, PlaceOrder);
    }

    private void CreateCartRow(RectTransform parent, ProductData data, int amount)
    {
        Image row = CreatePanel(parent, "Cart_" + data.productId,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);
        row.raycastTarget = false;

        LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
        element.minHeight = 76f;
        element.preferredHeight = 76f;

        RectTransform r = row.rectTransform;
        CreateText(r, data.productName + "  ×  " + amount, 27, FontStyle.Bold,
            new Vector2(0.025f, 0.15f), new Vector2(0.55f, 0.85f), TextAnchor.MiddleLeft, ink);
        CreateText(r, "TOTAL: " + (data.PackCost * amount).ToString("0.##"),
            24, FontStyle.Normal, new Vector2(0.56f, 0.15f), new Vector2(0.80f, 0.85f),
            TextAnchor.MiddleCenter, muted);
        CreateButton(r, "−", new Vector2(0.83f, 0.18f), new Vector2(0.89f, 0.82f),
            new Color(0.86f, 0.89f, 0.92f), ink, () => ChangeCartQuantity(data, -1));
        CreateButton(r, "+", new Vector2(0.90f, 0.18f), new Vector2(0.96f, 0.82f),
            new Color(0.86f, 0.89f, 0.92f), ink, () => ChangeCartQuantity(data, 1));
    }

    private void ChangeCartQuantity(ProductData data, int delta)
    {
        if (!cart.TryGetValue(data, out int amount))
            return;

        amount += delta;
        if (amount <= 0)
            cart.Remove(data);
        else
            cart[data] = Mathf.Min(99, amount);

        ShowPage("Cart");
    }

    private void BuildOrdersPage()
    {
        CreateText(contentArea, "ORDER HISTORY", 32, FontStyle.Bold,
            new Vector2(0.025f, 0.91f), new Vector2(0.70f, 0.99f), TextAnchor.MiddleLeft, ink);

        CreateButton(contentArea, "BACK TO CART", new Vector2(0.76f, 0.91f), new Vector2(0.975f, 0.99f),
            new Color(0.84f, 0.89f, 0.93f), ink, () => ShowPage("Cart"));

        if (orderHistory.Count == 0)
        {
            CreateText(contentArea, "NO PURCHASED ORDERS YET.", 27, FontStyle.Bold,
                new Vector2(0.06f, 0.48f), new Vector2(0.94f, 0.72f),
                TextAnchor.MiddleCenter, muted);
            return;
        }

        CreateScrollArea(contentArea, new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.88f),
            out RectTransform content);

        for (int i = orderHistory.Count - 1; i >= 0; i--)
        {
            OrderEntry order = orderHistory[i];
            Image row = CreatePanel(content, "Order_" + i, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, Color.white);
            row.raycastTarget = false;

            LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 74f;
            element.preferredHeight = 74f;
            RectTransform r = row.rectTransform;

            CreateText(r, order.productName + "  ×  " + order.quantity, 27, FontStyle.Bold,
                new Vector2(0.025f, 0.15f), new Vector2(0.58f, 0.85f), TextAnchor.MiddleLeft, ink);
            CreateText(r, order.time, 21, FontStyle.Normal,
                new Vector2(0.59f, 0.15f), new Vector2(0.80f, 0.85f), TextAnchor.MiddleCenter, muted);
            CreateText(r, order.total.ToString("0.##"), 25, FontStyle.Bold,
                new Vector2(0.81f, 0.15f), new Vector2(0.97f, 0.85f), TextAnchor.MiddleCenter, green);
        }
    }

    private void PlaceOrder()
    {
        if (cart.Count == 0)
        {
            SetStatus("YOUR CART IS EMPTY.");
            return;
        }

        if (productBoxPrefab == null || deliveryPoint == null)
        {
            SetStatus("ASSIGN PRODUCT BOX PREFAB AND DELIVERY POINT IN THE INSPECTOR.");
            return;
        }

        float total = CalculateCartTotal();
        if (balance < total)
        {
            SetStatus("INSUFFICIENT FUNDS. REQUIRED: " + total.ToString("0.##") +
                " | AVAILABLE: " + balance.ToString("0.##") + ".");
            return;
        }

        // Validate every item before deducting money or spawning any cartons.
        foreach (KeyValuePair<ProductData, int> entry in cart)
        {
            if (entry.Key == null || entry.Key.productPrefab == null || entry.Value <= 0)
            {
                SetStatus("INVALID PRODUCT IN CART. REMOVE IT AND TRY AGAIN.");
                return;
            }
        }

        balance -= total;
        int cartonIndex = 0;
        int perRow = Mathf.Max(1, cartonsPerRow);

        foreach (KeyValuePair<ProductData, int> entry in cart)
        {
            ProductData data = entry.Key;
            int count = entry.Value;

            orderHistory.Add(new OrderEntry
            {
                productName = data.productName,
                quantity = count,
                total = data.PackCost * count,
                time = DateTime.Now.ToString("HH:mm")
            });

            for (int i = 0; i < count; i++)
            {
                int column = cartonIndex % perRow;
                int row = cartonIndex / perRow;

                // Fixed spacing: never depends on the carton's collider/scale,
                // so one stretched carton can not push the next one out of the map.
                Vector3 position = deliveryPoint.position
                    + deliveryPoint.right * (column * cartonSpacingX)
                    + deliveryPoint.forward * (row * cartonSpacingZ)
                    + Vector3.up * spawnLift;

                try
                {
                    ProductBox carton = Instantiate(productBoxPrefab, position, deliveryPoint.rotation);
                    Vector3 scaleBefore = carton.transform.localScale;
                    carton.SetProduct(data, data.packSize);

                    Debug.Log("[Order] " + data.productName + " #" + (i + 1) +
                        " | scale before: " + scaleBefore +
                        " | scale after: " + carton.transform.localScale +
                        " | pos: " + carton.transform.position);
                }
                catch (Exception e)
                {
                    // One bad carton must not stop the remaining ones from spawning.
                    Debug.LogError("[Order] Failed to spawn carton: " + e);
                }

                cartonIndex++;
            }
        }

        cart.Clear();
        RefreshHeader();
        ShowPage("Orders");
        SetStatus("ORDER CONFIRMED. CARTONS SENT TO THE DELIVERY POINT.");
    }

    private float CalculateCartTotal()
    {
        float total = 0f;
        foreach (KeyValuePair<ProductData, int> entry in cart)
            if (entry.Key != null)
                total += entry.Key.PackCost * entry.Value;
        return total;
    }

    private void RefreshHeader()
    {
        if (balanceLabel != null)
            balanceLabel.text = "BALANCE: " + balance.ToString("0.##");

        if (cartCountLabel != null)
        {
            int count = 0;
            foreach (int value in cart.Values)
                count += value;
            cartCountLabel.text = "CARTONS IN CART: " + count;
        }
    }

    private void ClearContent()
    {
        for (int i = contentArea.childCount - 1; i >= 0; i--)
        {
            GameObject child = contentArea.GetChild(i).gameObject;
            // Destroy() is delayed until the end of the frame. Deactivate and detach first so the
            // old buttons can never be hit by a raycast (or sit under the new ones) in the meantime.
            child.SetActive(false);
            child.transform.SetParent(null, false);
            Destroy(child);
        }
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null)
            statusLabel.text = message;
    }

    private static GameObject CreateScrollArea(RectTransform parent, Vector2 anchorMin,
        Vector2 anchorMax, out RectTransform contentRect)
    {
        GameObject scrollObject = new GameObject("ScrollArea",
            typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
        scrollRect.SetParent(parent, false);
        scrollRect.anchorMin = anchorMin;
        scrollRect.anchorMax = anchorMax;
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;
        scrollObject.GetComponent<Image>().color = new Color(0.88f, 0.91f, 0.94f, 1f);

        GameObject viewport = new GameObject("Viewport",
            typeof(RectTransform), typeof(Image), typeof(Mask));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.SetParent(scrollRect, false);
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = Color.white;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentRect = content.GetComponent<RectTransform>();
        contentRect.SetParent(viewportRect, false);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return scrollObject;
    }

    private static Image CreatePanel(Transform parent, string objectName, Vector2 anchor,
        Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style,
        Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment, Color color)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Text text = go.GetComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 anchorMin,
        Vector2 anchorMax, Color background, Color foreground, UnityEngine.Events.UnityAction action)
    {
        GameObject go = new GameObject("Button_" + label, typeof(RectTransform),
            typeof(Image), typeof(Button));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.color = background;
        image.raycastTarget = true;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.interactable = true;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        // Use Unity's standard Button click event. InputSystemUIInputModule
        // dispatches pointer clicks to this event; no EventTrigger or legacy
        // input polling is required.
        button.onClick.RemoveAllListeners();
        if (action != null)
            button.onClick.AddListener(action);

        Text text = CreateText(rect, label, 26, FontStyle.Bold,
            Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, foreground);
        text.raycastTarget = false;
        text.enabled = true;

        rect.SetAsLastSibling();
        return button;
    }
}