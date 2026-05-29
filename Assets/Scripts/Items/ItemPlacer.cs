using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

/// <summary>
/// Inventory-aware adapter for the ItemPlacement/ObjectPlacer tutorial system.
/// Right-click starts placement for supported items, R and mouse wheel yaw, right-click pitches up/down, left-click places the selected item.
/// </summary>
public class ItemPlacer : MonoBehaviour
{
    [Header("Placement Parameters")]
    public float PlaceDistance = 6f;
    public float PlaceHeightOffset = 0.05f;
    public LayerMask GroundLayers = ~0;
    public LayerMask InvalidPlacementLayers = 0;
    public KeyCode RotateKey = KeyCode.R;
    public float RotationStep = 90f;
    public float ScrollRotationStep = 15f;

    [Header("Roof Placement")]
    public bool RootRequiresSlope = true;
    public float RootMinSlopeAngle = 1f;
    public float RootSlopeSampleDistance = 0.75f;
    public float RootMinSlopeHeightDelta = 0.02f;
    public float RootTiltStep = 5f;
    public float RootMinTiltFromHorizontal = 2f;
    public float RootMaxTiltFromHorizontal = 80f;
    public float RootPreviewDistanceFromPlayer = 3.5f;

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
    private bool _directPlacementMode;
    private ItemData _directPlacementItem;
    private float _rootTiltAngle;

    private const string GrassrodevineWetItemName = "GrassrodevineWet";
    private const float GrassrodevineWetGroundLift = 0.2f;
    private const string GhostLayerName = "Ignore Raycast";

    public bool IsPlacementModeActive => _inPlacementMode;
    public bool IsDirectPlacementActive => _inPlacementMode && _directPlacementMode;
    public float ActivePlacementMoveMultiplier => IsDirectPlacementActive && _directPlacementItem != null
        ? Mathf.Clamp01(_directPlacementItem.PlacementMoveMultiplier)
        : 1f;
    public bool LockRunForActivePlacement => IsDirectPlacementActive
        && _directPlacementItem != null
        && _directPlacementItem.LockRunDuringPlacement;
    public bool LockJumpForActivePlacement => IsDirectPlacementActive
        && _directPlacementItem != null
        && _directPlacementItem.LockJumpDuringPlacement;
    public bool JustPlaced { get; set; }

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
        if (JustPlaced) JustPlaced = false;
        UpdateInput();

        if (!_inPlacementMode)
            return;

        if (IsInventoryPanelOpen())
        {
            if (_previewObject != null)
                _previewObject.SetActive(false);
            return;
        }

        ItemData selectedItem = _directPlacementMode ? _directPlacementItem : (_inventory != null ? _inventory.SelectedItem : null);
        if (selectedItem != _previewItem)
            RebuildPreview(selectedItem);

        if (_previewObject == null)
            return;

