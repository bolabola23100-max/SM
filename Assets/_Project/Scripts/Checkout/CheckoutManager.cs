using UnityEngine;
using UnityEngine.UI;

/// <summary>Controls entering/leaving the manual checkout mode.</summary>
public class CheckoutManager : MonoBehaviour
{
    public static CheckoutManager Instance { get; private set; }
    [Header("UI")]
    public GameObject checkoutUI;
    public Button exitButton;
    [Header("Player (auto-found if empty)")]
    public PlayerMovement playerMovement;
    public ProductInteraction productInteraction;
    public MobileJoystick mobileJoystick;
    public TouchLook touchLook;
    private bool active;
    public bool IsActive => active;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (checkoutUI) checkoutUI.SetActive(false);
        if (exitButton) exitButton.onClick.AddListener(ExitCheckout);
    }
    void Start()
    {
        if (!playerMovement) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (!productInteraction) productInteraction = FindFirstObjectByType<ProductInteraction>();
        if (!mobileJoystick) mobileJoystick = FindFirstObjectByType<MobileJoystick>();
        if (!touchLook) touchLook = FindFirstObjectByType<TouchLook>();
    }
    public void EnterCheckout()
    {
        if (active) return;
        active = true;
        if (playerMovement) playerMovement.enabled = false;
        if (productInteraction) productInteraction.enabled = false;
        if (mobileJoystick) mobileJoystick.enabled = false;
        if (touchLook) touchLook.enabled = false;
        if (checkoutUI) checkoutUI.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    public void ExitCheckout()
    {
        if (!active) return;
        active = false;
        if (checkoutUI) checkoutUI.SetActive(false);
        if (playerMovement) playerMovement.enabled = true;
        if (productInteraction) productInteraction.enabled = true;
        if (mobileJoystick) mobileJoystick.enabled = true;
        if (touchLook) touchLook.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}