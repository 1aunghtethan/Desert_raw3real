using UnityEngine;

/// <summary>
/// Marks placed Roof objects so they brace nearby sand and can be cut apart with the Raw Knife.
/// </summary>
public class RoofSandStabilizer : MonoBehaviour
{
    [Header("Raw Knife Cutting")]
    public string RequiredCutItemName = "Raw Knife";
    public float CutHealth = 54f;

    [Header("Cut Loot")]
    public int GrassrodeCount = 6;
    public int RealGrassCount = 5;
    public int SmallBranchCount = 8;

    private const string GrassrodePath = "Items/Grassrode_ItemData";
    private const string RealGrassPath = "Items/RealGrass_ItemData";
    private const string SmallBranchPath = "Items/SmallBranch_ItemData";

    private bool _isCutDone;
    private float _currentCutHealth;

    private void Awake()
    {
        _currentCutHealth = Mathf.Max(1f, CutHealth);
    }

    public bool TryCut(ItemData tool, float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (_isCutDone || tool == null || tool.ItemName != RequiredCutItemName)
            return false;

        _currentCutHealth -= Mathf.Max(0.1f, damage);
        Debug.Log($"[RoofSandStabilizer] Roof cut with {tool.ItemName}. Cut health: {_currentCutHealth:F1}/{CutHealth:F1}");

        if (_currentCutHealth <= 0f)
            FinishCut(hitPoint, hitDirection);

        return true;
    }

    private void FinishCut(Vector3 hitPoint, Vector3 hitDirection)
    {
        if (_isCutDone)
            return;

        _isCutDone = true;

        SpawnItemDrops(Resources.Load<ItemData>(GrassrodePath), GrassrodeCount, hitPoint, hitDirection);
        SpawnItemDrops(Resources.Load<ItemData>(RealGrassPath), RealGrassCount, hitPoint, hitDirection);
        SpawnItemDrops(Resources.Load<ItemData>(SmallBranchPath), SmallBranchCount, hitPoint, hitDirection);

        Destroy(gameObject);
    }

    private void SpawnItemDrops(ItemData item, int count, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (item == null || count <= 0)
            return;

        Vector3 center = GetDropCenter(hitPoint);
        Vector3 away = hitDirection.sqrMagnitude > 0.001f ? hitDirection.normalized : transform.forward;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = transform.forward;
        away.Normalize();

        for (int i = 0; i < count; i++)
        {
            float angle = i * 137.5f;
            Vector3 radial = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 spawnPos = center + radial * Random.Range(0.15f, 0.65f) + Vector3.up * Random.Range(0.15f, 0.45f);

            GameObject drop = ItemDropper.CreateWorldPickup(item, spawnPos);
            if (drop == null)
                continue;

            Rigidbody body = drop.GetComponent<Rigidbody>();
            if (body != null)
            {
                Vector3 impulse = (radial * 0.8f + away * 0.35f + Vector3.up * 0.75f).normalized * Random.Range(0.8f, 1.6f);
                body.AddForce(impulse, ForceMode.Impulse);
                body.AddTorque(Random.insideUnitSphere * 1.5f, ForceMode.Impulse);
            }
        }
    }

    private Vector3 GetDropCenter(Vector3 hitPoint)
    {
        if (hitPoint != Vector3.zero)
            return hitPoint;

        Renderer renderer = GetComponentInChildren<Renderer>();
        if (renderer != null)
            return renderer.bounds.center;

        return transform.position + Vector3.up * 0.5f;
    }
}
