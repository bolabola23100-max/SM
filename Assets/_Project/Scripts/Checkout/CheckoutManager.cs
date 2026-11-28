using UnityEngine;
using UnityEngine.InputSystem;

public class CheckoutManager : MonoBehaviour
{
    public static CheckoutManager Instance { get; private set; }

    public PlayerMovement playerMovement;
    public ProductInteraction productInteraction;
    public MobileJoystick mobileJoystick;
    public TouchLook touchLook;

    private bool active;
    public bool IsActive => active;

    private bool movementWasEnabled;
    private bool interactionWasEnabled;
    private bool joystickWasEnabled;
    private bool touchLookWasEnabled;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (!playerMovement) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (!productInteraction) productInteraction = FindFirstObjectByType<ProductInteraction>();
        if (!mobileJoystick) mobileJoystick = FindFirstObjectByType<MobileJoystick>();
        if (!touchLook) touchLook = FindFirstObjectByType<TouchLook>();
    }

    void Update()
    {
        if (active && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            ExitCheckout();
    }

    public void EnterCheckout()
    {
        if (active) return;
        active = true;

        movementWasEnabled = playerMovement && playerMovement.enabled;
        interactionWasEnabled = productInteraction && productInteraction.enabled;
        joystickWasEnabled = mobileJoystick && mobileJoystick.enabled;
        touchLookWasEnabled = touchLook && touchLook.enabled;

        // Stop walking and product actions, but keep camera touch-look enabled.
        if (playerMovement) playerMovement.enabled = false;
        if (productInteraction) productInteraction.enabled = false;
        if (mobileJoystick) mobileJoystick.enabled = false;
        if (touchLook) touchLook.enabled = touchLookWasEnabled;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("[CheckoutManager] Checkout active: movement and joystick stopped; camera look remains enabled.");
    }

    public void ExitCheckout()
    {
        if (!active) return;
        active = false;

        if (playerMovement) playerMovement.enabled = movementWasEnabled;
        if (productInteraction) productInteraction.enabled = interactionWasEnabled;
        if (mobileJoystick) mobileJoystick.enabled = joystickWasEnabled;
        if (touchLook) touchLook.enabled = touchLookWasEnabled;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("[CheckoutManager] Checkout exited.");
    }
}
