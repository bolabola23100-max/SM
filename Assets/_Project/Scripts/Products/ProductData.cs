using UnityEngine;

[CreateAssetMenu(fileName = "NewProduct", menuName = "Supermarket/Product Data")]
public class ProductData : ScriptableObject
{
    [Header("Identity")]
    public string productId;
    public string productName;

    [Header("Category")]
    public ProductCategory category = ProductCategory.Shelf;

    [Header("Prefab")]
    public GameObject productPrefab;

    [Header("Prices")]
    [Min(0f)] public float buyPrice = 1f;
    [Min(0f)] public float marketPrice = 5f;

    [Header("Carton")]
    [Min(1)] public int packSize = 12;

    [Header("Shelf Layout")]
    [Min(1)] public int columns = 3;
    [Min(1)] public int rows = 4;
    [Min(1)] public int layers = 1;

    [Header("Placement")]
    [Min(0f)] public float productHeight = 0.15f;
    public Vector3 rotation = new Vector3(0f, 90f, 0f);
    public Vector3 offset = Vector3.zero;

    public int ShelfLimit => Mathf.Max(1, columns) * Mathf.Max(1, rows) * Mathf.Max(1, layers);
    public float PackCost => buyPrice * Mathf.Max(1, packSize);

    private void OnValidate()
    {
        packSize = Mathf.Max(1, packSize);
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        layers = Mathf.Max(1, layers);
        buyPrice = Mathf.Max(0f, buyPrice);
        marketPrice = Mathf.Max(0f, marketPrice);
        productHeight = Mathf.Max(0f, productHeight);

        if (string.IsNullOrWhiteSpace(productId))
            productId = name.Trim().ToLowerInvariant().Replace(" ", "_");

        if (string.IsNullOrWhiteSpace(productName))
            productName = name;
    }
}