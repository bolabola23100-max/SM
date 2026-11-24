using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which boxed products are inside one shelf's ProductZone trigger.
/// Attach this component to the same GameObject as the trigger BoxCollider.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ShelfZone : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] private string productTag = "Product";

    [Header("Optional Limit")]
    [Min(0)]
    [SerializeField] private int maxProducts = 0; // 0 = unlimited

    private readonly HashSet<Transform> products = new HashSet<Transform>();

    public int ProductCount => products.Count;
    public int MaxProducts => maxProducts;
    public bool IsFull => maxProducts > 0 && products.Count >= maxProducts;
    public IReadOnlyCollection<Transform> Products => products;

    public event Action<ShelfZone, Transform> ProductEntered;
    public event Action<ShelfZone, Transform> ProductExited;

    private void Reset()
    {
        BoxCollider trigger = GetComponent<BoxCollider>();
        trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        Transform product = FindProductRoot(other.transform);
        if (product == null)
            return;

        if (products.Add(product))
            ProductEntered?.Invoke(this, product);
    }

    private void OnTriggerExit(Collider other)
    {
        Transform product = FindProductRoot(other.transform);
        if (product == null)
            return;

        if (products.Remove(product))
            ProductExited?.Invoke(this, product);
    }

    private Transform FindProductRoot(Transform target)
    {
        Transform current = target;

        while (current != null)
        {
            if (current.CompareTag(productTag))
                return current;

            current = current.parent;
        }

        return null;
    }

    /// <summary>Removes destroyed or disabled product references from the tracked list.</summary>
    private void LateUpdate()
    {
        products.RemoveWhere(product => product == null);
    }

    public bool ContainsProduct(Transform product)
    {
        return product != null && products.Contains(product);
    }

    /// <summary>Registers a product placed by the player's stocking interaction.</summary>
    public bool RegisterProduct(Transform product)
    {
        if (product == null || IsFull)
            return false;

        if (products.Add(product))
            ProductEntered?.Invoke(this, product);

        return true;
    }

    public void UnregisterProduct(Transform product)
    {
        if (product != null && products.Remove(product))
            ProductExited?.Invoke(this, product);
    }

    public void SetMaxProducts(int amount)
    {
        maxProducts = Mathf.Max(0, amount);
    }
}
