using UnityEngine;
using UnityEngine.InputSystem;

public class CheckoutManager : MonoBehaviour
{
    public static CheckoutManager Instance { get; private set; }

    [Header("References")]
    public PlayerMovement playerMovement;
    public ProductInteraction productInteraction;
    public MobileJoystick mobileJoystick;
    public TouchLook touchLook;

    [Header("Checkout Position & UI")]
    [Tooltip("Standing point in front of the cashier. Auto-finds a scene object named CheckoutPoint if empty.")]
    public Transform cashierStandPoint;
    [Tooltip("Exit checkout UI button. It is shown only during checkout.")]
    public GameObject exitButton;

    private bool active;
    public bool IsActive => active;
    private bool movementWasEnabled, interactionWasEnabled, joystickWasEnabled, touchLookWasEnabled;
    private CharacterController characterController;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (exitButton) exitButton.SetActive(false);
    }

    void Start()
    {
        if (!playerMovement) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (!productInteraction) productInteraction = FindFirstObjectByType<ProductInteraction>();
        if (!mobileJoystick) mobileJoystick = FindFirstObjectByType<MobileJoystick>();
        if (!touchLook) touchLook = FindFirstObjectByType<TouchLook>();
        if (!cashierStandPoint)
        {
            GameObject point = GameObject.Find("CheckoutPoint");
            if (point) cashierStandPoint = point.transform;
        }
        if (!exitButton)
        {
            GameObject button = GameObject.Find("ExitCheckoutButton");
            if (!button) button = GameObject.Find("CheckoutExitButton");
            if (button) exitButton = button;
        }
        if (playerMovement) characterController = playerMovement.GetComponent<CharacterController>();
        if (exitButton) exitButton.SetActive(false);
    }

    void Update()
    {
        if (active && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            ExitCheckout();
    }

    public void EnterCheckout()
    {
        if (active) return;
        if (!playerMovement) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (!playerMovement) { Debug.LogError("[CheckoutManager] PlayerMovement not found."); return; }
        if (!characterController) characterController = playerMovement.GetComponent<CharacterController>();

        movementWasEnabled = playerMovement.enabled;
        interactionWasEnabled = productInteraction && productInteraction.enabled;
        joystickWasEnabled = mobileJoystick && mobileJoystick.enabled;
        touchLookWasEnabled = touchLook && touchLook.enabled;

        if (playerMovement) playerMovement.enabled = false;
        if (productInteraction) productInteraction.enabled = false;
        if (mobileJoystick) { mobileJoystick.ResetInput(); mobileJoystick.enabled = false; }
        if (touchLook) touchLook.enabled = false;

        if (cashierStandPoint)
        {
            if (characterController) characterController.enabled = false;
            playerMovement.transform.SetPositionAndRotation(cashierStandPoint.position, cashierStandPoint.rotation);
            if (characterController) characterController.enabled = true;
        }
        else Debug.LogWarning("[CheckoutManager] Assign CheckoutPoint to Cashier Stand Point in the Inspector.");

        active = true;
        if (exitButton) exitButton.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("[CheckoutManager] Checkout entered: player positioned, movement disabled, exit button shown.");
    }

    public void ExitCheckout()
    {
        if (!active) return;
        active = false;
        if (playerMovement) playerMovement.enabled = movementWasEnabled;
        if (productInteraction) productInteraction.enabled = interactionWasEnabled;
        if (mobileJoystick) { mobileJoystick.enabled = joystickWasEnabled; mobileJoystick.ResetInput(); }
        if (touchLook) touchLook.enabled = touchLookWasEnabled;
        if (exitButton) exitButton.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("[CheckoutManager] Checkout exited.");
    }
}