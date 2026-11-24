using UnityEngine;

/// <summary>
/// Data and remaining stock carried by one physical carton.
/// Attach this component to the root GameObject tagged Box.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class ProductBox : MonoBehaviour
{
    [Header("Carton Contents")]
    [SerializeField] private ProductData productData;
    [Min(1)]
    [SerializeField] private int productCount = 12;

    [Header("State")]
    [SerializeField] private bool isOpen;

    public ProductData Data => productData;
    public int RemainingCount => productCount;
    public bool IsOpen => isOpen;
    public bool IsEmpty => productCount <= 0;

    public bool Open()
    {
        if (isOpen || IsEmpty || productData == null || productData.productPrefab == null)
            return false;

        isOpen = true;
        return true;
    }

    public bool RemoveOne()
    {
        if (!isOpen || IsEmpty || productData == null)
            return false;

        productCount--;
        return true;
    }

    public void Close()
    {
        isOpen = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        productCount = Mathf.Max(1, productCount);
    }
#endif
}
