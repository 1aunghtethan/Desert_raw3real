using UnityEngine;

/// <summary>
/// Handles placing items from inventory onto the ground with a ghost preview.
/// Press B to toggle placement mode on/off.
/// While in placement mode: Right-click to place, R to rotate.
/// For weapons, hold Left Shift + Right Click to place.
/// </summary>
public class ItemPlacer : MonoBehaviour
{
    [Header("Placement Settings")]
    public float PlaceDistance = 10f;
    public float PlaceHeightOffset = 0.05f;
    public LayerMask GroundLayers = ~0;
    public KeyCode PlaceModeKey = KeyCode.B;
    public KeyCode RotateKey = KeyCode.R;
    public float RotationStep = 90f;

    [Header("Ghost Preview")]
    public Color ValidColor = new Color(0.2f, 1.0f, 0.2f, 0.4f);
    public Color InvalidColor = new Color(1.0f, 0.2f, 0.2f, 0.4f);

    private Inventory _inventory;
    private PlayerController _player;
    private EquipmentHolder _equipment;
    private ItemDropper _dropper;

    // Cached UI reference (avoid FindFirstObjectByType every frame)
    private InventoryPanelUI _cachedPanel;
    private CanvasGroup _cachedPanelCanvasGroup;

    // Ghost state
    private GameObject _ghostObj;
    private Material _ghostMaterial;
    private ItemData _lastItem;
    private float _currentRotationY = 0f;
    private bool _placementModeActive = false;

    // Layer for ghost to prevent interference with other raycasts
    private const string GhostLayerName = "Ignore Raycast";

    void Start()
    {
        _inventory = GetComponent<Inventory>();
        if (_inventory == null) _inventory = Inventory.Instance;

        _player = GetComponent<PlayerController>();
        if (_player == null) _player = FindFirstObjectByType<PlayerController>();

        _equipment = GetComponent<EquipmentHolder>();
        if (_equipment == null) _equipment = EquipmentHolder.Instance;

        _dropper = GetComponent<ItemDropper>();
        if (_dropper == null) _dropper = FindFirstObjectByType<ItemDropper>();

        // Cache the UI panel reference once instead of searching every frame
        _cachedPanel = FindFirstObjectByType<InventoryPanelUI>();
        if (_cachedPanel != null)
            _cachedPanelCanvasGroup = _cachedPanel.GetComponent<CanvasGroup>();

        CreateGhostMaterial();
    }

    void OnDisable()
    {
        DestroyGhost();
    }

    void OnDestroy()
    {
        DestroyGhost();
        if (_ghostMaterial != null) Destroy(_ghostMaterial);
    }

    /// <summary>Whether placement mode is currently active (ghost visible).</summary>
    public bool IsPlacementModeActive => _placementModeActive;

    void Update()
    {
        // Toggle placement mode on/off
        if (Input.GetKeyDown(PlaceModeKey))
        {
            TogglePlacementMode();
        }

        // Only process ghost & placement when mode is active
        if (_placementModeActive)
        {
            UpdateGhostPreview();

            if (Input.GetKeyDown(RotateKey))
            {
                _currentRotationY = (_currentRotationY + RotationStep) % 360f;
            }

            // Handle placement: Left Click
            if (Input.GetMouseButtonDown(0))
            {
                TryPlaceItem();
            }
        }
    }

    /// <summary>
    /// Toggles placement mode on/off. When off, the ghost is hidden.
    /// </summary>
    public void TogglePlacementMode()
    {
        _placementModeActive = !_placementModeActive;

        if (_placementModeActive)
        {
            Debug.Log("[ItemPlacer] Placement mode ON — look at ground to preview");
        }
        else
        {
            // Hide and destroy the ghost when exiting placement mode
            DestroyGhost();
            _lastItem = null;
            _currentRotationY = 0f;
            Debug.Log("[ItemPlacer] Placement mode OFF");
        }
    }

    private bool IsInventoryPanelOpen()
    {
        // Re-cache if the panel was destroyed and re-created (e.g. scene reload)
        if (_cachedPanel == null)
        {
            _cachedPanel = FindFirstObjectByType<InventoryPanelUI>();
            _cachedPanelCanvasGroup = _cachedPanel != null
                ? _cachedPanel.GetComponent<CanvasGroup>()
                : null;
        }

        if (_cachedPanel == null) return false;
        if (_cachedPanelCanvasGroup != null) return _cachedPanelCanvasGroup.alpha > 0.5f;

        // Fallback: check if the panel's GameObject is active
        return _cachedPanel.gameObject.activeInHierarchy;
    }

