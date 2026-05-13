using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

/// <summary>
/// Inventory-aware adapter for the ItemPlacement/ObjectPlacer tutorial system.
/// B toggles placement mode, R and mouse wheel yaw, right-click pitches up/down, left-click places the selected item.
/// </summary>
public class ItemPlacer : MonoBehaviour
{
    [Header("Placement Parameters")]
    public float PlaceDistance = 6f;
    public float PlaceHeightOffset = 0.05f;
    public LayerMask GroundLayers = ~0;
    public LayerMask InvalidPlacementLayers = 0;
    public KeyCode PlaceModeKey = KeyCode.B;
    public KeyCode RotateKey = KeyCode.R;
    public float RotationStep = 90f;
    public float ScrollRotationStep = 15f;

    [Header("ObjectPlacement Raycast")]
    public float ObjectDistanceFromPlayer = 3f;
    public float RaycastStartVerticalOffset = 4f;
    public float RaycastDistance = 8f;

    [Header("Preview Material")]
    public Color ValidColor = new Color(0.2f, 1f, 0.2f, 0.45f);
    public Color InvalidColor = new Color(1f, 0.2f, 0.2f, 0.45f);

    [Header("Terrain Fit")]
    public bool RequireFullGroundSupport = false;
    public float TerrainPenetrationTolerance = 0.15f;
    public float TerrainSupportProbeHeight = 3f;
    public float TerrainSupportProbeDepth = 1.5f;

    private Inventory _inventory;
    private PlayerController _player;
    private InventoryPanelUI _cachedPanel;
    private CanvasGroup _cachedPanelCanvasGroup;

    private GameObject _previewObject;
    private ItemData _previewItem;
    private Material _previewMaterial;
    private Vector3 _currentPlacementPosition;
    private float _currentRotationX;
    private float _currentRotationY;
    private bool _inPlacementMode;
    private bool _validPreviewState;
    private RaycastHit _currentGroundHit;
    private bool _hasCurrentGroundHit;
    private bool _hitValidGround;

    private const string GhostLayerName = "Ignore Raycast";

    public bool IsPlacementModeActive => _inPlacementMode;

    private void Start()
    {
        _inventory = GetComponent<Inventory>();
        if (_inventory == null) _inventory = Inventory.Instance;

        _player = GetComponent<PlayerController>();
        if (_player == null) _player = FindFirstObjectByType<PlayerController>();

        _cachedPanel = FindFirstObjectByType<InventoryPanelUI>();
        if (_cachedPanel != null)
            _cachedPanelCanvasGroup = _cachedPanel.GetComponent<CanvasGroup>();

        CreatePreviewMaterial();
    }

    private void OnDisable()
    {
        ExitPlacementMode();
    }

    private void OnDestroy()
    {
        ExitPlacementMode();
        if (_previewMaterial != null)
            Destroy(_previewMaterial);
    }

    private void Update()
    {
        UpdateInput();

        if (!_inPlacementMode)
            return;

        if (IsInventoryPanelOpen())
        {
            if (_previewObject != null)
                _previewObject.SetActive(false);
            return;
        }

        ItemData selectedItem = _inventory != null ? _inventory.SelectedItem : null;
        if (selectedItem != _previewItem)
            RebuildPreview(selectedItem);

        if (_previewObject == null)
            return;

        UpdateCurrentPlacementPosition();
        UpdatePreviewState();
    }

    private void UpdateInput()
    {
        if (Input.GetKeyDown(PlaceModeKey))
        {
            if (_inPlacementMode)
                ExitPlacementMode();
            else
                EnterPlacementMode();
        }

        if (!_inPlacementMode)
            return;

        if (Input.GetKeyDown(RotateKey))
            RotatePreviewYaw(RotationStep);

        if (Input.GetMouseButtonDown(1))
            RotatePreviewPitch(RotationStep);

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
            RotatePreviewYaw(scroll * ScrollRotationStep);

        if (Input.GetMouseButtonDown(0))
            PlaceObject();
    }

    private void UpdateCurrentPlacementPosition()
    {
        Camera cam = GetPlacementCamera();
        if (cam == null)
        {
            _previewObject.SetActive(false);
            _validPreviewState = false;
            return;
        }

        Vector3 cameraForward = new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z);
        if (cameraForward.sqrMagnitude < 0.001f)
            cameraForward = transform.forward;
        cameraForward.Normalize();

        Vector3 startPos = cam.transform.position + cameraForward * ObjectDistanceFromPlayer;
        startPos.y += RaycastStartVerticalOffset;

