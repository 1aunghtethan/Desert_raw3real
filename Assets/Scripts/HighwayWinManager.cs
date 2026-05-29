using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns one distant procedural highway per match and ends the game when the player reaches it.
/// </summary>
[DisallowMultipleComponent]
public class HighwayWinManager : MonoBehaviour
{
    private static HighwayWinManager _active;

    private enum HighwayDirection
    {
        East,
        West,
        North,
        South
    }

    [Header("References")]
    public Transform Player;
    public TerrainConfig TerrainConfig;
    public Texture2D RoadTexture;
    public Material RoadMaterial;

    [Header("Distance")]
    public bool TestMode;
    public float NormalDistanceMin = 3000f;
    public float NormalDistanceMax = 4000f;
    public float TestDistance = 100f;

    [Header("Road")]
    public float RoadWidth = 10f;
    public float WinDistanceFromRoadEdge = 3f;
    public float VisibleRoadLength = 600f;
    public float SampleSpacing = 8f;
    public float RoadYOffset = 0.15f;
    public float TextureRepeatLength = 16f;

    [Header("Flat Corridor")]
    public float FlatCorridorHalfWidth = 100f;
    public float FlatHeight = 4f;
    public float FlatBlendWidth = 20f;

    [Header("Testing Visibility")]
    public bool ShowDirectionMarker;
    public float DirectionMarkerHeight = 40f;
    public float DirectionMarkerWidth = 4f;

    [Header("Highway Direction Notification")]
    [SerializeField] private RectTransform notificationPanel;
    [SerializeField] private GameObject southDirectionText;
    [SerializeField] private GameObject eastDirectionText;
    [SerializeField] private GameObject northDirectionText;
    [SerializeField] private GameObject westDirectionText;
    [SerializeField] private AudioClip eastDirectionClip;
    [SerializeField] private AudioClip westDirectionClip;
    [SerializeField] private AudioClip northDirectionClip;
    [SerializeField] private AudioClip southDirectionClip;
    [SerializeField] private AudioClip notificationChimeClip;
    [SerializeField] private float notificationDelayMin = 30f;
    [SerializeField] private float notificationDelayMax = 40f;
    [SerializeField] private float notificationPostVoiceDelay = 0.5f;
    [SerializeField] private float notificationHiddenY = 120f;
    [SerializeField] private float notificationVisibleY = -11f;
    [SerializeField] private float notificationSlideDuration = 0.5f;
    [SerializeField] private float notificationHoldDuration = 2.5f;

    private HighwayDirection _direction;
    private Vector3 _spawnPoint;
    private float _roadCenterCoordinate;
    private Vector3 _roadAxis;
    private Vector3 _roadWidthAxis;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _roadMesh;
    private Material _runtimeRoadMaterial;
    private Transform _directionMarker;
    private Material _directionMarkerMaterial;
    private float _lastMeshCenter = float.NaN;
    private bool _hasWon;
    private bool _endpointPicked;
    private bool _notificationSequenceStarted;
    private Coroutine _notificationRoutine;

    public struct FlatCorridorReference
    {
        public bool PerpendicularAxisIsX;
        public float CenterCoordinate;
        public float OuterHalfWidth;
        public float AlongAnchorCoordinate;
    }

    private void Awake()
    {
        _active = this;
        ResolveReferences();
        EnsureEndpointPicked();
    }

    private void Start()
    {
        ResolveReferences();
        EnsureEndpointPicked();
        Time.timeScale = 1f;

        if (Player == null || TerrainConfig == null)
        {
            Debug.LogError("[HighwayWinManager] Missing Player or TerrainConfig reference.");
            enabled = false;
            return;
        }

        CreateRoadObject();
        CreateDirectionMarker();
        RebuildRoadMesh(force: true);
        PrepareHighwayNotification();
        StartHighwayNotificationSequence();

        Debug.Log($"[HighwayWinManager] Highway endpoint: {_direction}, center coordinate {_roadCenterCoordinate:F1}, test mode {TestMode}.");
    }

    private void Update()
    {
        if (_hasWon || Player == null)
            return;

        RebuildRoadMesh(force: false);
        UpdateDirectionMarker();
        CheckWinDistance();
    }

    private void ResolveReferences()
    {
        if (Player == null)
        {
            PlayerController controller = FindFirstObjectByType<PlayerController>();
            if (controller != null)
                Player = controller.transform;
        }

        TerrainManager terrainManager = TerrainManager.Instance;
        if (terrainManager != null && terrainManager.Config != null)
        {
            TerrainConfig = terrainManager.Config;
        }
        else if (TerrainConfig == null)
        {
            Debug.LogWarning("[HighwayWinManager] TerrainManager config was not available; using assigned TerrainConfig reference.");
        }
    }

