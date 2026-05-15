using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Health system for plants. Implements IDamageable for combat.
/// Handles taking damage, toppling over, and spawning loot.
/// </summary>
public class PlantHealth : MonoBehaviour, IDamageable
{
    public PlantData Data;
    private float m_CurrentHealth;
    private bool m_IsDead = false;

    [Header("Health Bar / UI")]
    [Tooltip("Offset for any floating health bar or feedback.")]
    public Vector3 FeedbackOffset = Vector3.up * 2.0f;

    void Start()
    {
        // FALLBACK: If data was lost or not assigned in prefab, try to load it from Resources
        if (Data == null)
        {
            if (gameObject.name.ToLower().Contains("joshuatree"))
            {
                Data = Resources.Load<PlantData>("Plants/JoshuaTreeData");
                if (Data != null) Debug.Log($"[PlantHealth] Automatically recovered PlantData for {gameObject.name}");
            }
        }

        if (Data != null)
        {
            m_CurrentHealth = Data.MaxHealth;
        }
        else
        {
            m_CurrentHealth = 100f;
            Debug.LogWarning($"[PlantHealth] No PlantData assigned to {gameObject.name}. Using default health of 100.");
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (m_IsDead) return;

        m_CurrentHealth -= damage;
        float maxH = Data != null ? Data.MaxHealth : 100f;
        Debug.Log($"[PlantHealth] {gameObject.name} (Plant) took {damage} damage. Health: {m_CurrentHealth}/{maxH}");

        if (Data != null && Data.HitParticles != null)
        {
            Instantiate(Data.HitParticles, hitPoint, Quaternion.LookRotation(hitDirection));
        }

        if (m_CurrentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (m_IsDead) return;
        m_IsDead = true;

        bool topples = Data != null && Data.TopplesOnDeath;
        
        // Smart Fallback: If the name contains "log", we assume it shouldn't topple regardless of flag
        if (gameObject.name.ToLower().Contains("log")) topples = false;

        Debug.Log($"[PlantHealth] {gameObject.name} has been cut down. Topples: {topples}");

        if (topples)
        {
            // 1. Disable own colliders immediately so they don't block anything during the fall
            Collider[] myCols = GetComponentsInChildren<Collider>();
            foreach (Collider c in myCols) c.enabled = false;

            // 2. Disable PlantPhysics to allow scripted rotation
            PlantPhysics physics = GetComponent<PlantPhysics>();
            if (physics != null) physics.enabled = false;

            // 3. Start the 3-second fall, loot will spawn at the end
            StartCoroutine(FallAndDie());
        }
        else
        {
            // 4. For logs/small items, spawn loot and disappear instantly
            SpawnLoot();
            Destroy(gameObject);
        }
    }

    private System.Collections.IEnumerator FallAndDie()
    {
        Quaternion startRot = transform.rotation;
        // Target: Rotate 90 degrees forward on X axis
        Quaternion targetRot = startRot * Quaternion.Euler(75, 0, 0);
        
        float elapsed = 0f;
        float duration = 3.0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Use smooth step for a natural heavy-looking topple
            float smoothT = t * t * (3f - 2f * t);
            transform.rotation = Quaternion.Slerp(startRot, targetRot, smoothT);
            yield return null;
        }

        // Spawn loot ONLY after the fall is complete
        SpawnLoot();
        Destroy(gameObject);
    }


    private void SpawnLoot()
    {
        if (Data == null || Data.LootPrefabs == null || Data.LootPrefabs.Count == 0)
        {
            Debug.LogWarning($"[PlantHealth] {gameObject.name} died but has no loot prefabs configured.");
            return;
        }

        // Ensure Item-to-Item collision is disabled so logs never get stuck
        int itemLayer = LayerMask.NameToLayer("Item");
        if (itemLayer >= 0)
        {
            Physics.IgnoreLayerCollision(itemLayer, itemLayer, true);
        }

        int count = Data.LootCount;
        for (int i = 0; i < count; i++)
        {
            GameObject prefab = Data.LootPrefabs[Random.Range(0, Data.LootPrefabs.Count)];
            
            if (prefab == null)
            {
                Debug.LogWarning($"[PlantHealth] A loot prefab is missing or deleted in {Data.name}. Skipping this loot drop.");
                continue;
            }

            // Place each item in a tight cluster around the center (defaults to 0.2m)
            float radius = (Data != null) ? Data.LootSpawnRadius : 0.2f;
            float angle = i * (360f / count) + Random.Range(-15f, 15f);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 spawnPos = transform.position + dir * radius;

            // Place initially in air so they drop smoothly to ground
            if (TerrainManager.Instance != null)
            {
                float h = TerrainManager.Instance.SampleHeight(spawnPos);
                // Use configured height (recommended 0.5m for bushes, 1.5m for trees)
                float spawnHeight = (Data != null) ? Data.LootSpawnHeight : 0.5f;
                if (!float.IsNaN(h)) spawnPos.y = h + spawnHeight; 
            }

            // Random rotation for natural look
            Quaternion spawnRot = Quaternion.Euler(Random.Range(0, 360f), Random.Range(0, 360f), Random.Range(0, 360f));
            bool isRealGrassLoot = Data != null && Data.PlantName == "Real Grass";
            if (isRealGrassLoot)
            {
                spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }

            GameObject loot = Instantiate(prefab, spawnPos, spawnRot);
            // Use the "Item" layer (10) so the interaction system can pick them up
            SetLayerRecursive(loot, itemLayer); 

            // Auto-fit BoxCollider to the actual mesh bounds to prevent hovering/sinking
            Renderer meshRend = loot.GetComponentInChildren<Renderer>();
            if (meshRend != null)
            {
                BoxCollider bc = loot.GetComponent<BoxCollider>();
                if (bc == null) bc = loot.AddComponent<BoxCollider>();
                
                // Get local bounds for the collider
                Bounds localBounds = meshRend.localBounds;
                bc.center = localBounds.center;
                bc.size = localBounds.size;
                bc.isTrigger = false;
            }

            // Aggressive Rigidbody initialization
            Rigidbody rb = loot.GetComponent<Rigidbody>();
            if (rb == null) rb = loot.AddComponent<Rigidbody>();
            
            rb.mass = 10f; // Heavier logs settle faster and better
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            if (isRealGrassLoot)
            {
                LootItem lootItem = loot.GetComponent<LootItem>();
                if (lootItem == null) lootItem = loot.AddComponent<LootItem>();
                if (lootItem != null && lootItem.Data == null)
                {
                    lootItem.Data = Resources.Load<ItemData>("Items/RealGrass_ItemData");
                }

                if (TerrainManager.Instance != null)
                {
                    float h = TerrainManager.Instance.SampleHeight(spawnPos);
                    if (!float.IsNaN(h))
                    {
                        loot.transform.position = new Vector3(spawnPos.x, h + 0.02f, spawnPos.z);
                    }
                }

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
                continue;
            }

            rb.isKinematic = false;
            rb.useGravity = true;
            rb.WakeUp();
            
            // Random scatter kick
            Vector3 randomKick = (dir + Random.insideUnitSphere * 0.5f).normalized * 3f + Vector3.up * 1f;
            rb.AddForce(randomKick, ForceMode.Impulse);
            
            // Implementation of "Natural Falling Speed" - moderate downward force to help grounding
            rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
            
            rb.AddTorque(Random.insideUnitSphere * 15f, ForceMode.Impulse);
        }
        
        // Rare loot roll (e.g. 8% chance for cactusraw)
        if (Data.RareLootPrefab != null && Random.value <= Data.RareLootChance)
        {
            float angle = Random.Range(0f, 360f);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 spawnPos = transform.position + dir * Data.LootSpawnRadius;
            if (TerrainManager.Instance != null)
            {
                float h = TerrainManager.Instance.SampleHeight(spawnPos);
                if (!float.IsNaN(h)) spawnPos.y = h + Data.LootSpawnHeight;
            }
            GameObject loot = Instantiate(Data.RareLootPrefab, spawnPos, Quaternion.identity);
            SetLayerRecursive(loot, itemLayer);
            Rigidbody rb = loot.GetComponent<Rigidbody>();
            if (rb == null) rb = loot.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
        }
        
        Debug.Log($"[PlantHealth] Grounded physical logs spawned for {gameObject.name}");
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