        if (Physics.Raycast(startPos, Vector3.down, out RaycastHit hitInfo, RaycastDistance, GetPlacementRayMask(), QueryTriggerInteraction.Ignore))
        {
            _currentGroundHit = hitInfo;
            _hasCurrentGroundHit = true;
            _hitValidGround = IsPlacementGroundCollider(hitInfo.collider);

            // Lift object so its bottom sits ON the terrain, not sinking through
            float bottomOffset = GetPreviewBottomOffset();
            _currentPlacementPosition = hitInfo.point + Vector3.up * (bottomOffset + PlaceHeightOffset);
            _previewObject.SetActive(true);
        }
        else
        {
            _hasCurrentGroundHit = false;
            _hitValidGround = false;
            _previewObject.SetActive(false);
            _validPreviewState = false;
            return;
        }

        float yaw = cam.transform.eulerAngles.y + _currentRotationY;
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(_currentRotationX, 0f, 0f);
        _previewObject.transform.SetPositionAndRotation(_currentPlacementPosition, rotation);
    }

    /// <summary>
    /// Calculates how far below the preview object's pivot the bottom of its
    /// visible bounds extends. Used to lift the ghost so it sits ON terrain.
    /// </summary>
    private float GetPreviewBottomOffset()
    {
        if (_previewObject == null)
            return 0f;

        Vector3 savedPos = _previewObject.transform.position;
        Quaternion savedRot = _previewObject.transform.rotation;
        _previewObject.transform.position = Vector3.zero;
        _previewObject.transform.rotation = Quaternion.identity;

        float lowestY = 0f;
        bool found = false;

        foreach (Renderer renderer in _previewObject.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            float bottomY = renderer.bounds.min.y;
            if (!found || bottomY < lowestY)
            {
                lowestY = bottomY;
                found = true;
            }
        }

        if (!found)
        {
            foreach (Collider col in _previewObject.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled) continue;
                float bottomY = col.bounds.min.y;
                if (!found || bottomY < lowestY)
                {
                    lowestY = bottomY;
                    found = true;
                }
            }
        }

        _previewObject.transform.position = savedPos;
        _previewObject.transform.rotation = savedRot;

        return found ? Mathf.Max(0f, -lowestY) : 0f;
    }

    private void RotatePreviewYaw(float degrees)
    {
        _currentRotationY = Mathf.Repeat(_currentRotationY + degrees, 360f);
    }

    private void RotatePreviewPitch(float degrees)
    {
        _currentRotationX = Mathf.Repeat(_currentRotationX + degrees, 360f);
    }

    private void UpdatePreviewState()
    {
        bool canPlace = CanPlaceObject();
        SetPreviewColor(canPlace ? ValidColor : InvalidColor);
        _validPreviewState = canPlace;
    }

    private bool CanPlaceObject()
    {
        if (_previewObject == null || !_previewObject.activeInHierarchy)
            return false;

        if (GetHorizontalDistance(GetPlacementOrigin(), _currentPlacementPosition) > PlaceDistance)
            return false;

        if (!_hasCurrentGroundHit || !_hitValidGround)
            return false;

        // Check for blocking overlaps (other objects in the way)
        if (!TryGetPreviewBounds(out Bounds bounds))
            return true; // No bounds info = allow placement

        if (!HasNoBlockingOverlap(bounds))
            return false;

        // Only run strict terrain-fit if enabled
        if (RequireFullGroundSupport)
            return HasValidTerrainFit();

        return true;
    }

