using UnityEngine;

public enum ProductCategory
{
    Shelf,
    Fridge,
    Freezer,
    Vegetables
}

[DisallowMultipleComponent]
public class ProductItem : MonoBehaviour
{
    [Header("Product Data")]
    [SerializeField] private ProductData data;

    [Header("Optimization")]
    [Min(1f)]
    [SerializeField] private float visibilityDistance = 15f;

    private Renderer[] cachedRenderers;

    public ProductData Data => data;
    public string ProductId => data != null ? data.productId : string.Empty;
    public string ProductName => data != null ? data.productName : "Unknown";
    public float BuyPrice => data != null ? data.buyPrice : 0f;
    public float MarketPrice => data != null ? data.marketPrice : 0f;
    public ProductCategory Category => data != null ? data.category : ProductCategory.Shelf;
    public float VisibilityDistance => visibilityDistance;

    private void Awake()
    {
        CacheRenderers();
        ApplyOptimization();
    }

    private void OnValidate()
    {
        visibilityDistance = Mathf.Max(1f, visibilityDistance);
        if (!Application.isPlaying)
        {
            CacheRenderers();
            ApplyOptimization();
        }
    }

    public void SetData(ProductData productData)
    {
        data = productData;
    }

    public bool CanBePlacedOn(ProductCategory targetCategory)
    {
        return data != null && data.category == targetCategory;
    }

    private void CacheRenderers()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ApplyOptimization()
    {
        if (cachedRenderers == null)
            return;

        foreach (Renderer itemRenderer in cachedRenderers)
        {
            if (itemRenderer == null)
                continue;

            itemRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            itemRenderer.receiveShadows = false;
            itemRenderer.allowOcclusionWhenDynamic = true;
        }
    }
}