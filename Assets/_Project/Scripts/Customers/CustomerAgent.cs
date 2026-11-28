using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Temporary customer that shops, places one real product at ScannerPoint, and waits for payment.</summary>
public class CustomerAgent : MonoBehaviour
{
    private enum State { GoingToShelf, GoingToQueue, WaitingInQueue, GoingToConveyor, WaitingAtScanner, Leaving }

    private State state;
    private CustomerSystem owner;
    private NavMeshAgent navAgent;
    private Transform carryPoint;
    private readonly List<ProductItem> carriedProducts = new List<ProductItem>();
    private readonly List<ProductItem> conveyorProducts = new List<ProductItem>();
    private readonly HashSet<ProductItem> scannedConveyorProducts = new HashSet<ProductItem>();
    private int desiredProductCount = 1;
    private float moveSpeed = 1.35f;
    private float arriveDistance = 0.35f;
    private Vector3 directTarget;
    private ShelfZone currentShelf;

    public bool IsWaitingAtScanner => state == State.WaitingAtScanner;
    public bool IsWaitingInQueue => state == State.WaitingInQueue;
    public IReadOnlyList<ProductItem> ConveyorProducts => conveyorProducts;

    public void Initialize(CustomerSystem system, Vector3 spawnPosition, Color outfitColor)
    {
        owner = system;
        transform.position = spawnPosition;

        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule != null) capsule.height = 1.8f;

        Renderer bodyRenderer = GetComponent<Renderer>();
        if (bodyRenderer != null)
        {
            bodyRenderer.material.color = outfitColor;
            bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        GameObject hand = new GameObject("CustomerCarryPoint");
        carryPoint = hand.transform;
        carryPoint.SetParent(transform, false);
        carryPoint.localPosition = new Vector3(0f, 0.55f, 0.48f);

        navAgent = gameObject.AddComponent<NavMeshAgent>();
        navAgent.speed = moveSpeed;
        navAgent.angularSpeed = 360f;
        navAgent.acceleration = 8f;
        navAgent.radius = 0.28f;
        navAgent.height = 1.8f;
        navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(spawnPosition, out hit, 2f, NavMesh.AllAreas))
        {
            navAgent.enabled = true;
            navAgent.Warp(hit.position);
        }
        else navAgent.enabled = false;

