using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CustomerSystem : MonoBehaviour
{
    public static CustomerSystem Instance { get; private set; }

    [Header("Automatic Customer Flow")]
    [Min(3f)] public float spawnInterval = 12f;
    [Range(1, 8)] public int maxCustomers = 4;
    [Min(0.4f)] public float queueSpacing = 0.85f;

    private Transform spawnPoint;
    private Transform queuePoint;
    private Transform exitPoint;
    public Transform ScannerPoint { get; private set; }

    private readonly List<CustomerAgent> customers = new List<CustomerAgent>();
    private float spawnTimer = 3f;
    private readonly Color[] customerColors =
    {
        new Color(0.16f, 0.42f, 0.78f),
        new Color(0.72f, 0.28f, 0.22f),
        new Color(0.22f, 0.58f, 0.38f),
        new Color(0.55f, 0.36f, 0.70f),
        new Color(0.82f, 0.55f, 0.18f)
    };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        spawnPoint = GameObject.Find("CustomerSpawnPoint")?.transform;
        // Prefer the dedicated queue marker. Support the existing scene spelling too.
        queuePoint = GameObject.Find("CustomerQueuePoint")?.transform;
        if (queuePoint == null) queuePoint = GameObject.Find("CustommerQueuePint")?.transform;
        if (queuePoint == null) queuePoint = GameObject.Find("CustomerQueuePint")?.transform;
        if (queuePoint == null) queuePoint = GameObject.Find("CheckoutPoint")?.transform;
        exitPoint = GameObject.Find("CustomerExitPoint")?.transform;

        // Reuse the existing ScannerPoint. CustomerConveyorPoint is only a fallback.
        ScannerPoint = GameObject.Find("ScannerPoint")?.transform;
        if (ScannerPoint == null)
            ScannerPoint = GameObject.Find("CustomerConveyorPoint")?.transform;

        if (spawnPoint == null)
        {
            Debug.LogError("[CustomerSystem] Create CustomerSpawnPoint outside the store entrance.");
            enabled = false;
            return;
        }

        if (ScannerPoint == null)
            Debug.LogError("[CustomerSystem] ScannerPoint was not found. Create an Empty named ScannerPoint at the conveyor surface.");

        if (queuePoint == null)
            Debug.LogWarning("[CustomerSystem] CustomerQueuePoint not found; CheckoutPoint will be used if available.");

        spawnTimer = 2f;
    }

    private void Update()
    {
        customers.RemoveAll(customer => customer == null);
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f && customers.Count < maxCustomers)
        {
            SpawnCustomer();
            spawnTimer = spawnInterval;
        }
    }

    private void SpawnCustomer()
    {
        if (spawnPoint == null) return;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "TemporaryCustomer";
        body.transform.localScale = new Vector3(0.62f, 0.9f, 0.62f);
        body.transform.position = spawnPoint.position + Vector3.up * 0.9f;

        CustomerAgent agent = body.AddComponent<CustomerAgent>();
        customers.Add(agent);
        agent.Initialize(this, body.transform.position, customerColors[Random.Range(0, customerColors.Length)]);
        Debug.Log("[CustomerSystem] Temporary customer spawned.");
    }

    public int GetRandomProductCount()
    {
        // Inclusive range: each customer randomly wants between one and four items.
        return Random.Range(1, 5);
    }

    public ShelfZone GetRandomStockedShelf()
    {
        ShelfZone[] zones = FindObjectsOfType<ShelfZone>();
        List<ShelfZone> stocked = new List<ShelfZone>();
        foreach (ShelfZone zone in zones)
            if (zone != null && zone.ProductCount > 0) stocked.Add(zone);

        return stocked.Count == 0 ? null : stocked[Random.Range(0, stocked.Count)];
    }

    public Vector3 GetQueuePosition(CustomerAgent customer)
    {
        if (queuePoint == null)
        {
            CheckoutManager checkout = CheckoutManager.Instance;
            if (checkout != null && checkout.cashierStandPoint != null)
                return checkout.cashierStandPoint.position - checkout.cashierStandPoint.forward * 1.2f;
            return transform.position;
        }

        int index = Mathf.Max(0, customers.IndexOf(customer));
        Vector3 position = queuePoint.position - queuePoint.forward * (index * queueSpacing);
        position.y = customer != null ? customer.transform.position.y : queuePoint.position.y;
        return position;
    }

    public Vector3 GetExitPosition()
    {
        if (exitPoint != null) return exitPoint.position;
        if (spawnPoint != null) return spawnPoint.position - spawnPoint.forward * 2f;
        return transform.position;
    }

    public void NotifyCustomerWaiting(CustomerAgent customer)
    {
        if (customers.Count > 0 && customers[0] == customer)
            SendFrontCustomerToScanner();
        Debug.Log("[CustomerSystem] Customer reached the checkout queue.");
    }

    public void NotifyCustomerAtScanner(CustomerAgent customer)
    {
        Debug.Log("[CustomerSystem] Front customer is ready for scanning.");
    }

    private void SendFrontCustomerToScanner()
    {
        if (customers.Count == 0 || ScannerPoint == null) return;
        CustomerAgent front = customers[0];
        if (front != null && front.IsWaitingInQueue)
            front.GoToConveyor(ScannerPoint);
    }

    public bool IsFrontCustomerProduct(ProductItem item)
    {
        if (item == null || customers.Count == 0) return false;
        CustomerAgent front = customers[0];
        return front != null && front.IsWaitingAtScanner && front.ConveyorProducts.Contains(item);
    }

    public bool AreAllFrontCustomerProductsScanned(ICollection<ProductItem> scannedItems)
    {
        if (customers.Count == 0 || scannedItems == null) return false;
        CustomerAgent front = customers[0];
        if (front == null || !front.IsWaitingAtScanner || front.ConveyorProducts.Count == 0) return false;

        foreach (ProductItem product in front.ConveyorProducts)
            if (product != null && !scannedItems.Contains(product))
                return false;

        return true;
    }

    public void NotifySaleCompleted(bool frontCustomerProductsWereScanned)
    {
        customers.RemoveAll(customer => customer == null);
        if (!frontCustomerProductsWereScanned || customers.Count == 0)
        {
            Debug.Log("[CustomerSystem] Sale did not include the front customer's conveyor product; queue unchanged.");
            return;
        }

        CustomerAgent frontCustomer = customers[0];
        if (frontCustomer != null && frontCustomer.IsWaitingAtScanner)
        {
            frontCustomer.LeaveAfterPayment();
            customers.RemoveAt(0);
            Debug.Log("[CustomerSystem] Front customer paid and is leaving.");

            for (int i = 0; i < customers.Count; i++)
                if (customers[i] != null)
                    customers[i].MoveForwardInQueue(GetQueuePosition(customers[i]));

            SendFrontCustomerToScanner();
        }
    }

    public void RemoveCustomer(CustomerAgent customer) => customers.Remove(customer);

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
