using UnityEngine;

/// <summary>
/// Tracks cumulative sunlight exposure on a placed GrassrodevineWet item
/// using cyclemanager.currentTime (game-time). After RequiredSunHours of
/// continuous direct sunlight, marks the item as sun-exposed so pickup
/// delivers SunExposedResultItem instead of the normal item.
/// </summary>
public class SunlightItemTransformer : MonoBehaviour
{
    [Header("Transform Rule")]
    public ItemData RequiredItem;
    public ItemData SunExposedResultItem;
    [Min(0.1f)] public float RequiredSunHours = 2f;

    [Header("Sunlight Check")]
    public Light SunLight;
    public LayerMask OcclusionLayers = ~0;
    public float RayStartHeight = 0.15f;
    public float RayDistance = 1000f;

    private LootItem _loot;
    private float _sunExposureStartTime = -1f;
    private bool _sunExposed;
    private cyclemanager _cycle;

    private void Awake()
    {
        _loot = GetComponent<LootItem>();
    }

    private void Start()
    {
        _cycle = FindFirstObjectByType<cyclemanager>();
    }

    private void Update()
    {
        if (_sunExposed)
            return;

        if (_loot == null)
            _loot = GetComponent<LootItem>();

        if (_loot == null || !_loot.IsPlaced)
        {
            _sunExposureStartTime = -1f;
            return;
        }

        if (RequiredItem != null && _loot.Data != RequiredItem)
            return;

        if (SunExposedResultItem == null)
            return;

        if (IsInDirectSunlight())
        {
            if (_sunExposureStartTime < 0f)
                _sunExposureStartTime = GetGameTime();

            float elapsed = GetGameTime() - _sunExposureStartTime;
            if (elapsed < 0f) elapsed += 24f;

            if (elapsed >= RequiredSunHours)
                MarkSunExposed();
        }
        else
        {
            _sunExposureStartTime = -1f;
        }
    }

    private float GetGameTime()
    {
        if (_cycle != null)
            return _cycle.currentTime;
        return -1f;
    }

    private bool IsInDirectSunlight()
    {
        Light sun = SunLight != null ? SunLight : FindSunLight();
        if (sun == null || !sun.enabled || sun.intensity <= 0.01f)
            return false;

        Vector3 samplePoint = transform.position + Vector3.up * RayStartHeight;
        Vector3 toSun;

        if (sun.type == LightType.Directional)
            toSun = -sun.transform.forward;
        else
        {
            toSun = sun.transform.position - samplePoint;
            RayDistance = Mathf.Max(RayDistance, toSun.magnitude);
            toSun.Normalize();
        }

        if (toSun.y <= 0.01f)
            return false;

        return !Physics.Raycast(samplePoint, toSun, RayDistance, OcclusionLayers, QueryTriggerInteraction.Ignore);
    }

    private Light FindSunLight()
    {
        cyclemanager cycle = FindFirstObjectByType<cyclemanager>();
        if (cycle != null && cycle.sunLight != null)
            return cycle.sunLight;

        DayNightCycle dayNight = FindFirstObjectByType<DayNightCycle>();
        if (dayNight != null)
            return dayNight.GetComponent<Light>();

        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light light in lights)
        {
            if (light != null && light.type == LightType.Directional && light.enabled)
                return light;
        }

        return null;
    }

    private void MarkSunExposed()
    {
        _sunExposed = true;
        _loot.SunExposedOverride = SunExposedResultItem;
        enabled = false;
    }
}