        desiredProductCount = owner.GetRandomProductCount();
        ShelfZone firstShelf = owner.GetRandomStockedShelf();
        if (firstShelf != null) GoToShelf(firstShelf);
        else LeaveWithoutPurchase();
    }

    private void Update()
    {
        if (state == State.WaitingInQueue || state == State.WaitingAtScanner) return;

        if (HasReachedDestination())
        {
            if (state == State.GoingToShelf)
            {
                if (TryTakeOneProduct())
                {
                    // Keep shopping until this customer's random basket size is reached.
                    ShelfZone nextShelf = carriedProducts.Count < desiredProductCount ? owner.GetRandomStockedShelf() : null;
                    if (nextShelf != null) GoToShelf(nextShelf);
                    else GoToQueue();
                }
                else if (carriedProducts.Count > 0) GoToQueue();
                else LeaveWithoutPurchase();
            }
            else if (state == State.GoingToQueue)
            {
                state = State.WaitingInQueue;
                owner.NotifyCustomerWaiting(this);
            }
            else if (state == State.GoingToConveyor)
            {
                PlaceProductOnConveyor();
                state = State.WaitingAtScanner;
                owner.NotifyCustomerAtScanner(this);
            }
            else if (state == State.Leaving)
            {
                owner.RemoveCustomer(this);
                Destroy(gameObject);
            }
            return;
        }

        if (navAgent == null || !navAgent.enabled || !navAgent.isOnNavMesh)
        {
            Vector3 flatTarget = new Vector3(directTarget.x, transform.position.y, directTarget.z);
            Vector3 direction = flatTarget - transform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                transform.position = Vector3.MoveTowards(transform.position, flatTarget, moveSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(direction.normalized), 8f * Time.deltaTime);
            }
        }
    }

    private bool HasReachedDestination()
    {
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
            return !navAgent.pathPending && navAgent.remainingDistance <= Mathf.Max(arriveDistance, navAgent.stoppingDistance + 0.05f);

        Vector3 a = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 b = new Vector3(directTarget.x, 0f, directTarget.z);
        return Vector3.Distance(a, b) <= arriveDistance;
    }

    private void SetDestination(Vector3 position)
    {
        directTarget = position;
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(position, out hit, 2f, NavMesh.AllAreas))
                navAgent.SetDestination(hit.position);
            else
                Debug.LogWarning("[CustomerAgent] Destination is not close to the baked NavMesh: " + position);
        }
    }

    private void GoToShelf(ShelfZone shelf)
    {
        currentShelf = shelf;
        state = State.GoingToShelf;
        Vector3 target = shelf.transform.position + shelf.transform.forward * 0.65f;
        SetDestination(target);
    }

    private bool TryTakeOneProduct()
    {
        if (currentShelf == null || currentShelf.Products == null) return false;

        foreach (Transform productTransform in currentShelf.Products)
        {
            if (productTransform == null) continue;
            ProductItem item = productTransform.GetComponent<ProductItem>();
            if (item == null || item.Data == null) continue;

            currentShelf.UnregisterProduct(productTransform);
            carriedProducts.Add(item);

            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Collider[] colliders = item.GetComponentsInChildren<Collider>();
            foreach (Collider itemCollider in colliders) itemCollider.enabled = false;

            // Keep the product's original world size: the temporary customer capsule
            // is scaled down, so parenting directly would shrink the product too.
            Vector3 originalWorldScale = item.transform.lossyScale;
            item.transform.SetParent(carryPoint, false);
            Vector3 parentScale = carryPoint.lossyScale;
            item.transform.localScale = new Vector3(
                SafeScaleDivide(originalWorldScale.x, parentScale.x),
                SafeScaleDivide(originalWorldScale.y, parentScale.y),
                SafeScaleDivide(originalWorldScale.z, parentScale.z));
            int carryIndex = carriedProducts.Count - 1;
            item.transform.localPosition = new Vector3((carryIndex % 2) * 0.22f, (carryIndex / 2) * 0.18f, 0f);
            item.transform.localRotation = Quaternion.identity;
            return true;
        }
        return false;
    }

    private static float SafeScaleDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
    }

    private void LeaveWithoutPurchase()
    {
        state = State.Leaving;
        SetDestination(owner.GetExitPosition());
    }

    private void GoToQueue()
    {
        state = State.GoingToQueue;
        SetDestination(owner.GetQueuePosition(this));
    }

    public void MoveForwardInQueue(Vector3 position)
    {
        if (state == State.Leaving || state == State.WaitingAtScanner) return;
        state = State.GoingToQueue;
        SetDestination(position);
    }

    public void GoToConveyor(Transform point)
    {
        if (point == null || state == State.Leaving || state == State.WaitingAtScanner) return;
        state = State.GoingToConveyor;
        SetDestination(point.position);
    }

    private void PlaceProductOnConveyor()
    {
        if (carriedProducts.Count == 0 || owner.ScannerPoint == null)
        {
            Debug.LogWarning("[CustomerAgent] Customer reached ScannerPoint without products or scanner point.");
            return;
        }

        conveyorProducts.Clear();
        scannedConveyorProducts.Clear();
        for (int i = 0; i < carriedProducts.Count; i++)
        {
            ProductItem product = carriedProducts[i];
            if (product == null) continue;

            Transform itemTransform = product.transform;
            itemTransform.SetParent(null, true);

            Collider[] colliders = product.GetComponentsInChildren<Collider>(true);
            foreach (Collider itemCollider in colliders) itemCollider.enabled = true;

            Rigidbody body = product.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }

            conveyorProducts.Add(product);
        }

        carriedProducts.Clear();
        ArrangeConveyorProducts();
        Debug.Log("[CustomerAgent] Customer placed " + conveyorProducts.Count + " products in the checkout queue (max 3 visible on the belt).");
    }

    /// <summary>Marks one item as scanned and advances the next waiting item onto the belt.</summary>
    public void OnProductScanned(ProductItem product)
    {
        if (product == null || !conveyorProducts.Contains(product)) return;
        scannedConveyorProducts.Add(product);
        ArrangeConveyorProducts();
    }

    private void ArrangeConveyorProducts()
    {
        if (owner == null || owner.ScannerPoint == null) return;

        int beltIndex = 0;
        int waitingIndex = 0;
        for (int i = 0; i < conveyorProducts.Count; i++)
        {
            ProductItem product = conveyorProducts[i];
            if (product == null || scannedConveyorProducts.Contains(product)) continue;

            Transform itemTransform = product.transform;
            itemTransform.SetParent(null, true);
            Vector3 position;

            if (beltIndex < 3)
            {
                // Three fixed positions: left, centre, right. A scan advances the queue.
                float sideOffset = (beltIndex - 1) * 0.32f;
                position = owner.ScannerPoint.position + owner.ScannerPoint.right * sideOffset;
                beltIndex++;
            }
            else
            {
                // Extra products wait behind the belt and move forward as slots become free.
                position = owner.ScannerPoint.position
                    - owner.ScannerPoint.forward * (0.65f + waitingIndex * 0.32f);
                waitingIndex++;
            }

            itemTransform.SetPositionAndRotation(position, owner.ScannerPoint.rotation);
        }
    }

    public void LeaveAfterPayment()
    {
        if (state == State.Leaving) return;
        state = State.Leaving;
        conveyorProducts.Clear();
        scannedConveyorProducts.Clear();
        carriedProducts.Clear();
        SetDestination(owner.GetExitPosition());
    }
}
