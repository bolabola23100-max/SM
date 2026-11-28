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

    [Header("Checkout Position & UI")]
    public Transform cashierStandPoint;
    public GameObject exitButton;

    private bool active;
    public bool IsActive => active;
    private bool interactionWasEnabled, joystickWasEnabled, touchLookWasEnabled;
    private CharacterController characterController;
    private Button exitUnityButton;

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

        active = true;
        playerMovement.MovementLocked = true;

        if (productInteraction) productInteraction.enabled = false;
        if (mobileJoystick)
        {
            mobileJoystick.ResetInput();
            mobileJoystick.enabled = false; // disables joystick input, not its UI GameObject
        }
        if (touchLook) touchLook.enabled = touchLookWasEnabled; // keep camera touch-look working

        if (cashierStandPoint)
        {
            if (characterController) characterController.enabled = false;
            playerMovement.transform.SetPositionAndRotation(cashierStandPoint.position, cashierStandPoint.rotation);
            if (characterController) characterController.enabled = true;
        }
        else Debug.LogWarning("[CheckoutManager] Assign CheckoutPoint to Cashier Stand Point.");

        if (exitButton) exitButton.SetActive(true);
        if (exitUnityButton) exitUnityButton.interactable = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("[CheckoutManager] Checkout entered: movement locked, camera look active, exit button shown.");
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
        if (touchLook) touchLook.enabled = true;
        if (exitButton) exitButton.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("[CheckoutManager] Checkout exited.");
    }
}