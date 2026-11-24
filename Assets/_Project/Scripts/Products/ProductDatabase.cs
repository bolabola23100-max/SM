using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ProductDatabase", menuName = "Supermarket/Product Database")]
public class ProductDatabase : ScriptableObject
{
    [Serializable]
    public class SellPriceEntry
    {
        public string productId;
        [Min(0f)] public float price;
    }

    [Serializable]
    public class SaveData
    {
        public List<SellPriceEntry> sellPrices = new List<SellPriceEntry>();
    }

    [Header("All Products")]
    [SerializeField] private List<ProductData> products = new List<ProductData>();

    [Header("Runtime Sell Price Overrides")]
    [SerializeField] private List<SellPriceEntry> sellPriceOverrides = new List<SellPriceEntry>();

    private Dictionary<string, ProductData> productsById;
    private Dictionary<string, float> sellPricesById;

    public IReadOnlyList<ProductData> Products => products;

    private void OnEnable()
    {
        RebuildLookups();
    }

    private void OnValidate()
    {
        RebuildLookups();
    }

    public void RebuildLookups()
    {
        productsById = new Dictionary<string, ProductData>(StringComparer.OrdinalIgnoreCase);
        sellPricesById = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (ProductData product in products)
        {
            if (product == null || string.IsNullOrWhiteSpace(product.productId))
                continue;

            productsById[product.productId.Trim()] = product;
        }

        foreach (SellPriceEntry entry in sellPriceOverrides)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.productId))
                continue;

            sellPricesById[entry.productId.Trim()] = Mathf.Max(0f, entry.price);
        }
    }

    public List<string> GetNames()
    {
        var result = new List<string>();
        foreach (ProductData product in products)
        {
            if (product != null)
                result.Add(product.productName);
        }
        return result;
    }

    public ProductData GetProduct(string productId)
    {
        EnsureLookups();
        if (string.IsNullOrWhiteSpace(productId))
            return null;

        productsById.TryGetValue(productId.Trim(), out ProductData product);
        return product;
    }

    public GameObject GetPrefab(string productId)
    {
        return GetProduct(productId)?.productPrefab;
    }

    public int GetPackSize(string productId)
    {
        return GetProduct(productId)?.packSize ?? 0;
    }

    public float GetBuyPrice(string productId)
    {
        return GetProduct(productId)?.buyPrice ?? 0f;
    }

    public float GetMarketPrice(string productId)
    {
        return GetProduct(productId)?.marketPrice ?? 0f;
    }

    public float GetPackCost(string productId)
    {
        ProductData product = GetProduct(productId);
        return product != null ? product.PackCost : 0f;
    }

    public float GetSellPrice(string productId)
    {
        EnsureLookups();
        ProductData product = GetProduct(productId);
        if (product == null)
            return 0f;

        return sellPricesById.TryGetValue(product.productId, out float customPrice)
            ? customPrice
            : product.marketPrice;
    }

    public bool SetSellPrice(string productId, float price)
    {
        ProductData product = GetProduct(productId);
        if (product == null)
            return false;

        price = Mathf.Max(0f, price);
        EnsureLookups();
        sellPricesById[product.productId] = price;

        SellPriceEntry existing = sellPriceOverrides.Find(
            entry => entry != null && string.Equals(entry.productId, product.productId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            existing = new SellPriceEntry { productId = product.productId, price = price };
            sellPriceOverrides.Add(existing);
        }
        else
        {
            existing.price = price;
        }

        return true;
    }

    public int GetShelfLimit(string productId)
    {
        return GetProduct(productId)?.ShelfLimit ?? 0;
    }

    public bool BuyChance(string productId, float chance)
    {
        if (GetProduct(productId) == null)
            return false;

        return UnityEngine.Random.value < Mathf.Clamp01(chance);
    }

    public SaveData GetSaveData()
    {
        EnsureLookups();
        var saveData = new SaveData();

        foreach (KeyValuePair<string, float> pair in sellPricesById)
        {
            saveData.sellPrices.Add(new SellPriceEntry
            {
                productId = pair.Key,
                price = pair.Value
            });
        }

        return saveData;
    }

    public void LoadSaveData(SaveData saveData)
    {
        sellPriceOverrides.Clear();
        if (saveData != null && saveData.sellPrices != null)
        {
            foreach (SellPriceEntry entry in saveData.sellPrices)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.productId))
                    continue;

                sellPriceOverrides.Add(new SellPriceEntry
                {
                    productId = entry.productId,
                    price = Mathf.Max(0f, entry.price)
                });
            }
        }

        RebuildLookups();
    }

    private void EnsureLookups()
    {
        if (productsById == null || sellPricesById == null)
            RebuildLookups();
    }
}