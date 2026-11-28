using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Checkout trigger: enter its area and press E or call Interact from a mobile UI button.</summary>
public class CheckoutStation : MonoBehaviour
{
    [SerializeField] private CheckoutManager checkoutManager;
    private bool playerInside;
    void Awake(){ if(!checkoutManager) checkoutManager=CheckoutManager.Instance; }
    void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>())
            playerInside=true;
    }
    void OnTriggerExit(Collider other)
    {
        if(other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>())
            playerInside=false;
    }
    void Update()
    {
        if(playerInside && Keyboard.current!=null && Keyboard.current.eKey.wasPressedThisFrame)
            Interact();
    }
    public void Interact()
    {
        if(!playerInside) return;
        if(!checkoutManager) checkoutManager=CheckoutManager.Instance;
        if(checkoutManager) checkoutManager.EnterCheckout();
    }
}