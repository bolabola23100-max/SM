using UnityEngine; using UnityEngine.InputSystem;
public class CheckoutManager : MonoBehaviour {
 public static CheckoutManager Instance {get;private set;} public PlayerMovement playerMovement; public ProductInteraction productInteraction; public MobileJoystick mobileJoystick; public TouchLook touchLook; private bool active; public bool IsActive=>active;
 void Awake(){if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;}
 void Start(){if(!playerMovement)playerMovement=FindFirstObjectByType<PlayerMovement>();if(!productInteraction)productInteraction=FindFirstObjectByType<ProductInteraction>();if(!mobileJoystick)mobileJoystick=FindFirstObjectByType<MobileJoystick>();if(!touchLook)touchLook=FindFirstObjectByType<TouchLook>();}
 void Update(){if(active&&Keyboard.current!=null&&Keyboard.current.eKey.wasPressedThisFrame)ExitCheckout();}
 public void EnterCheckout(){if(active)return;active=true;if(playerMovement)playerMovement.enabled=false;if(productInteraction)productInteraction.enabled=false;if(mobileJoystick)mobileJoystick.enabled=false;if(touchLook)touchLook.enabled=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;Debug.Log("[CheckoutManager] Entered checkout. Press E to exit.");}
 public void ExitCheckout(){if(!active)return;active=false;if(playerMovement)playerMovement.enabled=true;if(productInteraction)productInteraction.enabled=true;if(mobileJoystick)mobileJoystick.enabled=true;if(touchLook)touchLook.enabled=true;Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;Debug.Log("[CheckoutManager] Exited checkout.");}
}