    private void PrepareHighwayNotification()
    {
        ResolveNotificationReferences();

        if (notificationPanel == null)
        {
            Debug.LogWarning("[HighwayWinManager] Highway notification panel not found. Direction UI will be skipped.");
            return;
        }

        SetNotificationPanelY(notificationHiddenY);
        SetDirectionTextsActive(null);
    }

    private void StartHighwayNotificationSequence()
    {
        if (_notificationSequenceStarted)
            return;

        _notificationSequenceStarted = true;
        _notificationRoutine = StartCoroutine(HighwayNotificationRoutine());
    }

    private IEnumerator HighwayNotificationRoutine()
    {
        float minDelay = Mathf.Max(0f, Mathf.Min(notificationDelayMin, notificationDelayMax));
        float maxDelay = Mathf.Max(minDelay, Mathf.Max(notificationDelayMin, notificationDelayMax));
        yield return new WaitForSeconds(Random.Range(minDelay, maxDelay));

        AudioClip directionClip = GetDirectionClip(_direction);
        if (directionClip != null)
        {
            AudioManager.Instance.PlaySFX(directionClip);
            yield return new WaitForSeconds(directionClip.length);
        }

        float postVoiceDelay = Mathf.Max(0f, notificationPostVoiceDelay);
        if (postVoiceDelay > 0f)
            yield return new WaitForSeconds(postVoiceDelay);

        if (notificationPanel == null)
        {
            _notificationRoutine = null;
            yield break;
        }

        SetDirectionTextsActive(_direction);
        yield return SlideNotificationPanel(notificationVisibleY);

        if (notificationChimeClip != null)
            AudioManager.Instance.PlaySFX(notificationChimeClip);

        float holdDuration = Mathf.Max(0f, notificationHoldDuration);
        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        yield return SlideNotificationPanel(notificationHiddenY);
        SetDirectionTextsActive(null);
        _notificationRoutine = null;
    }

    private IEnumerator SlideNotificationPanel(float targetY)
    {
        if (notificationPanel == null)
            yield break;

        Vector2 start = notificationPanel.anchoredPosition;
        Vector2 end = new Vector2(start.x, targetY);
        float duration = Mathf.Max(0f, notificationSlideDuration);
        if (duration <= 0f)
        {
            notificationPanel.anchoredPosition = end;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            notificationPanel.anchoredPosition = Vector2.LerpUnclamped(start, end, eased);
            yield return null;
        }

        notificationPanel.anchoredPosition = end;
    }

    private void ResolveNotificationReferences()
    {
        if (notificationPanel == null)
        {
            GameObject panelObject = GameObject.Find("show UI/notification") ?? GameObject.Find("notification");
            if (panelObject != null)
                notificationPanel = panelObject.GetComponent<RectTransform>();
        }

        if (notificationPanel != null)
        {
            if (southDirectionText == null)
                southDirectionText = FindNotificationChild("south");
            if (eastDirectionText == null)
                eastDirectionText = FindNotificationChild("East");
            if (northDirectionText == null)
                northDirectionText = FindNotificationChild("North");
            if (westDirectionText == null)
                westDirectionText = FindNotificationChild("west");
        }

        if (eastDirectionClip == null)
            eastDirectionClip = Resources.Load<AudioClip>("Sounds/east");
        if (westDirectionClip == null)
            westDirectionClip = Resources.Load<AudioClip>("Sounds/west");
        if (northDirectionClip == null)
            northDirectionClip = Resources.Load<AudioClip>("Sounds/north");
        if (southDirectionClip == null)
            southDirectionClip = Resources.Load<AudioClip>("Sounds/south");
        if (notificationChimeClip == null)
            notificationChimeClip = Resources.Load<AudioClip>("Sounds/Notification_chime_#2-1779418448981");
    }

    private GameObject FindNotificationChild(string childName)
    {
        Transform child = notificationPanel != null ? notificationPanel.Find(childName) : null;
        return child != null ? child.gameObject : null;
    }

    private void SetNotificationPanelY(float y)
    {
        if (notificationPanel == null)
            return;

        Vector2 position = notificationPanel.anchoredPosition;
        position.y = y;
        notificationPanel.anchoredPosition = position;
    }

    private void SetDirectionTextsActive(HighwayDirection? activeDirection)
    {
        SetActiveIfPresent(eastDirectionText, activeDirection == HighwayDirection.East);
        SetActiveIfPresent(westDirectionText, activeDirection == HighwayDirection.West);
        SetActiveIfPresent(northDirectionText, activeDirection == HighwayDirection.North);
        SetActiveIfPresent(southDirectionText, activeDirection == HighwayDirection.South);
    }

