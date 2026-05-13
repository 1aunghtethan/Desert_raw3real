using UnityEngine;

/// <summary>
/// Converts a placed world item into another item after it receives direct sunlight
/// for a configured amount of time.
/// </summary>
public class SunlightItemTransformer : MonoBehaviour
{
    [Header("Transform Rule")]
    public ItemData RequiredItem;
    public ItemData ResultItem;
    [Min(0.1f)] public float RequiredSunSeconds = 2f;

    [Header("Sunlight Check")]
    public Light SunLight;
    public LayerMask OcclusionLayers = ~0;
    public float RayStartHeight = 0.15f;
    public float RayDistance = 1000f;

    private LootItem _loot;
    private float _sunTimer;
    private bool _transformed;

    private void Awake()
    {
        _loot = GetComponent<LootItem>();
    }

    private void Update()
    {
        if (_transformed)
            return;

        if (_loot == null)
            _loot = GetComponent<LootItem>();

        if (_loot == null || !_loot.IsPlaced)
            return;

        if (RequiredItem != null && _loot.Data != RequiredItem)
            return;

        if (ResultItem == null)
            return;

        if (IsInDirectSunlight())
        {
            _sunTimer += Time.deltaTime;
            if (_sunTimer >= RequiredSunSeconds)
                TransformItem();
        }
        else
        {
            _sunTimer = 0f;
        }
    }

    private bool IsInDirectSunlight()
    {
        Light sun = SunLight != null ? SunLight : FindSunLight();
        if (sun == null || !sun.enabled || sun.intensity <= 0.01f)
            return false;

        Vector3 samplePoint = transform.position + Vector3.up * RayStartHeight;
        Vector3 toSun;

        if (sun.type == LightType.Directional)
        {
            toSun = -sun.transform.forward;
        }
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

    private void TransformItem()
    {
        _transformed = true;

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        GameObject replacement = ItemDropper.CreateWorldPickup(ResultItem, position);
        if (replacement != null)
        {
            replacement.transform.rotation = rotation;

            Rigidbody rb = replacement.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            }

            WorldItemSpin spin = replacement.GetComponent<WorldItemSpin>();
            if (spin != null) Destroy(spin);

            LootItem replacementLoot = replacement.GetComponent<LootItem>();
            if (replacementLoot != null)
                replacementLoot.IsPlaced = true;
        }

        Destroy(gameObject);
    }
}
