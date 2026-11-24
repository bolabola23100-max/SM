using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime shop shown while the player is focused on the desktop computer.
/// Each purchase deducts money and spawns real ProductBox cartons at DeliveryPoint.
/// </summary>
public class ComputerShopUI : MonoBehaviour
{
    [Header("Shop Data")]
    [SerializeField] private ProductDatabase productDatabase;
    [SerializeField] private ProductBox productBoxPrefab;
    [SerializeField] private Transform deliveryPoint;

    [Header("Economy")]
    [Min(0f)][SerializeField] private float startingBalance = 1000f;

    [Header("UI")]
    [SerializeField] private ComputerInteraction computerInteraction;
    [Tooltip("Assign the flat front surface of the monitor. If empty, ComputerInteraction.Screen Surface is used.")]
    [SerializeField] private Transform screenSurface;
    [Min(0.1f)][SerializeField] private float screenWorldWidth = 0.85f;
    [SerializeField] private float surfaceOffset = 0.012f;
    [SerializeField] private Vector2 panelSize = new Vector2(900f, 620f);

    private readonly Dictionary<ProductData, int> quantities = new Dictionary<ProductData, int>();
    private readonly Dictionary<ProductData, Text> quantityLabels = new Dictionary<ProductData, Text>();
    private Canvas canvas;
    private Text balanceLabel;
    private Text statusLabel;
    private float balance;
    private bool wasFocused;

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
        canvas.gameObject.SetActive(focused);
        if (focused)
            RefreshBalance();
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
            canvasRect.SetParent(monitorSurface, false);
            canvasRect.localPosition = Vector3.forward * surfaceOffset;
            canvasRect.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogError("ComputerShopUI: Assign Screen Surface to the flat front of the computer monitor.");
            canvasObject.SetActive(false);
        }

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        Image background = CreatePanel(canvasObject.transform, "ShopBackground",
            new Vector2(0.5f, 0.5f), Vector2.zero, panelSize,
            new Color(0.035f, 0.055f, 0.075f, 0.97f));

        RectTransform root = background.rectTransform;

        Text title = CreateText(root, "STORE  /  SUPPLY ORDER", 30, FontStyle.Bold,
            new Vector2(0.04f, 0.91f), new Vector2(0.62f, 0.99f),
            TextAnchor.MiddleLeft, Color.white);

        balanceLabel = CreateText(root, "", 23, FontStyle.Bold,
            new Vector2(0.66f, 0.91f), new Vector2(0.96f, 0.99f),
            TextAnchor.MiddleRight, new Color(0.35f, 0.9f, 0.72f));

        CreateText(root, "اختار المنتج وعدد الكراتين، وبعد الشراء هتظهر الكراتين عند نقطة التسليم.",
            16, FontStyle.Normal, new Vector2(0.04f, 0.84f), new Vector2(0.96f, 0.91f),
            TextAnchor.MiddleLeft, new Color(0.68f, 0.75f, 0.81f));

        GameObject scrollObject = new GameObject("ProductsScroll",
            typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
        scrollRect.SetParent(root, false);
        scrollRect.anchorMin = new Vector2(0.04f, 0.18f);
        scrollRect.anchorMax = new Vector2(0.96f, 0.82f);
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;
        scrollObject.GetComponent<Image>().color = new Color(0.07f, 0.095f, 0.12f, 1f);

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.SetParent(scrollRect, false);
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = Color.white;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.SetParent(viewportRect, false);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 10f;
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

        if (productDatabase != null)
        {
            foreach (ProductData data in productDatabase.Products)
            {
                if (data == null || data.productPrefab == null)
                    continue;

                quantities[data] = 1;
                CreateProductRow(contentRect, data);
            }
        }

        statusLabel = CreateText(root, "", 16, FontStyle.Normal,
            new Vector2(0.04f, 0.08f), new Vector2(0.72f, 0.16f),
            TextAnchor.MiddleLeft, new Color(0.95f, 0.78f, 0.35f));

        CreateButton(root, "إغلاق  |  ESC", new Vector2(0.76f, 0.055f),
            new Vector2(0.96f, 0.15f), new Color(0.18f, 0.25f, 0.31f),
            () => { if (computerInteraction != null) computerInteraction.TryInteract(); });

        RefreshBalance();
    }

    private void CreateProductRow(RectTransform parent, ProductData data)
    {
        Image row = CreatePanel(parent, "Product_" + data.productId,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            new Color(0.105f, 0.14f, 0.17f, 1f));
        LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
        rowLayout.minHeight = 92f;
        rowLayout.preferredHeight = 92f;

        RectTransform r = row.rectTransform;
        CreateText(r, data.productName + "   •   " + data.category,
            20, FontStyle.Bold, new Vector2(0.025f, 0.52f), new Vector2(0.48f, 0.95f),
            TextAnchor.MiddleLeft, Color.white);

        CreateText(r, "سعر الكرتونة: " + data.PackCost.ToString("0.##") +
            "   |   " + data.packSize + " قطعة",
            15, FontStyle.Normal, new Vector2(0.025f, 0.08f), new Vector2(0.48f, 0.5f),
            TextAnchor.MiddleLeft, new Color(0.68f, 0.77f, 0.82f));

        CreateButton(r, "−", new Vector2(0.56f, 0.2f), new Vector2(0.63f, 0.8f),
            new Color(0.2f, 0.29f, 0.35f), () => ChangeQuantity(data, -1));

        Text quantity = CreateText(r, "1", 20, FontStyle.Bold,
            new Vector2(0.635f, 0.2f), new Vector2(0.70f, 0.8f),
            TextAnchor.MiddleCenter, Color.white);
        quantityLabels[data] = quantity;

        CreateButton(r, "+", new Vector2(0.705f, 0.2f), new Vector2(0.775f, 0.8f),
            new Color(0.2f, 0.29f, 0.35f), () => ChangeQuantity(data, 1));

        CreateButton(r, "شراء", new Vector2(0.81f, 0.17f), new Vector2(0.97f, 0.83f),
            new Color(0.05f, 0.55f, 0.42f), () => Buy(data));
    }

    private void ChangeQuantity(ProductData data, int delta)
    {
        if (!quantities.ContainsKey(data))
            return;

        quantities[data] = Mathf.Clamp(quantities[data] + delta, 1, 99);
        quantityLabels[data].text = quantities[data].ToString();
        if (statusLabel != null)
            statusLabel.text = "الإجمالي: " + (data.PackCost * quantities[data]).ToString("0.##");
    }

    private void Buy(ProductData data)
    {
        if (data == null || productBoxPrefab == null || deliveryPoint == null)
        {
            SetStatus("اربط Product Box Prefab و Delivery Point في Inspector.");
            return;
        }

        int count = quantities.TryGetValue(data, out int selected) ? selected : 1;
        float total = data.PackCost * count;

        if (balance < total)
        {
            SetStatus("رصيدك مش كافي للطلب ده.");
            return;
        }

        balance -= total;
        for (int i = 0; i < count; i++)
        {
            ProductBox carton = Instantiate(productBoxPrefab, deliveryPoint.position, deliveryPoint.rotation);
            carton.SetProduct(data, data.packSize);

            BoxCollider boxCollider = carton.GetComponent<BoxCollider>();
            float width = boxCollider != null ? boxCollider.size.x * carton.transform.lossyScale.x : 0.55f;
            int column = i % 3;
            int row = i / 3;
            Vector3 offset = deliveryPoint.right * (column * (width + 0.12f))
                + deliveryPoint.forward * (row * (width + 0.12f));
            carton.transform.position += offset;
        }

        RefreshBalance();
        SetStatus("تم الشراء! " + count + " كرتونة من " + data.productName +
            " وصلت لنقطة التسليم.");
    }

    private void RefreshBalance()
    {
        if (balanceLabel != null)
            balanceLabel.text = "الرصيد: " + balance.ToString("0.##");
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null)
            statusLabel.text = message;
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
        Vector2 anchorMax, Color color, UnityEngine.Events.UnityAction action)
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
        image.color = color;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        Text text = CreateText(rect, label, 18, FontStyle.Bold,
            Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, Color.white);
        text.raycastTarget = false;
        return button;
    }
}
