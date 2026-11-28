using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CheckoutManager : MonoBehaviour
{
    public static CheckoutManager Instance { get; private set; }

    [Header("References")]
    public PlayerMovement playerMovement;
    public ProductInteraction productInteraction;
    public MobileJoystick mobileJoystick;
    public TouchLook touchLook;
    public ComputerInteraction computerInteraction;

    [Header("Checkout Position & UI")]
    public Transform cashierStandPoint;
    public GameObject exitButton;

    [Header("Sales")]
    [Min(0.5f)] public float scanDistance = 4f;

    private bool active;
    public bool IsActive => active;
    private bool interactionWasEnabled, joystickWasEnabled, touchLookWasEnabled;
    private CharacterController characterController;
    private Button exitUnityButton;
    private Camera playerCamera;

    private readonly Dictionary<ProductItem, ScannedProduct> scannedProducts = new Dictionary<ProductItem, ScannedProduct>();
    private GameObject salesCanvas;
    private GameObject salesPanel;
    private TMP_Text headerText;
    private TMP_Text itemsText;
    private TMP_Text totalText;
    private TMP_Text statusText;

    private sealed class ScannedProduct
    {
        public ProductItem item;
        public float price;
    }

    private static readonly RaycastHit[] scanHitBuffer = new RaycastHit[48];

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        ResolveReferences();

        if (!cashierStandPoint)
        {
            GameObject point = GameObject.Find("CheckoutPoint");
            if (point) cashierStandPoint = point.transform;
        }
        if (!exitButton)
        {
            GameObject buttonObject = GameObject.Find("ExitCheckoutButton");
            if (!buttonObject) buttonObject = GameObject.Find("CheckoutExitButton");
            if (buttonObject) exitButton = buttonObject;
        }

        if (playerMovement) characterController = playerMovement.GetComponent<CharacterController>();
        if (exitButton)
        {
            exitUnityButton = exitButton.GetComponent<Button>();
            if (!exitUnityButton) exitUnityButton = exitButton.GetComponentInChildren<Button>(true);
            if (exitUnityButton)
            {
                exitUnityButton.onClick.RemoveListener(ExitCheckout);
                exitUnityButton.onClick.AddListener(ExitCheckout);
            }
            exitButton.SetActive(false);
        }

        BuildSalesUI();
        RefreshSalesUI();
    }

    private void ResolveReferences()
    {
        if (!playerMovement) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (!productInteraction) productInteraction = FindFirstObjectByType<ProductInteraction>();
        if (!mobileJoystick) mobileJoystick = FindFirstObjectByType<MobileJoystick>();
        if (!touchLook) touchLook = FindFirstObjectByType<TouchLook>();
        if (!computerInteraction) computerInteraction = FindFirstObjectByType<ComputerInteraction>();

        if (playerCamera == null && playerMovement != null && playerMovement.playerCamera != null)
            playerCamera = playerMovement.playerCamera.GetComponent<Camera>();
        if (playerCamera == null && productInteraction != null)
            playerCamera = productInteraction.playerCamera;
        if (playerCamera == null) playerCamera = Camera.main;
    }

    void Update()
    {
        if (active && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            ExitCheckout();
    }

    public void EnterCheckout()
    {
        if (active) return;
        ResolveReferences();
        if (!playerMovement) { Debug.LogError("[CheckoutManager] PlayerMovement not found."); return; }
        if (!characterController) characterController = playerMovement.GetComponent<CharacterController>();

        interactionWasEnabled = productInteraction != null && productInteraction.enabled;
        joystickWasEnabled = mobileJoystick != null && mobileJoystick.enabled;
        touchLookWasEnabled = touchLook != null && touchLook.enabled;

        active = true;
        playerMovement.MovementLocked = true;

        if (productInteraction) productInteraction.enabled = false;
        if (mobileJoystick)
        {
            mobileJoystick.ResetInput();
            mobileJoystick.enabled = false;
        }
        if (touchLook) touchLook.enabled = touchLookWasEnabled;

        if (cashierStandPoint)
        {
            if (characterController) characterController.enabled = false;
            playerMovement.transform.SetPositionAndRotation(cashierStandPoint.position, cashierStandPoint.rotation);
            if (characterController) characterController.enabled = true;
        }
        else Debug.LogWarning("[CheckoutManager] Assign CheckoutPoint to Cashier Stand Point.");

        if (exitButton) exitButton.SetActive(true);
        if (exitUnityButton) exitUnityButton.interactable = true;
        if (salesCanvas) salesCanvas.SetActive(true);
        RefreshSalesUI();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("[CheckoutManager] Checkout entered.");
    }

    public void ExitCheckout()
    {
        if (!active) return;
        active = false;

        if (playerMovement) playerMovement.MovementLocked = false;
        if (productInteraction) productInteraction.enabled = interactionWasEnabled;
        if (mobileJoystick)
        {
            mobileJoystick.enabled = joystickWasEnabled;
            mobileJoystick.ResetInput();
        }
        if (touchLook) touchLook.enabled = touchLookWasEnabled;
        if (exitButton) exitButton.SetActive(false);
        if (salesCanvas) salesCanvas.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("[CheckoutManager] Checkout exited.");
    }

    /// <summary>Called by the existing Interact Button while checkout mode is active.</summary>
    public bool TryScanAimedProduct()
    {
        if (!active) return false;
        ResolveReferences();
        if (playerCamera == null)
        {
            SetStatus("Camera not found.");
            return false;
        }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        int count = Physics.RaycastNonAlloc(ray, scanHitBuffer, scanDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        ProductItem product = null;
        float nearestProductDistance = float.MaxValue;

        // The ray can hit the counter, shelf, or a trigger before reaching the item.
        // Select the nearest actual ProductItem instead of rejecting the whole ray
        // because a non-product collider happened to be closer.
        for (int i = 0; i < count; i++)
        {
            Collider col = scanHitBuffer[i].collider;
            if (!col) continue;
            if (playerMovement != null && col.transform.root == playerMovement.transform.root) continue;

            ProductItem candidate = col.GetComponentInParent<ProductItem>();
            if (candidate == null || candidate.Data == null) continue;

            if (scanHitBuffer[i].distance < nearestProductDistance)
            {
                nearestProductDistance = scanHitBuffer[i].distance;
                product = candidate;
            }
        }

        for (int i = 0; i < count; i++) scanHitBuffer[i] = default;

        if (product == null || product.Data == null)
        {
            SetStatus("Aim at a product, then tap INTERACT to scan.");
            return false;
        }

        if (scannedProducts.ContainsKey(product))
        {
            SetStatus("This product is already scanned.");
            return false;
        }

        float price = Mathf.Max(0f, product.MarketPrice);
        scannedProducts.Add(product, new ScannedProduct { item = product, price = price });
        SetStatus("Scanned: " + product.ProductName);
        RefreshSalesUI();
        return true;
    }

    public void ClearScannedProducts()
    {
        if (!active) return;
        scannedProducts.Clear();
        SetStatus("Cart cleared. Aim at a product to scan.");
        RefreshSalesUI();
    }

    public void CompleteSale()
    {
        if (!active || scannedProducts.Count == 0)
        {
            SetStatus("Scan at least one product before payment.");
            return;
        }

        MoneyManager money = MoneyManager.Instance;
        if (money == null)
        {
            SetStatus("MoneyManager is missing. Sale was not completed.");
            Debug.LogError("[CheckoutManager] MoneyManager.Instance not found; sale cancelled.");
            return;
        }

        float originalTotal = GetCartTotal();
        float finalTotal = money.ApplyCustomerDiscount(originalTotal);
        int soldCount = 0;

        foreach (ScannedProduct entry in scannedProducts.Values)
        {
            ProductItem item = entry.item;
            if (item == null) continue;

            ShelfZone zone = item.GetComponentInParent<ShelfZone>();
            if (zone != null) zone.UnregisterProduct(item.transform);

            Destroy(item.gameObject);
            soldCount++;
        }

        if (soldCount <= 0)
        {
            scannedProducts.Clear();
            SetStatus("Scanned products are no longer available.");
            RefreshSalesUI();
            return;
        }

        money.AddRevenue(finalTotal, "Checkout sale (" + soldCount + " items)", "Sale");
        if (finalTotal < originalTotal)
            money.GiveDiscount(originalTotal, finalTotal, "Customer discount");

        scannedProducts.Clear();
        SetStatus("Payment successful. Received " + finalTotal.ToString("0.00") + ".");
        RefreshSalesUI();
        Debug.Log("[CheckoutManager] Sale completed. Items: " + soldCount + ", total: " + finalTotal);
    }

    private float GetCartTotal()
    {
        float total = 0f;
        foreach (ScannedProduct entry in scannedProducts.Values)
            if (entry.item != null) total += entry.price;
        return total;
    }

    private void RefreshSalesUI()
    {
        if (headerText == null) return;

        int validCount = 0;
        StringBuilder lines = new StringBuilder();
        foreach (ScannedProduct entry in scannedProducts.Values)
        {
            if (entry.item == null) continue;
            validCount++;
            lines.Append(entry.item.ProductName);
            lines.Append("    ");
            lines.Append(entry.price.ToString("0.00"));
            lines.AppendLine();
        }

        headerText.text = "CHECKOUT  |  " + validCount + " ITEM(S)";
        itemsText.text = validCount > 0 ? lines.ToString() : "No products scanned yet.\nAim at a product and tap INTERACT.";
        totalText.text = "TOTAL: " + GetCartTotal().ToString("0.00");
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    private void BuildSalesUI()
    {
        if (salesCanvas != null) return;

        ResolveReferences();
        Transform monitorSurface = computerInteraction != null ? computerInteraction.screenSurface : null;
        if (monitorSurface == null)
        {
            Debug.LogError("[CheckoutManager] Computer monitor Screen Surface is not assigned. Checkout screen cannot be displayed.");
            return;
        }

        salesCanvas = new GameObject("CheckoutSalesCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        Canvas canvas = salesCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = playerCamera != null ? playerCamera : Camera.main;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 60;
        GraphicRaycaster raycaster = salesCanvas.GetComponent<GraphicRaycaster>();
        raycaster.ignoreReversedGraphics = false;
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        RectTransform canvasRect = salesCanvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(340f, 225f);
        canvasRect.localScale = Vector3.one * (0.65f / 340f);
        canvasRect.SetParent(monitorSurface, false);
        canvasRect.localPosition = Vector3.forward * 0.014f;
        canvasRect.localRotation = Quaternion.Euler(0f, 180f, 0f);

        salesPanel = CreateUIObject("SalesPanel", salesCanvas.transform, typeof(Image));
        RectTransform panelRect = salesPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(340f, 225f);
        Image panelImage = salesPanel.GetComponent<Image>();
        panelImage.color = new Color(0.035f, 0.055f, 0.075f, 0.94f);

        headerText = CreateText("Header", salesPanel.transform, "CHECKOUT", 18, FontStyles.Bold,
            new Vector2(14f, -10f), new Vector2(312f, 25f), TextAlignmentOptions.Left);
        headerText.color = new Color(0.35f, 0.95f, 0.68f);

        itemsText = CreateText("Items", salesPanel.transform, "No products scanned yet.", 13, FontStyles.Normal,
            new Vector2(14f, -42f), new Vector2(312f, 105f), TextAlignmentOptions.TopLeft);
        itemsText.color = Color.white;

        totalText = CreateText("Total", salesPanel.transform, "TOTAL: 0.00", 17, FontStyles.Bold,
            new Vector2(14f, -151f), new Vector2(312f, 25f), TextAlignmentOptions.Left);
        totalText.color = new Color(1f, 0.82f, 0.35f);

        statusText = CreateText("Status", salesPanel.transform, "Tap INTERACT while aiming at a product to scan.", 10, FontStyles.Normal,
            new Vector2(14f, -177f), new Vector2(312f, 18f), TextAlignmentOptions.Left);
        statusText.color = new Color(0.72f, 0.79f, 0.85f);

        CreateActionButton("PayButton", salesPanel.transform, "PAY NOW",
            new Vector2(14f, -198f), new Vector2(150f, 28f),
            new Color(0.08f, 0.52f, 0.34f), CompleteSale);
        CreateActionButton("ClearButton", salesPanel.transform, "CLEAR",
            new Vector2(176f, -198f), new Vector2(150f, 28f),
            new Color(0.25f, 0.30f, 0.36f), ClearScannedProducts);

        salesCanvas.SetActive(false);
    }

    private static GameObject CreateUIObject(string objectName, Transform parent, params System.Type[] components)
    {
        GameObject go = new GameObject(objectName, components);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TMP_Text CreateText(string objectName, Transform parent, string value, int fontSize,
        FontStyles style, Vector2 position, Vector2 size, TextAlignmentOptions alignment)
    {
        GameObject go = CreateUIObject(objectName, parent, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void CreateActionButton(string objectName, Transform parent, string label,
        Vector2 position, Vector2 size, Color color, UnityEngine.Events.UnityAction action)
    {
        GameObject go = CreateUIObject(objectName, parent, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        image.color = color;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        TMP_Text text = CreateText("Label", go.transform, label, 12, FontStyles.Bold,
            new Vector2(0f, 0f), size, TextAlignmentOptions.Center);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}