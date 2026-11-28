using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns temporary capsule customers and manages their simple shopping/checkout queue.
/// Scene points are found by exact names, so no Inspector wiring is required.
/// </summary>
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
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        spawnPoint = GameObject.Find("CustomerSpawnPoint")?.transform;
        queuePoint = GameObject.Find("CustomerQueuePoint")?.transform;
        if (queuePoint == null) queuePoint = GameObject.Find("CheckoutPoint")?.transform;
        exitPoint = GameObject.Find("CustomerExitPoint")?.transform;

        if (spawnPoint == null)
        {
            Debug.LogError("[CustomerSystem] Create an Empty GameObject named CustomerSpawnPoint outside the store entrance.");
            enabled = false;
            return;
        }

        if (queuePoint == null)
            Debug.LogWarning("[CustomerSystem] CustomerQueuePoint not found yet. Customers will approach the CheckoutManager position until you add the queue point.");

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

        for (int i = 0; i < customers.Count; i++)
        {
            if (customers[i] != null && customers[i].IsWaiting == false)
                customers[i].UpdateQueuePosition(GetQueuePosition(customers[i]));
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
        Color outfit = customerColors[Random.Range(0, customerColors.Length)];
        agent.Initialize(this, body.transform.position, outfit);
        Debug.Log("[CustomerSystem] Temporary customer spawned.");
    }

    public ShelfZone GetRandomStockedShelf()
    {
        ShelfZone[] zones = FindObjectsOfType<ShelfZone>();
        List<ShelfZone> stocked = new List<ShelfZone>();

        foreach (ShelfZone zone in zones)
        {
            if (zone != null && zone.ProductCount > 0)
                stocked.Add(zone);
        }

        if (stocked.Count == 0) return null;
        return stocked[Random.Range(0, stocked.Count)];
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
        Debug.Log("[CustomerSystem] Customer reached the checkout queue.");
    }

    public void NotifySaleCompleted()
    {
        customers.RemoveAll(customer => customer == null);
        if (customers.Count == 0) return;

        CustomerAgent frontCustomer = customers[0];
        if (frontCustomer != null)
            frontCustomer.LeaveAfterPayment();

        customers.Remove(frontCustomer);
        Debug.Log("[CustomerSystem] Front customer paid and is leaving.");
    }

    public void RemoveCustomer(CustomerAgent customer)
    {
        customers.Remove(customer);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
