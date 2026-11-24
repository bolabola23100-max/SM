using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

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
    [Min(0.1f)][SerializeField] private float screenWorldWidth = 0.85f;
    [SerializeField] private float surfaceOffset = 0.012f;
    [SerializeField] private Vector2 panelSize = new Vector2(900f, 620f);

    private sealed class CartEntry
    {
        public ProductData data;
        public int quantity;
    }

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

    private Canvas canvas;
    private RectTransform pageRoot;
    private Text balanceLabel;
    private Text cartCountLabel;
    private Text statusLabel;
    private RectTransform contentArea;
    private float balance;
    private bool wasFocused;
    private bool hasMonitorSurface;
    private string currentPage = "Store";

    private readonly Color ink = new Color(0.08f, 0.11f, 0.14f);
    private readonly Color muted = new Color(0.32f, 0.37f, 0.42f);
    private readonly Color green = new Color(0.04f, 0.52f, 0.38f);
    private readonly Color pale = new Color(0.93f, 0.95f, 0.97f);

    private void Awake()
    {
        balance = startingBalance;
        BuildInterface();
        canvas.gameObject.SetActive(false);
    }

    private void Update()
    {
        bool focused = computerInteraction != null && computerInteraction.IsFocused;
        if (focused == wasFocused)
            return;

        wasFocused = focused;
        canvas.gameObject.SetActive(focused && hasMonitorSurface);
        if (focused && hasMonitorSurface)
            ShowPage(currentPage);
    }

    private void BuildInterface()
    {
        Transform monitorSurface = screenSurface != null
            ? screenSurface
            : (computerInteraction != null ? computerInteraction.screenSurface : null);

        GameObject canvasObject = new GameObject("ComputerShopCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = computerInteraction != null ? computerInteraction.playerCamera : null;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 50;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = panelSize;
        canvasRect.localScale = Vector3.one * (screenWorldWidth / Mathf.Max(1f, panelSize.x));

        if (monitorSurface != null)
        {
            hasMonitorSurface = true;
            canvasRect.SetParent(monitorSurface, false);
            canvasRect.localPosition = Vector3.forward * surfaceOffset;
            canvasRect.localRotation = Quaternion.identity;
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
            new Vector2(0.5f, 0.5f), Vector2.zero, panelSize, new Color(0.96f, 0.97f, 0.98f, 1f));
        pageRoot = background.rectTransform;

        CreateText(pageRoot, "SUPERMARKET  /  SUPPLY STORE", 28, FontStyle.Bold,
            new Vector2(0.035f, 0.91f), new Vector2(0.60f, 0.99f),
            TextAnchor.MiddleLeft, ink);

        balanceLabel = CreateText(pageRoot, "", 21, FontStyle.Bold,
            new Vector2(0.66f, 0.91f), new Vector2(0.965f, 0.99f),
            TextAnchor.MiddleRight, green);

        CreateButton(pageRoot, "المتجر", new Vector2(0.035f, 0.835f), new Vector2(0.19f, 0.90f),
            new Color(0.85f, 0.89f, 0.92f), ink, () => ShowPage("Store"));
        CreateButton(pageRoot, "السلة", new Vector2(0.205f, 0.835f), new Vector2(0.36f, 0.90f),
            new Color(0.85f, 0.89f, 0.92f), ink, () => ShowPage("Cart"));
        CreateButton(pageRoot, "طلباتي", new Vector2(0.375f, 0.835f), new Vector2(0.53f, 0.90f),
            new Color(0.85f, 0.89f, 0.92f), ink, () => ShowPage("Orders"));

        cartCountLabel = CreateText(pageRoot, "", 16, FontStyle.Bold,
            new Vector2(0.55f, 0.835f), new Vector2(0.75f, 0.90f),
            TextAnchor.MiddleLeft, green);

        contentArea = CreatePanel(pageRoot, "PageContent",
            new Vector2(0.5f, 0.47f), Vector2.zero, new Vector2(panelSize.x * 0.93f, panelSize.y * 0.65f),
            new Color(0.91f, 0.93f, 0.95f, 1f)).rectTransform;

        statusLabel = CreateText(pageRoot, "", 15, FontStyle.Bold,
            new Vector2(0.035f, 0.035f), new Vector2(0.72f, 0.105f),
            TextAnchor.MiddleLeft, new Color(0.55f, 0.34f, 0.05f));

        CreateButton(pageRoot, "إغلاق الكمبيوتر", new Vector2(0.76f, 0.035f), new Vector2(0.965f, 0.11f),
            new Color(0.18f, 0.25f, 0.31f), Color.white,
            () => { if (computerInteraction != null) computerInteraction.TryInteract(); });

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
        CreateText(contentArea, "المنتجات المتاحة", 22, FontStyle.Bold,
            new Vector2(0.025f, 0.91f), new Vector2(0.70f, 0.99f), TextAnchor.MiddleLeft, ink);

        GameObject scrollObject = CreateScrollArea(contentArea, new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.89f),
            out RectTransform content);
        if (productDatabase == null || productDatabase.Products.Count == 0)
        {
            CreateText(content, "مفيش منتجات في Product Database.", 20, FontStyle.Bold,
                new Vector2(0.05f, 0.7f), new Vector2(0.95f, 0.9f), TextAnchor.MiddleCenter, muted);
            return;
        }

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
        LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
        element.minHeight = 96f;
        element.preferredHeight = 96f;

        RectTransform r = row.rectTransform;
        CreateText(r, data.productName + "  |  " + data.category, 19, FontStyle.Bold,
            new Vector2(0.025f, 0.55f), new Vector2(0.52f, 0.94f), TextAnchor.MiddleLeft, ink);
        CreateText(r, "سعر الكرتونة: " + data.PackCost.ToString("0.##") + "  •  " + data.packSize + " قطعة",
            15, FontStyle.Normal, new Vector2(0.025f, 0.08f), new Vector2(0.52f, 0.50f),
            TextAnchor.MiddleLeft, muted);

        CreateButton(r, "−", new Vector2(0.56f, 0.20f), new Vector2(0.62f, 0.80f),
            new Color(0.86f, 0.89f, 0.92f), ink, () => ChangeSelectedQuantity(data, -1));
        Text quantity = CreateText(r, selectedQuantities[data].ToString(), 19, FontStyle.Bold,
            new Vector2(0.625f, 0.20f), new Vector2(0.69f, 0.80f),
            TextAnchor.MiddleCenter, ink);
        selectedQuantityLabels[data] = quantity;
        CreateButton(r, "+", new Vector2(0.695f, 0.20f), new Vector2(0.755f, 0.80f),
            new Color(0.86f, 0.89f, 0.92f), ink, () => ChangeSelectedQuantity(data, 1));

        CreateButton(r, "إضافة للسلة", new Vector2(0.78f, 0.16f), new Vector2(0.975f, 0.84f),
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
        SetStatus("اتضاف " + amount + " كرتونة من " + data.productName + " للسلة.");
    }

    private void BuildCartPage()
    {
        CreateText(contentArea, "سلة المشتريات", 22, FontStyle.Bold,
            new Vector2(0.025f, 0.91f), new Vector2(0.70f, 0.99f), TextAnchor.MiddleLeft, ink);

        CreateButton(contentArea, "عرض طلباتي", new Vector2(0.76f, 0.91f), new Vector2(0.975f, 0.99f),
            new Color(0.84f, 0.89f, 0.93f), ink, () => ShowPage("Orders"));

        if (cart.Count == 0)
        {
            CreateText(contentArea, "السلة فاضية. ارجع للمتجر واضف المنتجات اللي محتاجها.",
                19, FontStyle.Bold, new Vector2(0.06f, 0.48f), new Vector2(0.94f, 0.72f),
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
        CreateText(contentArea, "إجمالي السلة: " + total.ToString("0.##"),
            20, FontStyle.Bold, new Vector2(0.03f, 0.08f), new Vector2(0.55f, 0.18f),
            TextAnchor.MiddleLeft, ink);
        CreateButton(contentArea, "شراء الطلب", new Vector2(0.70f, 0.055f), new Vector2(0.975f, 0.18f),
            green, Color.white, PlaceOrder);
    }

    private void CreateCartRow(RectTransform parent, ProductData data, int amount)
    {
        Image row = CreatePanel(parent, "Cart_" + data.productId,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);
        LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
        element.minHeight = 76f;
        element.preferredHeight = 76f;

        RectTransform r = row.rectTransform;
        CreateText(r, data.productName + "  ×  " + amount, 18, FontStyle.Bold,
            new Vector2(0.025f, 0.15f), new Vector2(0.55f, 0.85f), TextAnchor.MiddleLeft, ink);
        CreateText(r, "الإجمالي: " + (data.PackCost * amount).ToString("0.##"),
            16, FontStyle.Normal, new Vector2(0.56f, 0.15f), new Vector2(0.80f, 0.85f),
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
        CreateText(contentArea, "طلباتي السابقة", 22, FontStyle.Bold,
            new Vector2(0.025f, 0.91f), new Vector2(0.70f, 0.99f), TextAnchor.MiddleLeft, ink);

        CreateButton(contentArea, "الرجوع للسلة", new Vector2(0.76f, 0.91f), new Vector2(0.975f, 0.99f),
            new Color(0.84f, 0.89f, 0.93f), ink, () => ShowPage("Cart"));

        if (orderHistory.Count == 0)
        {
            CreateText(contentArea, "لسه مفيش طلبات تم شراؤها.", 19, FontStyle.Bold,
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
            LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 74f;
            element.preferredHeight = 74f;
            RectTransform r = row.rectTransform;

            CreateText(r, order.productName + "  ×  " + order.quantity, 18, FontStyle.Bold,
                new Vector2(0.025f, 0.15f), new Vector2(0.58f, 0.85f), TextAnchor.MiddleLeft, ink);
            CreateText(r, order.time, 14, FontStyle.Normal,
                new Vector2(0.59f, 0.15f), new Vector2(0.80f, 0.85f), TextAnchor.MiddleCenter, muted);
            CreateText(r, order.total.ToString("0.##"), 17, FontStyle.Bold,
                new Vector2(0.81f, 0.15f), new Vector2(0.97f, 0.85f), TextAnchor.MiddleCenter, green);
        }
    }

    private void PlaceOrder()
    {
        if (cart.Count == 0)
        {
            SetStatus("السلة فاضية.");
            return;
        }

        if (productBoxPrefab == null || deliveryPoint == null)
        {
            SetStatus("اربط Product Box Prefab و Delivery Point في Inspector.");
            return;
        }

        float total = CalculateCartTotal();
        if (balance < total)
        {
            SetStatus("رصيدك مش كافي. محتاج " + total.ToString("0.##") +
                " ورصيدك " + balance.ToString("0.##") + ".");
            return;
        }

        // Validate every item before deducting money or spawning any cartons.
        foreach (KeyValuePair<ProductData, int> entry in cart)
        {
            if (entry.Key == null || entry.Key.productPrefab == null || entry.Value <= 0)
            {
                SetStatus("في منتج غير صالح في السلة. احذفه وحاول تاني.");
                return;
            }
        }

        balance -= total;
        int cartonIndex = 0;
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
                ProductBox carton = Instantiate(productBoxPrefab, deliveryPoint.position, deliveryPoint.rotation);
                carton.SetProduct(data, data.packSize);

                BoxCollider boxCollider = carton.GetComponent<BoxCollider>();
                Vector3 size = boxCollider != null ? Vector3.Scale(boxCollider.size, carton.transform.lossyScale) : Vector3.one * 0.5f;
                int column = cartonIndex % 3;
                int row = cartonIndex / 3;
                Vector3 offset = deliveryPoint.right * (column * (size.x + 0.12f))
                    + deliveryPoint.forward * (row * (size.z + 0.12f));
                carton.transform.position += offset;
                cartonIndex++;
            }
        }

        cart.Clear();
        RefreshHeader();
        SetStatus("تم تأكيد الطلب ووصول الكراتين لنقطة التسليم.");
        ShowPage("Orders");
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
            balanceLabel.text = "الرصيد: " + balance.ToString("0.##");

        if (cartCountLabel != null)
        {
            int count = 0;
            foreach (int value in cart.Values)
                count += value;
            cartCountLabel.text = "عدد الكراتين في السلة: " + count;
        }
    }

    private void ClearContent()
    {
        for (int i = contentArea.childCount - 1; i >= 0; i--)
            Destroy(contentArea.GetChild(i).gameObject);
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
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        Text text = CreateText(rect, label, 17, FontStyle.Bold,
            Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, foreground);
        text.raycastTarget = false;
        return button;
    }
}