    private void UpdateGhostPreview()
    {
        if (_inventory == null) return;

        // Skip ghost if inventory UI is open
        if (IsInventoryPanelOpen())
        {
            if (_ghostObj != null) _ghostObj.SetActive(false);
            return;
        }

        ItemData currentItem = _inventory.SelectedItem;

        // If item changed, rebuild ghost
        if (currentItem != _lastItem)
        {
            RebuildGhost(currentItem);
            _lastItem = currentItem;
            _currentRotationY = 0f; // Reset rotation for new item
        }

        if (_ghostObj == null) return;

        // Raycast to find placement point
        Camera cam = (_player != null) ? _player.GetActiveCamera() : Camera.main;
        if (cam == null)
        {
            _ghostObj.SetActive(false);
            return;
        }

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        
        RaycastHit validHit = default;
        bool foundValid = false;
        float closestDistance = float.MaxValue;

        // Use RaycastAll to explicitly ignore the player and ghost
        // Note: Extent is 200f instead of PlaceDistance + 10f in case the Third-Person Camera is very far back
        RaycastHit[] hits = Physics.RaycastAll(ray, 200f, GroundLayers, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            // Ignore Player's colliders
            if (_player != null && h.collider.transform.root == _player.transform) continue;
            // Ignore Ghost's colliders
            if (_ghostObj != null && h.collider.transform.root == _ghostObj.transform) continue;

            if (h.distance < closestDistance)
            {
                closestDistance = h.distance;
                validHit = h;
                foundValid = true;
            }
        }

        if (foundValid)
        {
            // Calculate distance from the Player (crucial for Third Person camera), using 2D horizontal distance
            float distFromPlayer = validHit.distance;
            if (_player != null)
            {
                Vector2 playerPos2D = new Vector2(_player.transform.position.x, _player.transform.position.z);
                Vector2 hitPos2D = new Vector2(validHit.point.x, validHit.point.z);
                distFromPlayer = Vector2.Distance(playerPos2D, hitPos2D);
            }
            
            bool inRange = distFromPlayer <= PlaceDistance;
            
            _ghostObj.SetActive(true);
            
            // Positioning
            _ghostObj.transform.position = validHit.point + validHit.normal * PlaceHeightOffset;
            
            // Alignment: Use surface normal for Up, but preserve custom Y rotation
            Quaternion baseRot = Quaternion.FromToRotation(Vector3.up, validHit.normal);
            _ghostObj.transform.rotation = baseRot * Quaternion.Euler(0, _currentRotationY, 0);

            // Visibility/Color
            Color targetColor = inRange ? ValidColor : InvalidColor;
            
            MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
            propBlock.SetColor("_BaseColor", targetColor);
            propBlock.SetColor("_Color", targetColor);
            propBlock.SetColor("_MainColor", targetColor); // Support more shaders

            foreach (var rend in _ghostObj.GetComponentsInChildren<Renderer>())
            {
                rend.SetPropertyBlock(propBlock);
            }
        }
        else
        {
            _ghostObj.SetActive(false);
        }
    }