private void PlaceObject()
    {
        if (!_inPlacementMode || !_validPreviewState || _inventory == null)
            return;

        ItemData item = _inventory.SelectedItem;
        if (item == null)
            return;

        // Use the preview position which already accounts for the object's
        // bottom-bounds offset, so it sits flush on the terrain.
        Vector3 placePos = _currentPlacementPosition;

        GameObject placedObject = ItemDropper.CreateWorldPickup(item, placePos);
        if (placedObject == null)
            return;

        placedObject.transform.rotation = _previewObject.transform.rotation;

        // ALL placed items should stay exactly where put — freeze physics
        Rigidbody rb = placedObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Disable hovering/bobbing so placed items don't float in the air
        WorldItemSpin spin = placedObject.GetComponent<WorldItemSpin>();
        if (spin != null) Object.Destroy(spin);

        LootItem loot = placedObject.GetComponent<LootItem>();
        if (loot != null)
            loot.IsPlaced = true;

        _inventory.RemoveItem(_inventory.SelectedIndex, 1);

        if (_inventory.SelectedItem == null)
            ExitPlacementMode();
        else
            RebuildPreview(_inventory.SelectedItem);
    }

    private void EnterPlacementMode()
    {
        if (_inPlacementMode)
            return;

        _inPlacementMode = true;
        _currentRotationX = 0f;
        _currentRotationY = 0f;
        RebuildPreview(_inventory != null ? _inventory.SelectedItem : null);
    }

    private void ExitPlacementMode()
    {
        if (!_inPlacementMode && _previewObject == null)
            return;

        ClearEditorSelectionIfPreviewSelected();

        if (_previewObject != null)
            Destroy(_previewObject);

        _previewObject = null;
        _previewItem = null;
        _validPreviewState = false;
        _inPlacementMode = false;
    }

    private void RebuildPreview(ItemData item)
    {
        ClearEditorSelectionIfPreviewSelected();

        if (_previewObject != null)
            Destroy(_previewObject);

        _previewObject = null;
        _previewItem = item;
        _validPreviewState = false;

        if (!_inPlacementMode || item == null)
            return;

        GameObject template = item.GetPlacementPrefab();
        if (template == null)
            return;

        try
        {
            _previewObject = Instantiate(template, transform);
        }
        catch (System.InvalidCastException)
        {
            Debug.LogWarning($"[ItemPlacer] {item.ItemName} has an invalid placement prefab reference.");
            return;
        }

        _previewObject.name = "Placement_Preview";

        int ghostLayer = LayerMask.NameToLayer(GhostLayerName);
        if (ghostLayer >= 0)
            SetLayerRecursive(_previewObject, ghostLayer);

        foreach (MonoBehaviour script in _previewObject.GetComponentsInChildren<MonoBehaviour>())
            Destroy(script);

        foreach (Rigidbody rb in _previewObject.GetComponentsInChildren<Rigidbody>())
            Destroy(rb);

        Collider[] colliders = _previewObject.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = true;
            col.isTrigger = true;
        }

        PreviewObjectValidChecker checker = _previewObject.AddComponent<PreviewObjectValidChecker>();
        checker.Configure(GetEffectiveInvalidPlacementLayers());

        foreach (Renderer renderer in _previewObject.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = _previewMaterial;
            renderer.sharedMaterials = materials;
        }

        _previewObject.SetActive(false);
    }

    private bool HasValidTerrainFit()
    {
        if (!_hasCurrentGroundHit || !_hitValidGround)
            return false;

        if (!TryGetPreviewBounds(out Bounds bounds))
            return false;

        if (!RequireFullGroundSupport)
            return HasNoBlockingOverlap(bounds);

        if (!HasNoBlockingOverlap(bounds))
            return false;

        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        extents.x *= 0.9f;
        extents.z *= 0.9f;

        Vector3[] probePoints =
        {
            new Vector3(center.x, bounds.min.y, center.z),
            new Vector3(center.x - extents.x, bounds.min.y, center.z - extents.z),
            new Vector3(center.x - extents.x, bounds.min.y, center.z + extents.z),
            new Vector3(center.x + extents.x, bounds.min.y, center.z - extents.z),
            new Vector3(center.x + extents.x, bounds.min.y, center.z + extents.z)
        };

        float maxProbeDistance = TerrainSupportProbeHeight + TerrainSupportProbeDepth;
        foreach (Vector3 point in probePoints)
        {
            Vector3 origin = point + Vector3.up * TerrainSupportProbeHeight;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxProbeDistance, GroundLayers, QueryTriggerInteraction.Ignore))
                return false;

            if (!IsPlacementGroundCollider(hit.collider))
                return false;

            if (hit.point.y > point.y + TerrainPenetrationTolerance)
                return false;

            if (point.y - hit.point.y > TerrainSupportProbeDepth)
                return false;
        }

        return true;
    }

    private bool HasNoBlockingOverlap(Bounds bounds)
    {
        Vector3 extents = bounds.extents;
        extents.x = Mathf.Max(0.001f, extents.x - TerrainPenetrationTolerance);
        extents.y = Mathf.Max(0.001f, extents.y - TerrainPenetrationTolerance);
        extents.z = Mathf.Max(0.001f, extents.z - TerrainPenetrationTolerance);

        Collider[] overlaps = Physics.OverlapBox(bounds.center, extents, Quaternion.identity, GetEffectiveBlockingLayers(), QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null)
                continue;

            if (_previewObject != null && overlap.transform.root == _previewObject.transform.root)
                continue;

            if (IsPlacementGroundCollider(overlap))
                continue;

            return false;
        }

        return true;
    }

    private bool TryGetPreviewBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        if (_previewObject == null)
            return false;

        bool hasBounds = false;
        foreach (Collider collider in _previewObject.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (hasBounds)
            return true;

        foreach (Renderer renderer in _previewObject.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private bool IsInventoryPanelOpen()
    {
        if (_cachedPanel == null)
        {
            _cachedPanel = FindFirstObjectByType<InventoryPanelUI>();
            _cachedPanelCanvasGroup = _cachedPanel != null ? _cachedPanel.GetComponent<CanvasGroup>() : null;
        }

        if (_cachedPanel == null)
            return false;

        if (_cachedPanelCanvasGroup != null)
            return _cachedPanelCanvasGroup.alpha > 0.5f;

        return _cachedPanel.gameObject.activeInHierarchy;
    }

    private Camera GetPlacementCamera()
    {
        return _player != null ? _player.GetActiveCamera() : Camera.main;
    }

    private Vector3 GetPlacementOrigin()
    {
        return _player != null ? _player.transform.position : transform.position;
    }

    private LayerMask GetEffectiveInvalidPlacementLayers()
    {
        int mask = InvalidPlacementLayers.value;

        int ghostLayer = LayerMask.NameToLayer(GhostLayerName);
        if (ghostLayer >= 0)
            mask &= ~(1 << ghostLayer);

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            mask &= ~(1 << playerLayer);

        // Exclude terrain/ground layers so the ghost touching terrain doesn't
        // invalidate placement
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        if (terrainLayer >= 0)
            mask &= ~(1 << terrainLayer);

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0)
            mask &= ~(1 << groundLayer);

        int defaultLayer = 0; // Default layer (often used for terrain)
        mask &= ~(1 << defaultLayer);

        int waterLayer = LayerMask.NameToLayer("Water");
        if (waterLayer >= 0)
            mask &= ~(1 << waterLayer);

        return mask;
    }

    private LayerMask GetEffectiveBlockingLayers()
    {
        return GetEffectiveInvalidPlacementLayers();
    }

    private LayerMask GetPlacementRayMask()
    {
        int mask = GroundLayers.value | GetEffectiveBlockingLayers().value;

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            mask &= ~(1 << uiLayer);

        return mask;
    }

    private static bool IsPlacementGroundCollider(Collider collider)
    {
        if (collider == null)
            return false;

        return collider is TerrainCollider
            || collider.GetComponentInParent<Terrain>() != null
            || collider.GetComponentInParent<SandChunk>() != null
            || collider.GetComponentInParent<PlacementSurface>() != null;
    }

    private static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    private void CreatePreviewMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        _previewMaterial = new Material(shader);

        if (shader.name.Contains("Universal Render Pipeline"))
        {
            _previewMaterial.SetFloat("_Surface", 1f);
            _previewMaterial.SetFloat("_Blend", 0f);
            _previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _previewMaterial.SetInt("_ZWrite", 0);
            _previewMaterial.DisableKeyword("_ALPHATEST_ON");
            _previewMaterial.EnableKeyword("_ALPHABLEND_ON");
            _previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _previewMaterial.SetOverrideTag("RenderType", "Transparent");
            _previewMaterial.renderQueue = 3000;
        }
        else
        {
            // Standard shader transparency
            _previewMaterial.SetFloat("_Mode", 3f); // Transparent mode
            _previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _previewMaterial.SetInt("_ZWrite", 0);
            _previewMaterial.DisableKeyword("_ALPHATEST_ON");
            _previewMaterial.EnableKeyword("_ALPHABLEND_ON");
            _previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _previewMaterial.renderQueue = 3000;
        }

        SetPreviewColor(ValidColor);
    }

    private void SetPreviewColor(Color color)
    {
        if (_previewMaterial == null)
            return;

        _previewMaterial.color = color;
        if (_previewMaterial.HasProperty("_BaseColor"))
            _previewMaterial.SetColor("_BaseColor", color);
        if (_previewMaterial.HasProperty("_Color"))
            _previewMaterial.SetColor("_Color", color);
    }

    private void ClearEditorSelectionIfPreviewSelected()
    {
#if UNITY_EDITOR
        if (_previewObject == null)
            return;

        GameObject selected = Selection.activeGameObject;
        if (selected != null && selected.transform.root == _previewObject.transform.root)
        {
            Selection.activeGameObject = null;
            InternalEditorUtility.RepaintAllViews();
        }
#endif
    }

    private static void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