    private static void SetActiveIfPresent(GameObject target, bool active)
    {
        if (target != null)
            target.SetActive(active);
    }

    private AudioClip GetDirectionClip(HighwayDirection direction)
    {
        switch (direction)
        {
            case HighwayDirection.East:
                return eastDirectionClip;
            case HighwayDirection.West:
                return westDirectionClip;
            case HighwayDirection.North:
                return northDirectionClip;
            case HighwayDirection.South:
                return southDirectionClip;
            default:
                return null;
        }
    }

    public static bool TryApplyHighwayFlattening(float worldX, float worldZ, float naturalHeight, out float height, out float corridorBlend)
    {
        height = naturalHeight;
        corridorBlend = 0f;

        if (_active == null || !_active._endpointPicked)
            return false;

        return _active.TryApplyFlatteningInternal(worldX, worldZ, naturalHeight, out height, out corridorBlend);
    }

    public static bool TryGetFlatCorridorReference(out FlatCorridorReference reference)
    {
        reference = default;

        if (_active == null || !_active._endpointPicked)
            return false;

        bool perpendicularAxisIsX = _active._direction == HighwayDirection.East || _active._direction == HighwayDirection.West;
        reference = new FlatCorridorReference
        {
            PerpendicularAxisIsX = perpendicularAxisIsX,
            CenterCoordinate = _active._roadCenterCoordinate,
            OuterHalfWidth = Mathf.Max(0f, _active.FlatCorridorHalfWidth) + Mathf.Max(0f, _active.FlatBlendWidth),
            AlongAnchorCoordinate = perpendicularAxisIsX ? _active._spawnPoint.z : _active._spawnPoint.x
        };
        return true;
    }

    private bool TryApplyFlatteningInternal(float worldX, float worldZ, float naturalHeight, out float height, out float corridorBlend)
    {
        float perpendicularCoordinate = (_direction == HighwayDirection.East || _direction == HighwayDirection.West)
            ? worldX
            : worldZ;

        float distanceToCenter = Mathf.Abs(perpendicularCoordinate - _roadCenterCoordinate);
        float halfWidth = Mathf.Max(0f, FlatCorridorHalfWidth);
        float blendWidth = Mathf.Max(0f, FlatBlendWidth);

        if (distanceToCenter <= halfWidth)
        {
            height = FlatHeight;
            corridorBlend = 1f;
            return true;
        }

        if (blendWidth <= 0f || distanceToCenter >= halfWidth + blendWidth)
        {
            height = naturalHeight;
            corridorBlend = 0f;
            return false;
        }

        float blendT = Mathf.InverseLerp(halfWidth + blendWidth, halfWidth, distanceToCenter);
        corridorBlend = Mathf.SmoothStep(0f, 1f, blendT);
        height = Mathf.Lerp(naturalHeight, FlatHeight, corridorBlend);
        return true;
    }

    private void EnsureEndpointPicked()
    {
        if (_endpointPicked || TerrainConfig == null)
            return;

        PickHighwayEndpoint();
        _endpointPicked = true;
    }

    private void PickHighwayEndpoint()
    {
        _spawnPoint = TerrainConfig.PlayerSpawnPoint;
        float minDistance = Mathf.Max(0f, Mathf.Min(NormalDistanceMin, NormalDistanceMax));
        float maxDistance = Mathf.Max(minDistance, Mathf.Max(NormalDistanceMin, NormalDistanceMax));
        System.Random highwayRandom = new System.Random(TerrainConfig.Seed + 24681357);
        float distance = TestMode ? Mathf.Max(0f, TestDistance) : Mathf.Lerp(minDistance, maxDistance, (float)highwayRandom.NextDouble());

        _direction = (HighwayDirection)highwayRandom.Next(0, 4);
        switch (_direction)
        {
            case HighwayDirection.East:
                _roadCenterCoordinate = _spawnPoint.x + distance;
                _roadAxis = Vector3.forward;
                _roadWidthAxis = Vector3.right;
                break;
            case HighwayDirection.West:
                _roadCenterCoordinate = _spawnPoint.x - distance;
                _roadAxis = Vector3.forward;
                _roadWidthAxis = Vector3.right;
                break;
            case HighwayDirection.North:
                _roadCenterCoordinate = _spawnPoint.z + distance;
                _roadAxis = Vector3.right;
                _roadWidthAxis = Vector3.forward;
                break;
            default:
                _roadCenterCoordinate = _spawnPoint.z - distance;
                _roadAxis = Vector3.right;
                _roadWidthAxis = Vector3.forward;
                break;
        }
    }