        UpdateCurrentPlacementPosition();
        UpdatePreviewState();
    }

    private void UpdateInput()
    {
        if (!_inPlacementMode && Input.GetMouseButtonDown(1) && IsGrassrodevineWetPlacementItem(_inventory != null ? _inventory.SelectedItem : null))
        {
            EnterPlacementMode();
            return;
        }

        if (!_inPlacementMode)
            return;

        if (!IsRootPlacementItem(_previewItem))
        {
            if (Input.GetKeyDown(RotateKey))
                RotatePreviewYaw(RotationStep);

            if (Input.GetMouseButtonDown(1) && !IsGrassrodevineWetPlacementItem(_previewItem))
                RotatePreviewPitch(RotationStep);

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
                RotatePreviewYaw(scroll * ScrollRotationStep);
        }
        else
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
                AdjustRootTilt(scroll);
        }

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

        float placementDistance = IsRootPlacementItem(_previewItem)
            ? Mathf.Max(0.1f, RootPreviewDistanceFromPlayer)
            : ObjectDistanceFromPlayer;
        Vector3 rayOriginBase = IsRootPlacementItem(_previewItem)
            ? GetPlacementOrigin() + cameraForward * placementDistance
            : cam.transform.position + cameraForward * placementDistance;
        Vector3 startPos = rayOriginBase;
        startPos.y += RaycastStartVerticalOffset;

        float bottomOffset = GetPreviewBottomOffset();
        float sinkOffset = GetPreviewSinkOffset();

        if (Physics.Raycast(startPos, Vector3.down, out RaycastHit hitInfo, RaycastDistance, GetPlacementRayMask(), QueryTriggerInteraction.Ignore))
        {
            _currentGroundHit = hitInfo;
            _hasCurrentGroundHit = true;
            _hitValidGround = IsPlacementGroundCollider(hitInfo.collider);

            _currentPlacementPosition = hitInfo.point + Vector3.up * (bottomOffset + PlaceHeightOffset - sinkOffset);
            _previewObject.SetActive(true);
        }
        else
        {
            // Auto-snap fallback: search for ground near the player
            Vector3 playerPos = transform.position;
            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            LayerMask mask = GetPlacementRayMask();

            Vector3[] probes = new Vector3[] {
                playerPos + fwd * 3f,
                playerPos + fwd * 3f + right * 1.5f,
                playerPos + fwd * 3f - right * 1.5f,
                playerPos + right * 2f,
                playerPos - right * 2f,
                playerPos - fwd * 1f
            };

            bool found = false;
            foreach (Vector3 probe in probes)
            {
                Vector3 probeStart = probe + Vector3.up * RaycastStartVerticalOffset;
                if (Physics.Raycast(probeStart, Vector3.down, out hitInfo, RaycastDistance, mask, QueryTriggerInteraction.Ignore))
                {
                    if (IsPlacementGroundCollider(hitInfo.collider))
                    {
                        _currentGroundHit = hitInfo;
                        _hasCurrentGroundHit = true;
                        _hitValidGround = true;
                        _currentPlacementPosition = hitInfo.point + Vector3.up * (bottomOffset + PlaceHeightOffset - sinkOffset);
                        _previewObject.SetActive(true);
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                _hasCurrentGroundHit = false;
                _hitValidGround = false;
                _previewObject.SetActive(false);
                _validPreviewState = false;
                return;
            }
        }

        Quaternion previewRotation = GetPreviewRotation(cam);
        Vector3 previewPosition = GetPreviewPosition(previewRotation);
        if (IsGrassrodevineWetPlacementItem(_previewItem))
            previewPosition = GetGrassrodevineWetGroundedPreviewPosition(previewPosition, previewRotation);

        _previewObject.transform.SetPositionAndRotation(previewPosition, previewRotation);
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
        _previewObject.transform.rotation = GetBoundsMeasurementRotation();

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

    private float GetPreviewSinkOffset()
    {
        if (_previewObject == null || _previewItem == null || _previewItem.PlacementSinkPercent <= 0f)
            return 0f;

        return GetPreviewVisibleHeight() * Mathf.Clamp01(_previewItem.PlacementSinkPercent);
    }

    private float GetPreviewVisibleHeight()
    {
        if (_previewObject == null)
            return 0f;

        Vector3 savedPos = _previewObject.transform.position;
        Quaternion savedRot = _previewObject.transform.rotation;
        _previewObject.transform.position = Vector3.zero;
        _previewObject.transform.rotation = GetBoundsMeasurementRotation();

        Bounds bounds = new Bounds();
        bool found = false;

        foreach (Renderer renderer in _previewObject.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!found)
        {
            foreach (Collider col in _previewObject.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled) continue;
                if (!found)
                {
                    bounds = col.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(col.bounds);
                }
            }
        }

        _previewObject.transform.position = savedPos;
        _previewObject.transform.rotation = savedRot;

        return found ? bounds.size.y : 0f;
    }

    private void RotatePreviewYaw(float degrees)
    {
        _currentRotationY = Mathf.Repeat(_currentRotationY + degrees, 360f);
    }

    private void RotatePreviewPitch(float degrees)
    {
        _currentRotationX = Mathf.Repeat(_currentRotationX + degrees, 360f);
    }

    private void AdjustRootTilt(float scroll)
    {
        float minTilt = Mathf.Max(0f, RootMinTiltFromHorizontal);
        float maxTilt = Mathf.Max(0f, RootMaxTiltFromHorizontal);
        if (maxTilt < minTilt)
            maxTilt = minTilt;

        _rootTiltAngle = Mathf.Clamp(_rootTiltAngle + scroll * RootTiltStep, minTilt, maxTilt);
    }

    private void ResetRootTiltAngle()
    {
        float minTilt = Mathf.Max(0f, RootMinTiltFromHorizontal);
        float maxTilt = Mathf.Max(minTilt, RootMaxTiltFromHorizontal);
        _rootTiltAngle = Mathf.Clamp(minTilt, minTilt, maxTilt);
    }

    private Quaternion GetPreviewRotation(Camera cam)
    {
        if (IsRootPlacementItem(_previewItem))
            return GetRootPlacementRotation(_previewItem);

        float yaw = 0f;
        if (cam != null)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude > 0.001f)
                yaw = Quaternion.LookRotation(flatForward).eulerAngles.y;
        }

        yaw = (yaw + _currentRotationY) % 360f;
        return Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(_currentRotationX, 0f, 0f);
    }

    private Quaternion GetBoundsMeasurementRotation()
    {
        return IsRootPlacementItem(_previewItem)
            ? GetPlacementPrefabRotation(_previewItem)
            : Quaternion.identity;
    }

    private static Quaternion GetPlacementPrefabRotation(ItemData item)
    {
        GameObject template = item != null ? item.GetPlacementPrefab() : null;
        return template != null ? template.transform.rotation : Quaternion.identity;
    }

    private Quaternion GetRootPlacementRotation(ItemData item)
    {
        return GetRootBasePlacementRotation(item) * Quaternion.Euler(-_rootTiltAngle, 0f, 0f);
    }

    private Quaternion GetRootBasePlacementRotation(ItemData item)
    {
        Vector3 awayFromPlayer = _currentPlacementPosition - GetPlacementOrigin();
        awayFromPlayer.y = 0f;

        Quaternion awayRotation = awayFromPlayer.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(awayFromPlayer.normalized, Vector3.up)
            : Quaternion.identity;

        return awayRotation * Quaternion.Euler(0f, 180f, 0f) * GetPlacementPrefabRotation(item);
    }

    private Vector3 GetPreviewPosition(Quaternion previewRotation)
    {
        if (IsRootPlacementItem(_previewItem))
            return GetRootAnchoredPreviewPosition(previewRotation);

        return _currentPlacementPosition;
    }

    private Vector3 GetGrassrodevineWetGroundedPreviewPosition(Vector3 previewPosition, Quaternion previewRotation)
    {
        if (_previewObject == null)
            return previewPosition;

        Vector3 savedPosition = _previewObject.transform.position;
        Quaternion savedRotation = _previewObject.transform.rotation;
        _previewObject.transform.SetPositionAndRotation(previewPosition, previewRotation);

        bool hasBounds = TryGetPreviewLocalBounds(out Bounds localBounds);

        _previewObject.transform.SetPositionAndRotation(savedPosition, savedRotation);

        if (!hasBounds)
            return previewPosition;

        Vector3 liftedPreviewPosition = previewPosition + Vector3.up * GrassrodevineWetGroundLift;
        Vector3 farDirection = liftedPreviewPosition - GetPlacementOrigin();
        farDirection.y = 0f;
        if (farDirection.sqrMagnitude < 0.001f)
        {
            farDirection = transform.forward;
            farDirection.y = 0f;
        }

        if (farDirection.sqrMagnitude < 0.001f)
            return previewPosition;

        farDirection.Normalize();

        Vector3 localFarDirection = Quaternion.Inverse(previewRotation) * farDirection;
        localFarDirection.y = 0f;
        if (localFarDirection.sqrMagnitude < 0.001f)
            localFarDirection = Vector3.forward;
        else
            localFarDirection.Normalize();

        Vector3 localFarBottom = new Vector3(
            Mathf.Abs(localFarDirection.x) > 0.001f
                ? localBounds.center.x + Mathf.Sign(localFarDirection.x) * localBounds.extents.x
                : localBounds.center.x,
            localBounds.min.y,
            Mathf.Abs(localFarDirection.z) > 0.001f
                ? localBounds.center.z + Mathf.Sign(localFarDirection.z) * localBounds.extents.z
                : localBounds.center.z);

        Vector3 worldFarBottom = liftedPreviewPosition + previewRotation * localFarBottom;
        Vector3 rayOrigin = worldFarBottom + Vector3.up * RaycastStartVerticalOffset;
        float rayDistance = Mathf.Max(RaycastDistance, RaycastStartVerticalOffset + TerrainSupportProbeDepth + 2f);

        if (!TryRaycastPlacementGroundIgnoringPreview(rayOrigin, rayDistance, out RaycastHit groundHit))
            return liftedPreviewPosition;

        float targetY = groundHit.point.y + PlaceHeightOffset + GrassrodevineWetGroundLift - GetPreviewSinkOffset();
        float upwardAdjustment = Mathf.Max(0f, targetY - worldFarBottom.y);
        return liftedPreviewPosition + Vector3.up * upwardAdjustment;
    }

    private bool TryGetPreviewLocalBounds(out Bounds localBounds)
    {
        localBounds = new Bounds();
        if (_previewObject == null)
            return false;

        Transform root = _previewObject.transform;
        bool hasBounds = false;

        foreach (Collider collider in _previewObject.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled)
                continue;

            EncapsulateWorldBoundsAsLocal(collider.bounds, root, ref localBounds, ref hasBounds);
        }

        foreach (Renderer renderer in _previewObject.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
                continue;

            EncapsulateWorldBoundsAsLocal(renderer.bounds, root, ref localBounds, ref hasBounds);
        }

        return hasBounds;
    }

    private bool TryRaycastPlacementGroundIgnoringPreview(Vector3 origin, float distance, out RaycastHit closestGroundHit)
    {
        closestGroundHit = default;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance, GetPlacementRayMask(), QueryTriggerInteraction.Ignore);
        float closestDistance = float.MaxValue;
        bool found = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
                continue;

            if (_previewObject != null && hit.collider.transform.root == _previewObject.transform.root)
                continue;

            if (!IsPlacementGroundCollider(hit.collider))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestGroundHit = hit;
            closestDistance = hit.distance;
            found = true;
        }

        return found;
    }

    private Vector3 GetRootAnchoredPreviewPosition(Quaternion tiltedRotation)
    {
        Quaternion baseRotation = GetRootBasePlacementRotation(_previewItem);
        Vector3 farDirection = GetRootFarSideDirection();

        if (!TryGetRootFarSideBottomLocalAnchorOffset(baseRotation, farDirection, out Vector3 localAnchorOffset))
            return _currentPlacementPosition;

        return _currentPlacementPosition - tiltedRotation * localAnchorOffset;
    }

    private Vector3 GetRootFarSideDirection()
    {
        Vector3 farDirection = _currentPlacementPosition - GetPlacementOrigin();
        farDirection.y = 0f;

        if (farDirection.sqrMagnitude < 0.001f)
        {
            farDirection = transform.forward;
            farDirection.y = 0f;
        }

        return farDirection.sqrMagnitude > 0.001f ? farDirection.normalized : Vector3.forward;
    }

    private bool TryGetRootFarSideBottomLocalAnchorOffset(Quaternion baseRotation, Vector3 farDirection, out Vector3 localAnchorOffset)
    {
        localAnchorOffset = Vector3.zero;
        if (_previewObject == null || farDirection.sqrMagnitude < 0.001f)
            return false;

        if (!TryGetRootColliderLocalBounds(out Bounds localBounds)
            && !TryGetRootRendererLocalBounds(out localBounds))
        {
            return false;
        }

        Vector3 localFarDirection = Quaternion.Inverse(baseRotation) * farDirection.normalized;
        localFarDirection.y = 0f;
        if (localFarDirection.sqrMagnitude < 0.001f)
            localFarDirection = Vector3.forward;
        else
            localFarDirection.Normalize();

        float farZ = localBounds.center.z;
        if (Mathf.Abs(localFarDirection.z) > 0.001f)
            farZ += Mathf.Sign(localFarDirection.z) * localBounds.extents.z;
        else
            farZ += localBounds.extents.z;

        localAnchorOffset = new Vector3(localBounds.center.x, localBounds.min.y, farZ);
        return localAnchorOffset.sqrMagnitude > 0.001f;
    }

    private bool TryGetRootColliderLocalBounds(out Bounds localBounds)
    {
        localBounds = new Bounds();
        if (_previewObject == null)
            return false;

        Transform root = _previewObject.transform;
        bool hasBounds = false;
        foreach (Collider collider in _previewObject.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null)
                continue;

            if (collider is BoxCollider box)
            {
                Vector3 halfSize = box.size * 0.5f;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 boxLocalPoint = box.center + Vector3.Scale(halfSize, new Vector3(x, y, z));
                            Vector3 rootLocalPoint = root.InverseTransformPoint(box.transform.TransformPoint(boxLocalPoint));
                            EncapsulateLocalPoint(ref localBounds, ref hasBounds, rootLocalPoint);
                        }
                    }
                }
            }
            else
            {
                EncapsulateWorldBoundsAsLocal(collider.bounds, root, ref localBounds, ref hasBounds);
            }
        }

        return hasBounds;
    }

    private bool TryGetRootRendererLocalBounds(out Bounds localBounds)
    {
        localBounds = new Bounds();
        if (_previewObject == null)
            return false;

        Transform root = _previewObject.transform;
        bool hasBounds = false;
        foreach (Renderer renderer in _previewObject.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
                continue;

            EncapsulateWorldBoundsAsLocal(renderer.bounds, root, ref localBounds, ref hasBounds);
        }

        return hasBounds;
    }

    private static void EncapsulateWorldBoundsAsLocal(Bounds worldBounds, Transform root, ref Bounds localBounds, ref bool hasBounds)
    {
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 worldPoint = new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    EncapsulateLocalPoint(ref localBounds, ref hasBounds, root.InverseTransformPoint(worldPoint));
                }
            }
        }
    }

    private static void EncapsulateLocalPoint(ref Bounds localBounds, ref bool hasBounds, Vector3 point)
    {
        if (!hasBounds)
        {
            localBounds = new Bounds(point, Vector3.zero);
            hasBounds = true;
            return;
        }

        localBounds.Encapsulate(point);
    }

    private void UpdatePreviewState()
    {
        bool canPlace = CanPlaceObject();
        bool forceGreenPreview = IsGrassrodevineWetPlacementItem(_previewItem) || IsRootPlacementItem(_previewItem);
        SetPreviewColor(forceGreenPreview ? ValidColor : (canPlace ? ValidColor : InvalidColor));
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

        bool isRootPlacement = IsRootPlacementItem(_previewItem);
        if (isRootPlacement && !CanPlaceRootOnCurrentSlope())
            return false;

        // Check for blocking overlaps (other objects in the way)
        if (!TryGetPreviewBounds(out Bounds bounds))
            return true; // No bounds info = allow placement

        if (!HasNoBlockingOverlap(bounds))
            return false;

        if (isRootPlacement)
            return true;

        // Only run strict terrain-fit if enabled
        if (RequireFullGroundSupport)
            return HasValidTerrainFit();

        return true;
    }

    private bool CanPlaceRootOnCurrentSlope()
    {
        if (!RootRequiresSlope)
            return true;

        if (!_hasCurrentGroundHit)
            return false;

        if (TerrainManager.Instance != null && CanUseTerrainHeightSlopeSampling(_currentGroundHit.collider))
            return HasRootTerrainHeightSlope();

        float slopeAngle = Vector3.Angle(_currentGroundHit.normal, Vector3.up);
        return slopeAngle >= RootMinSlopeAngle;
    }

    private bool HasRootTerrainHeightSlope()
    {
        Vector3 center = _currentGroundHit.point;
        float sampleDistance = Mathf.Max(0.01f, RootSlopeSampleDistance);
        float minHeightDelta = Mathf.Max(0f, RootMinSlopeHeightDelta);

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = transform.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.right;
        right.Normalize();

        float centerHeight = TerrainManager.Instance.SampleHeight(center);
        return HasRootHeightDelta(centerHeight, center + forward * sampleDistance, minHeightDelta)
            || HasRootHeightDelta(centerHeight, center - forward * sampleDistance, minHeightDelta)
            || HasRootHeightDelta(centerHeight, center + right * sampleDistance, minHeightDelta)
            || HasRootHeightDelta(centerHeight, center - right * sampleDistance, minHeightDelta);
    }

    private static bool HasRootHeightDelta(float centerHeight, Vector3 samplePoint, float minHeightDelta)
    {
        float sampleHeight = TerrainManager.Instance.SampleHeight(samplePoint);
        return Mathf.Abs(sampleHeight - centerHeight) >= minHeightDelta;
    }

    private static bool CanUseTerrainHeightSlopeSampling(Collider collider)
    {
        if (collider == null)
            return false;

        return collider is TerrainCollider
            || collider.GetComponentInParent<Terrain>() != null
            || collider.GetComponentInParent<SandChunk>() != null;
    }

