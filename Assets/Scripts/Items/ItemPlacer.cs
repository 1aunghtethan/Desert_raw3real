using UnityEngine;

/// <summary>
/// Handles placing items from inventory onto the ground.
/// Right-click while looking at the ground to place the currently held item.
/// For weapons (Melee/Ranged), their own AltUse behavior takes priority.
/// Attach to the Player GameObject.
/// </summary>
public class ItemPlacer : MonoBehaviour
{
    [Header("Placement Settings")]
    public float PlaceDistance = 6f;
    public float PlaceHeightOffset = 0.2f;
    public LayerMask GroundLayers = ~0; // Default: all layers

    private Inventory _inventory;
    private PlayerController _player;
    private EquipmentHolder _equipment;

    void Start()
    {
        _inventory = GetComponent<Inventory>();
        if (_inventory == null) _inventory = Inventory.Instance;

        _player = GetComponent<PlayerController>();
        if (_player == null) _player = FindFirstObjectByType<PlayerController>();

        _equipment = GetComponent<EquipmentHolder>();
        if (_equipment == null) _equipment = EquipmentHolder.Instance;
    }

    void Update()
    {
        // Only handle right-click placement
        if (!Input.GetMouseButtonDown(1)) return;

        // Don't place if inventory panel is open
        InventoryPanelUI panel = FindFirstObjectByType<InventoryPanelUI>();
        if (panel != null && panel.GetComponent<CanvasGroup>() != null 
            && panel.GetComponent<CanvasGroup>().alpha > 0.5f) return;

        ItemData item = _inventory?.SelectedItem;
        if (item == null) return;

        // If the equipped weapon has its own AltUse, let it handle right-click
        // (Melee/Ranged weapons have their own behavior)
        if (_equipment != null && _equipment.CurrentBehaviour != null)
        {
            if (item.Type == ItemType.Melee || item.Type == ItemType.Ranged)
            {
                return; // Let EquipmentHolder handle it
            }
        }

        // Raycast to find ground
        Camera cam = (_player != null) ? _player.GetActiveCamera() : Camera.main;
        if (cam == null) return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));

        // Use RaycastAll to pass through the player collider
        RaycastHit[] hits = Physics.RaycastAll(ray, PlaceDistance, GroundLayers);
        
        RaycastHit? bestHit = null;
        float closestDist = float.MaxValue;
        
        foreach (var hit in hits)
        {
            // Skip the player's own colliders
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
            // Skip other LootItems
            if (hit.collider.GetComponent<LootItem>() != null) continue;
            
            if (hit.distance < closestDist)
            {
                closestDist = hit.distance;
                bestHit = hit;
            }
        }

        if (!bestHit.HasValue) return;

        // Place the item at the hit point
        Vector3 placePos = bestHit.Value.point + Vector3.up * PlaceHeightOffset;

        GameObject placedObj = ItemDropper.CreateWorldPickup(item, placePos);

        if (placedObj != null)
        {
            // For placed items, start settled (no throw force)
            Rigidbody rb = placedObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                
                // If it's a heavy static object (like a log), freeze it completely in place!
                if (!item.PickupsSpinAndBob)
                {
                    rb.isKinematic = true;
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }
            }

            // Remove one from inventory
            _inventory.RemoveItem(_inventory.SelectedIndex, 1);

            Debug.Log($"[ItemPlacer] Placed {item.ItemName} at {placePos}");
        }
    }
}
