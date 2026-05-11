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
    public LayerMask InvalidPlacementLayers = ~0;
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

        if (Physics.Raycast(startPos, Vector3.down, out RaycastHit hitInfo, RaycastDistance, GroundLayers, QueryTriggerInteraction.Ignore))
        {
            _currentPlacementPosition = hitInfo.point + hitInfo.normal * PlaceHeightOffset;
            _previewObject.SetActive(true);
        }
        else
        {
            _previewObject.SetActive(false);
            _validPreviewState = false;
            return;
        }

        float yaw = cam.transform.eulerAngles.y + _currentRotationY;
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(_currentRotationX, 0f, 0f);
        _previewObject.transform.SetPositionAndRotation(_currentPlacementPosition, rotation);
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

        PreviewObjectValidChecker checker = _previewObject.GetComponentInChildren<PreviewObjectValidChecker>();
        return checker == null || checker.IsValid;
    }

    private void PlaceObject()
    {
        if (!_inPlacementMode || !_validPreviewState || _inventory == null)
            return;

        ItemData item = _inventory.SelectedItem;
        if (item == null)
            return;

        GameObject placedObject = ItemDropper.CreateWorldPickup(item, _currentPlacementPosition);
        if (placedObject == null)
            return;

        placedObject.transform.rotation = _previewObject.transform.rotation;

        Rigidbody rb = placedObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (!item.PickupsSpinAndBob)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            }
        }

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
        int mask = InvalidPlacementLayers.value & ~GroundLayers.value;

        int ghostLayer = LayerMask.NameToLayer(GhostLayerName);
        if (ghostLayer >= 0)
            mask &= ~(1 << ghostLayer);

        return mask;
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
            _previewMaterial.renderQueue = 3000;
        }

        SetPreviewColor(InvalidColor);
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