private void PlaceObject()
    {
        if (!_inPlacementMode || !_validPreviewState)
            return;

        ItemData item = _directPlacementMode ? _directPlacementItem : (_inventory != null ? _inventory.SelectedItem : null);
        if (item == null)
            return;

        // Use the preview position which already accounts for the object's
        // bottom-bounds offset, so it sits flush on the terrain.
        Vector3 placePos = (IsRootPlacementItem(item) || IsGrassrodevineWetPlacementItem(item)) && _previewObject != null
            ? _previewObject.transform.position
            : _currentPlacementPosition;

        GameObject placedObject = _directPlacementMode
            ? InstantiateDirectPlacement(item, placePos, _previewObject.transform.rotation)
            : ItemDropper.CreateWorldPickup(item, placePos);
        if (placedObject == null)
            return;

        placedObject.transform.rotation = _previewObject.transform.rotation;

        // ALL placed items should stay exactly where put — freeze physics
        Rigidbody rb = placedObject.GetComponent<Rigidbody>();
        if (IsGrassrodevineWetPlacementItem(item))
        {
            ConfigureGrassrodevineWetPlacedPhysics(placedObject);
        }
        else if (rb != null)
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

        if (!_directPlacementMode && _inventory != null)
            _inventory.RemoveItem(_inventory.SelectedIndex, 1);
        JustPlaced = true;

        if (_directPlacementMode || _inventory == null || _inventory.SelectedItem == null)
            ExitPlacementMode();
        else
            RebuildPreview(_inventory.SelectedItem);
    }

    private void EnterPlacementMode()
    {
        if (_inPlacementMode)
            return;

        ItemData selectedItem = _inventory != null ? _inventory.SelectedItem : null;
        if (!CanPreviewPlacementItem(selectedItem))
            return;

        _inPlacementMode = true;
        _directPlacementMode = false;
        _directPlacementItem = null;
        _currentRotationX = 0f;
        _currentRotationY = 0f;
        RebuildPreview(selectedItem);
    }

    private static bool IsRootPlacementItem(ItemData item)
    {
        return item != null && (item.ItemName == "Roof" || item.ItemName == "Root");
    }

    private static bool IsGrassrodevineWetPlacementItem(ItemData item)
    {
        return item != null && item.ItemName == GrassrodevineWetItemName;
    }

    private static void ConfigureGrassrodevineWetPlacedPhysics(GameObject placedObject)
    {
        if (placedObject == null)
            return;

        BoxCollider box = placedObject.GetComponent<BoxCollider>();
        if (box == null)
            box = placedObject.AddComponent<BoxCollider>();

        if (TryGetLocalRendererBounds(placedObject.transform, out Bounds localBounds))
        {
            box.center = localBounds.center;
            box.size = new Vector3(
                Mathf.Max(0.05f, localBounds.size.x),
                Mathf.Max(0.05f, localBounds.size.y),
                Mathf.Max(0.05f, localBounds.size.z));
        }
        else
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.5f;
        }

        box.isTrigger = false;
        box.enabled = true;

        foreach (Collider collider in placedObject.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null)
                continue;

            collider.enabled = true;
            if (collider != box)
                collider.isTrigger = false;
        }

        Rigidbody body = placedObject.GetComponent<Rigidbody>();
        if (body == null)
            body = placedObject.AddComponent<Rigidbody>();

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
        body.mass = 0.5f;
    }

    private static bool CanPreviewPlacementItem(ItemData item)
    {
        return item != null && item.CanPlaceInWorld && item.GetPlacementPrefab() != null;
    }

    public bool CanStartDirectPlacement(ItemData item)
    {
        return item != null && item.DirectPlaceOnCraft && CanPreviewPlacementItem(item);
    }

    public bool BeginDirectPlacement(ItemData item)
    {
        if (!CanStartDirectPlacement(item))
            return false;

        ExitPlacementMode();
        _directPlacementMode = true;
        _directPlacementItem = item;
        _inPlacementMode = true;
        _currentRotationX = 0f;
        _currentRotationY = 0f;
        if (IsRootPlacementItem(item))
            ResetRootTiltAngle();
        RebuildPreview(item);
        return _previewObject != null;
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
        _directPlacementMode = false;
        _directPlacementItem = null;
        ResetRootTiltAngle();
    }

    private void RebuildPreview(ItemData item)
    {
        ClearEditorSelectionIfPreviewSelected();

        if (_previewObject != null)
            Destroy(_previewObject);

        _previewObject = null;
        _previewItem = item;
        _validPreviewState = false;
        if (IsRootPlacementItem(item))
            ResetRootTiltAngle();

        if (!_inPlacementMode || item == null)
            return;

        if (!CanPreviewPlacementItem(item))
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

        _previewObject.name = _directPlacementMode ? GetDirectPreviewName(item) : "Placement_Preview";

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

        if (IsRootPlacementItem(item))
        {
            ApplyTintedPreviewMaterials(_previewObject, ValidColor);
        }
        else if (!IsGrassrodevineWetPlacementItem(item))
        {
            foreach (Renderer renderer in _previewObject.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = _previewMaterial;
                renderer.sharedMaterials = materials;
            }
        }

        _previewObject.SetActive(false);
    }

    private void ApplyTintedPreviewMaterials(GameObject previewObject, Color color)
    {
        if (previewObject == null)
            return;

        foreach (Renderer renderer in previewObject.GetComponentsInChildren<Renderer>())
        {
            Material[] sourceMaterials = renderer.sharedMaterials;
            Material[] tintedMaterials = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                Material source = sourceMaterials[i] != null ? sourceMaterials[i] : _previewMaterial;
                if (source == null)
                    continue;

                Material tinted = new Material(source);
                SetMaterialColor(tinted, color);
                tintedMaterials[i] = tinted;
            }

            renderer.sharedMaterials = tintedMaterials;
        }
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private GameObject InstantiateDirectPlacement(ItemData item, Vector3 position, Quaternion rotation)
    {
        GameObject template = item.GetPlacementPrefab();
        if (template == null)
            return null;

        GameObject placedObject = Instantiate(template, position, rotation);
        placedObject.name = GetUniqueDirectPlacedName(item);

        if (IsRootPlacementItem(item))
            ConfigureRootPlacedBoxCollider(placedObject);

        return placedObject;
    }

    private static void ConfigureRootPlacedBoxCollider(GameObject placedObject)
    {
        if (placedObject == null)
            return;

        bool hasPrefabCollider = false;
        foreach (Collider collider in placedObject.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null)
                continue;

            hasPrefabCollider = true;
            collider.enabled = true;
        }

        if (!hasPrefabCollider)
        {
            BoxCollider rootBox = placedObject.AddComponent<BoxCollider>();
            if (TryGetLocalRendererBounds(placedObject.transform, out Bounds localBounds))
            {
                rootBox.center = localBounds.center;
                rootBox.size = localBounds.size;
            }
            else
            {
                rootBox.center = Vector3.zero;
                rootBox.size = Vector3.one;
            }

            rootBox.isTrigger = false;
            rootBox.enabled = true;
        }

        if (placedObject.GetComponent<RoofSandStabilizer>() == null)
            placedObject.AddComponent<RoofSandStabilizer>();
    }

    private static bool TryGetLocalRendererBounds(Transform root, out Bounds localBounds)
    {
        localBounds = new Bounds();
        if (root == null)
            return false;

        bool found = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
                continue;

            Bounds worldBounds = renderer.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            foreach (Vector3 corner in corners)
            {
                Vector3 localCorner = root.InverseTransformPoint(corner);
                if (!found)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    found = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        return found;
    }

    private static string GetDirectPreviewName(ItemData item)
    {
        return IsRootPlacementItem(item) ? "roof_GhostPreview" : "DirectPlacement_GhostPreview";
    }

    private static string GetUniqueDirectPlacedName(ItemData item)
    {
        string baseName = IsRootPlacementItem(item) ? "roof_Placed" : "DirectPlacement_Placed";
        if (GameObject.Find(baseName) == null)
            return baseName;

        int index = 2;
        while (GameObject.Find($"{baseName}_{index}") != null)
            index++;

        return $"{baseName}_{index}";
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

        SetMaterialColor(_previewMaterial, color);
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