    private void CreateRoadObject()
    {
        GameObject roadObject = new GameObject("Random Highway Road");
        roadObject.transform.SetParent(transform, false);

        _meshFilter = roadObject.AddComponent<MeshFilter>();
        _meshRenderer = roadObject.AddComponent<MeshRenderer>();
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = true;

        _roadMesh = new Mesh { name = "RandomHighwayRoadMesh" };
        _roadMesh.MarkDynamic();
        _meshFilter.sharedMesh = _roadMesh;

        Material materialToUse = null;
        if (RoadMaterial != null && RoadMaterial.shader != null && RoadMaterial.shader.isSupported)
        {
            _runtimeRoadMaterial = new Material(RoadMaterial);
            _runtimeRoadMaterial.name = "Runtime Highway Road Material";
            materialToUse = _runtimeRoadMaterial;
        }
        else
        {
            Shader safeRoadShader = FindSupportedShader(
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Standard",
                "Diffuse");

            if (safeRoadShader != null)
            {
                _runtimeRoadMaterial = new Material(safeRoadShader);
                _runtimeRoadMaterial.name = "Runtime Highway Road Material";
                materialToUse = _runtimeRoadMaterial;
            }
        }

        if (materialToUse == null)
        {
            Debug.LogError("[HighwayWinManager] No supported shader found for the highway road.");
            _meshRenderer.enabled = false;
            return;
        }

        if (RoadTexture != null)
        {
            RoadTexture.wrapMode = TextureWrapMode.Repeat;
            RoadTexture.filterMode = FilterMode.Trilinear;
            RoadTexture.anisoLevel = 16;
            RoadTexture.mipMapBias = -0.75f;
            SetRoadTexture(materialToUse, RoadTexture);
        }

        ApplyRoadMaterialSettings(materialToUse);
        _meshRenderer.sharedMaterial = materialToUse;
    }

    private static Shader FindSupportedShader(params string[] shaderNames)
    {
        foreach (string shaderName in shaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null && shader.isSupported)
                return shader;
        }

