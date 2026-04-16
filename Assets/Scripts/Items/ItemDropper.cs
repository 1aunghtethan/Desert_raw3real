using UnityEngine;

/// <summary>
/// Handles dropping items from the inventory into the world.
/// Press Q to throw the currently held item forward.
/// Attach to the Player GameObject.
/// </summary>
public class ItemDropper : MonoBehaviour
{
    [Header("Drop Settings")]
    public KeyCode DropKey = KeyCode.Q;
    public float ThrowForce = 6f; // Increased for better throw
    public float ThrowUpForce = 3f; // Restored to original arc
    public float DropSpawnDistance = 1.6f; // Restored and slightly increased
    public float DropPrefabScale = 0.4f;

    private Inventory _inventory;
    private PlayerController _player;

    void Start()
    {
        _inventory = GetComponent<Inventory>();
        if (_inventory == null) _inventory = Inventory.Instance;

        _player = GetComponent<PlayerController>();
        if (_player == null) _player = FindFirstObjectByType<PlayerController>();

        // Prevent all items from colliding with each other (stops exploding physics when a tree drops multiple logs)
        int itemLayer = LayerMask.NameToLayer("Item");
        if (itemLayer >= 0)
        {
            Physics.IgnoreLayerCollision(itemLayer, itemLayer, true);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(DropKey))
        {
            DropCurrentItem();
        }
    }

    /// <summary>
    /// Drops one count of the currently selected item into the world.
    /// </summary>
    public void DropCurrentItem()
    {
        if (_inventory == null) return;

        int selectedIndex = _inventory.SelectedIndex;
        ItemData item = _inventory.SelectedItem;

        if (item == null) return;

        // Get the camera for spawn direction
        Camera cam = (_player != null) ? _player.GetActiveCamera() : Camera.main;
        if (cam == null) return;

        // Spawn position: forward and slightly lower (hand/chest height) for a better throw arc
        Vector3 spawnPos = cam.transform.position + cam.transform.forward * DropSpawnDistance + Vector3.down * 0.2f;

        // Create the world pickup object
        GameObject droppedObj = CreateWorldPickup(item, spawnPos);

        if (droppedObj != null)
        {
            // Apply throw force
            Rigidbody rb = droppedObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 throwDir = cam.transform.forward * ThrowForce + Vector3.up * ThrowUpForce;
                rb.AddForce(throwDir, ForceMode.Impulse);

                // Add a little spin for visual flair
                rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);
            }

            // Remove one from inventory
            _inventory.RemoveItem(selectedIndex, 1);

            Debug.Log($"[ItemDropper] Dropped {item.ItemName}");
        }
    }

    /// <summary>
    /// Creates a world pickup GameObject for the given item.
    /// Uses DropPrefab if available, otherwise clones Prefab at smaller scale.
    /// </summary>
    public static GameObject CreateWorldPickup(ItemData item, Vector3 position)
    {
        if (item == null) return null;

        GameObject pickupObj;

        if (item.DropPrefab != null)
        {
            // Use the dedicated drop prefab, preserving its original local rotation
            pickupObj = Instantiate(item.DropPrefab, position, item.DropPrefab.transform.rotation);
        }
        else if (item.Prefab != null)
        {
            // Clone the weapon prefab and scale it down, preserving original rotation
            pickupObj = Instantiate(item.Prefab, position, item.Prefab.transform.rotation);
            pickupObj.transform.localScale = Vector3.one * 0.4f;
        }
        else
        {
            // Fallback: create a simple cube
            pickupObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pickupObj.transform.position = position;
            pickupObj.transform.localScale = Vector3.one * 0.3f;

            // Color it based on item type
            Renderer rend = pickupObj.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(rend.sharedMaterial);
                mat.color = GetItemColor(item.Type);
                rend.material = mat;
            }
        }
        
        pickupObj.name = $"Drop_{item.ItemName}";

        // Set to Item layer recursively so children/meshes are also detectable
        SetLayerRecursive(pickupObj, LayerMask.NameToLayer("Item"));

        // Disable any existing ItemBehaviour (we don't want it to attack)
        ItemBehaviour[] behaviours = pickupObj.GetComponentsInChildren<ItemBehaviour>();
        foreach (var b in behaviours)
        {
            Object.Destroy(b);
        }

        // Ensure it has a Rigidbody for physics
        Rigidbody rb = pickupObj.GetComponent<Rigidbody>();
        if (rb == null) rb = pickupObj.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.mass = 0.5f;
        rb.linearDamping = 0.1f;

        // Ensure it has a collider
        Collider col = pickupObj.GetComponent<Collider>();
        if (col == null)
        {
            Collider[] childCols = pickupObj.GetComponentsInChildren<Collider>();
            if (childCols.Length == 0)
            {
                BoxCollider box = pickupObj.AddComponent<BoxCollider>();
                box.size = Vector3.one * 0.5f;
            }
            else
            {
                // Re-enable any disabled colliders
                foreach (Collider c in childCols)
                {
                    c.enabled = true;
                }
            }
        }
        else
        {
            col.enabled = true;
        }

        // Add LootItem component for pickup
        LootItem loot = pickupObj.GetComponent<LootItem>();
        if (loot == null) loot = pickupObj.AddComponent<LootItem>();
        loot.Data = item;
        loot.PickupRadius = 4f; // Standardized pickup radius

        // Add OutlineController immediately for visual feedback
        if (pickupObj.GetComponent<OutlineController>() == null)
            pickupObj.AddComponent<OutlineController>();

        // Add a slow rotation for visual effect if configured
        if (item.PickupsSpinAndBob)
        {
            WorldItemSpin spin = pickupObj.GetComponent<WorldItemSpin>();
            if (spin == null) spin = pickupObj.AddComponent<WorldItemSpin>();
        }
        else
        {
            // Remove it if it exists by default
            WorldItemSpin spin = pickupObj.GetComponent<WorldItemSpin>();
            if (spin != null) Object.Destroy(spin);
        }

        return pickupObj;
    }

    private static Color GetItemColor(ItemType type)
    {
        switch (type)
        {
            case ItemType.Melee: return new Color(0.7f, 0.7f, 0.7f);
            case ItemType.Ranged: return new Color(0.6f, 0.4f, 0.2f);
            case ItemType.Tool: return new Color(0.4f, 0.4f, 0.5f);
            case ItemType.Consumable: return new Color(0.8f, 0.2f, 0.2f);
            default: return Color.white;
        }
    }

    private static void SetLayerRecursive(GameObject obj, int layer)
    {
        if (layer == -1) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }
}