    private void RebuildGhost(ItemData item)
    {
        DestroyGhost();
        if (item == null) return;

        GameObject template = item.DropPrefab != null ? item.DropPrefab : item.Prefab;
        if (template == null) return;

        _ghostObj = Instantiate(template);
        _ghostObj.name = "Placement_Ghost";

        // Put ghost on Ignore Raycast layer so it doesn't interfere with
        // placement raycasts, interaction raycasts, or weapon raycasts.
        int ghostLayer = LayerMask.NameToLayer(GhostLayerName);
        if (ghostLayer >= 0) SetLayerRecursive(_ghostObj, ghostLayer);

        // Strip functionality
        foreach (var script in _ghostObj.GetComponentsInChildren<MonoBehaviour>())
        {
            if (script != this) Destroy(script);
        }
        foreach (var rb in _ghostObj.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        foreach (var col in _ghostObj.GetComponentsInChildren<Collider>()) col.isTrigger = true;

        // Materials
        foreach (var rend in _ghostObj.GetComponentsInChildren<Renderer>())
        {
            // Replace ALL material slots to ensure consistent ghost appearance
            Material[] mats = new Material[rend.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMaterial;
            rend.materials = mats;
        }

        // Scaling logic: Sync with ItemDropper settings exact behavior
        if (item.DropPrefab != null)
        {
            // CreateWorldPickup preserves internal DropPrefab scale entirely
            _ghostObj.transform.localScale = item.DropPrefab.transform.localScale;
        }
        else
        {
            float targetScale = (_dropper != null) ? _dropper.DropPrefabScale : ItemDropper.DefaultDropScale;
            _ghostObj.transform.localScale = Vector3.one * targetScale;
        }

        _ghostObj.SetActive(false);
    }

    private void CreateGhostMaterial()
    {
        // Use URP Lit or Unlit with Transparency
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard"); // Fallback

        _ghostMaterial = new Material(shader);
        
        // Setup for transparency in URP
        if (shader.name.Contains("Universal Render Pipeline"))
        {
            _ghostMaterial.SetFloat("_Surface", 1); // 1 = Transparent
            _ghostMaterial.SetFloat("_Blend", 0); // 0 = Alpha
            _ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _ghostMaterial.SetInt("_ZWrite", 0);
            _ghostMaterial.DisableKeyword("_ALPHATEST_ON");
            _ghostMaterial.EnableKeyword("_ALPHABLEND_ON");
            _ghostMaterial.renderQueue = 3000;
        }
        else
        {
            // Standard Shader Fallback
            _ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _ghostMaterial.SetInt("_ZWrite", 0);
            _ghostMaterial.EnableKeyword("_ALPHABLEND_ON");
            _ghostMaterial.renderQueue = 3000;
        }

        _ghostMaterial.color = ValidColor;
    }

    private void TryPlaceItem()
    {
        ItemData item = _inventory?.SelectedItem;
        if (item == null) return;

        // Skip if ghost is invalid/off
        if (_ghostObj == null || !_ghostObj.activeInHierarchy) return;

        // Double check range from the Player (not the camera, which is further back in TPS mode)
        Camera cam = (_player != null) ? _player.GetActiveCamera() : Camera.main;
        if (cam == null) return;
        
        float distFromPlayer = Vector3.Distance(cam.transform.position, _ghostObj.transform.position);
        if (_player != null)
        {
            Vector2 p2D = new Vector2(_player.transform.position.x, _player.transform.position.z);
            Vector2 g2D = new Vector2(_ghostObj.transform.position.x, _ghostObj.transform.position.z);
            distFromPlayer = Vector2.Distance(p2D, g2D);
        }

        if (distFromPlayer > PlaceDistance) 
        {
            Debug.Log($"[ItemPlacer] Cannot place, horizontally out of range ({distFromPlayer:F1}m).");
            return;
        }

        // Place at ghost's position AND rotation
        GameObject placedObj = ItemDropper.CreateWorldPickup(item, _ghostObj.transform.position);

        if (placedObj != null)
        {
            placedObj.transform.rotation = _ghostObj.transform.rotation;

            Rigidbody rb = placedObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                
                // If weight/physics is disabled for this item, lock it in place
                if (!item.PickupsSpinAndBob)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }
            }
            
            _inventory.RemoveItem(_inventory.SelectedIndex, 1);
            Debug.Log($"[ItemPlacer] Placed {item.ItemName} at {placedObj.transform.position}");

            // If the slot is now empty, auto-exit placement mode
            if (_inventory.SelectedItem == null)
            {
                _placementModeActive = false;
                DestroyGhost();
                _lastItem = null;
                Debug.Log("[ItemPlacer] Slot empty — placement mode OFF");
            }
        }
    }

    /// <summary>
    /// Safely destroys the ghost object if it exists.
    /// </summary>
    private void DestroyGhost()
    {
        if (_ghostObj != null)
        {
            Destroy(_ghostObj);
            _ghostObj = null;
        }
    }

    /// <summary>
    /// Recursively sets the layer on an object and all its children.
    /// </summary>
    private static void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }
}