        return null;
    }

    private static void SetRoadTexture(Material material, Texture2D texture)
    {
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", Color.white);
        }
    }

    private static void ApplyRoadMaterialSettings(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", 0f);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.2f);

        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", Color.black);

        material.DisableKeyword("_EMISSION");
    }

    private void CreateDirectionMarker()
    {
        if (!ShowDirectionMarker)
            return;

        GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        markerObject.name = "Highway Direction Marker";
        markerObject.transform.SetParent(transform, false);

        Collider markerCollider = markerObject.GetComponent<Collider>();
        if (markerCollider != null)
            Destroy(markerCollider);

        Shader shader = FindSupportedShader(
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Standard",
            "Diffuse");

        if (shader == null)
        {
            Debug.LogWarning("[HighwayWinManager] Direction marker hidden because no supported shader was found.");
            Destroy(markerObject);
            return;
        }

        _directionMarkerMaterial = new Material(shader);
        _directionMarkerMaterial.name = "Runtime Highway Direction Marker Material";
        if (_directionMarkerMaterial.HasProperty("_BaseColor"))
            _directionMarkerMaterial.SetColor("_BaseColor", new Color(1f, 0.8f, 0.05f, 1f));
        if (_directionMarkerMaterial.HasProperty("_Color"))
            _directionMarkerMaterial.SetColor("_Color", new Color(1f, 0.8f, 0.05f, 1f));
        if (_directionMarkerMaterial.HasProperty("_EmissionColor"))
            _directionMarkerMaterial.SetColor("_EmissionColor", Color.black);
        _directionMarkerMaterial.DisableKeyword("_EMISSION");

        Renderer markerRenderer = markerObject.GetComponent<Renderer>();
        if (markerRenderer != null)
            markerRenderer.sharedMaterial = _directionMarkerMaterial;

        _directionMarker = markerObject.transform;
        UpdateDirectionMarker();
    }

    private void UpdateDirectionMarker()
    {
        if (_directionMarker == null || Player == null)
            return;

        float playerAxisCoordinate = Vector3.Dot(Player.position, _roadAxis);
        Vector3 markerBase = GetPointOnRoad(playerAxisCoordinate);
        markerBase.y = SampleRoadHeight(markerBase) + RoadYOffset;

        float markerHeight = Mathf.Max(1f, DirectionMarkerHeight);
        float markerWidth = Mathf.Max(0.2f, DirectionMarkerWidth);
        _directionMarker.position = markerBase + Vector3.up * (markerHeight * 0.5f);
        _directionMarker.localScale = new Vector3(markerWidth, markerHeight * 0.5f, markerWidth);
    }

    private void RebuildRoadMesh(bool force)
    {
        float playerAxisCoordinate = Vector3.Dot(Player.position, _roadAxis);
        float center = Mathf.Round(playerAxisCoordinate / Mathf.Max(0.1f, SampleSpacing)) * SampleSpacing;
        if (!force && Mathf.Abs(center - _lastMeshCenter) < SampleSpacing)
            return;

        _lastMeshCenter = center;

        int segmentCount = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(SampleSpacing, VisibleRoadLength) / Mathf.Max(0.1f, SampleSpacing)));
        int pointCount = segmentCount + 1;
        Vector3[] vertices = new Vector3[pointCount * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segmentCount * 6];

        float halfLength = segmentCount * SampleSpacing * 0.5f;
        float halfWidth = Mathf.Max(0.1f, RoadWidth * 0.5f);
        float repeatLength = Mathf.Max(0.1f, TextureRepeatLength);

        for (int i = 0; i < pointCount; i++)
        {
            float along = center - halfLength + i * SampleSpacing;
            Vector3 centerPoint = GetPointOnRoad(along);
            centerPoint.y = SampleRoadHeight(centerPoint) + RoadYOffset;

            vertices[i * 2] = centerPoint - _roadWidthAxis * halfWidth;
            vertices[i * 2 + 1] = centerPoint + _roadWidthAxis * halfWidth;

            float v = (along - center + halfLength) / repeatLength;
            uvs[i * 2] = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(1f, v);
        }

        for (int i = 0; i < segmentCount; i++)
        {
            int vertex = i * 2;
            int tri = i * 6;
            triangles[tri] = vertex;
            triangles[tri + 1] = vertex + 2;
            triangles[tri + 2] = vertex + 1;
            triangles[tri + 3] = vertex + 1;
            triangles[tri + 4] = vertex + 2;
            triangles[tri + 5] = vertex + 3;
        }

        _roadMesh.Clear();
        _roadMesh.vertices = vertices;
        _roadMesh.uv = uvs;
        _roadMesh.triangles = triangles;
        _roadMesh.RecalculateNormals();
        _roadMesh.RecalculateBounds();
    }

    private Vector3 GetPointOnRoad(float along)
    {
        if (_direction == HighwayDirection.East || _direction == HighwayDirection.West)
            return new Vector3(_roadCenterCoordinate, 0f, along);

        return new Vector3(along, 0f, _roadCenterCoordinate);
    }

    private float SampleRoadHeight(Vector3 worldPoint)
    {
        TerrainManager terrainManager = TerrainManager.Instance;
        if (terrainManager != null)
            return terrainManager.SampleHeight(worldPoint);

        float fallbackHeight = TerrainConfig != null ? TerrainConfig.BaseHeight : 0f;
        if (TryApplyHighwayFlattening(worldPoint.x, worldPoint.z, fallbackHeight, out float height, out _))
            return height;

        if (TerrainConfig != null)
            return TerrainConfig.BaseHeight;

        return 0f;
    }

    private void CheckWinDistance()
    {
        float playerCoordinate = (_direction == HighwayDirection.East || _direction == HighwayDirection.West)
            ? Player.position.x
            : Player.position.z;

        float distanceToCenterLine = Mathf.Abs(playerCoordinate - _roadCenterCoordinate);
        float triggerDistance = Mathf.Max(0.1f, RoadWidth * 0.5f) + Mathf.Max(0f, WinDistanceFromRoadEdge);

        if (distanceToCenterLine <= triggerDistance)
            TriggerWin();
    }

    private void TriggerWin()
    {
        _hasWon = true;
        Debug.Log("[HighwayWinManager] Player reached the highway. You win.");

        if (_notificationRoutine != null)
        {
            StopCoroutine(_notificationRoutine);
            _notificationRoutine = null;
        }

        if (!EndGameUIController.TryShowSurvived())
        {
            Debug.LogWarning("[HighwayWinManager] EndGameUIController not found. Survived panel could not be shown.");
        }
    }

    private void OnDestroy()
    {
        if (_active == this)
            _active = null;

        if (_notificationRoutine != null)
        {
            StopCoroutine(_notificationRoutine);
            _notificationRoutine = null;
        }

        if (_roadMesh != null)
            Destroy(_roadMesh);

        if (_runtimeRoadMaterial != null)
            Destroy(_runtimeRoadMaterial);

        if (_directionMarkerMaterial != null)
            Destroy(_directionMarkerMaterial);
    }
}
