using UnityEngine;
using System.Collections;

/// <summary>
/// Health system for animals. Implements IDamageable for combat.
/// Shows a heart feedback when hit and handles death.
/// </summary>
public class AnimalHealth : MonoBehaviour, IDamageable
{
    public AnimalData Data;
    private float m_CurrentHealth;
    
    [Header("Visual Feedback")]
    public GameObject HeartFeedbackPrefab; 
    public Vector3 FeedbackOffset = Vector3.up * 1.5f;

    private bool m_HasDieParameter;
    private Animator m_Animator;

    void Start()
    {
        if (Data != null) m_CurrentHealth = Data.MaxHealth;
        else m_CurrentHealth = 30f;

        m_Animator = GetComponent<Animator>();
        if (m_Animator != null)
        {
            foreach (var parameter in m_Animator.parameters)
            {
                if (parameter.name == "Die")
                {
                    m_HasDieParameter = true;
                    break;
                }
            }
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        m_CurrentHealth -= damage;
        float maxH = Data != null ? Data.MaxHealth : 30f;
        Debug.Log($"[AnimalHealth] {gameObject.name} took {damage} damage. Health: {m_CurrentHealth}/{maxH}");

        ShowHeartFeedback();

        // Reactive AI: If this is a fish, make it flee on hit
        OasisFishAI fishAI = GetComponent<OasisFishAI>();
        if (fishAI != null) fishAI.Flee();

        if (m_CurrentHealth <= 0)
        {
            Die();
        }
    }

    private void ShowHeartFeedback()
    {
        // Simple visual feedback: Instantiate a "heart" or just log it for now
        // if no prefab is assigned, we can use a simple debug line or a temporary sphere
        if (HeartFeedbackPrefab != null)
        {
            Instantiate(HeartFeedbackPrefab, transform.position + FeedbackOffset, Quaternion.identity);
        }
        else
        {
            // Fallback: Just scale the animal briefly as feedback
            StartCoroutine(PulseEffect());
        }
    }

    private IEnumerator PulseEffect()
    {
        Vector3 originalScale = transform.localScale;
        transform.localScale = originalScale * 1.2f;
        yield return new WaitForSeconds(0.1f);
        transform.localScale = originalScale;
    }

    [Header("Loot Settings")]
    [Tooltip("If assigned, this animal will drop this prefab. If null, uses AnimalData.LootPrefab or ItemSetup.MeatTemplate.")]
    public GameObject LootPrefabOverride;

    private void Die()
    {
        Debug.Log($"[AnimalHealth] {gameObject.name} has died.");
        // Play death animation if animator exists
        if (m_Animator != null && m_HasDieParameter)
        {
            m_Animator.SetTrigger("Die"); 
        }

        SpawnLoot();
        
        // Destroy after a short delay for animation/feedback
        Destroy(gameObject, 1.2f); // Slightly longer for animation
    }

    private void SpawnLoot()
    {
        // Priority: 1. LootPrefabOverride, 2. Data.LootPrefab, 3. ItemSetup.MeatTemplate
        GameObject lootTemplate = LootPrefabOverride;
        if (lootTemplate == null && Data != null) lootTemplate = Data.LootPrefab;
        if (lootTemplate == null) lootTemplate = ItemSetup.MeatTemplate;
        
        // Final fallback: Try to load it directly from Assets if in Editor
#if UNITY_EDITOR
        if (lootTemplate == null)
        {
            lootTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Meat/Meat.prefab");
            if (lootTemplate != null) Debug.Log($"[AnimalHealth] Loaded {lootTemplate.name} directly from assets as fallback.");
        }
#endif
        
        if (lootTemplate != null)
        {
            // Position it slightly above the terrain surface
            Vector3 spawnPos = transform.position;
            if (TerrainManager.Instance != null)
            {
                // SampleHeight is more accurate as it checks current chunk data
                float h = TerrainManager.Instance.SampleHeight(spawnPos);
                // Safe check for NaN from terrain
                if (float.IsNaN(h)) h = transform.position.y;
                spawnPos.y = h + 0.3f;
            }
            else
            {
                spawnPos.y += 0.5f;
            }

            GameObject meat = Instantiate(lootTemplate, spawnPos, Quaternion.identity);
            meat.name = lootTemplate.name; // Keep name clean
            meat.SetActive(true);

            // Give it a slight toss so it falls naturally
            Rigidbody rb = meat.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                
                // NaN Protection for normalization
                Vector3 randomDir = Random.insideUnitSphere;
                Vector3 combined = randomDir + Vector3.up;
                if (combined.sqrMagnitude < 0.001f) combined = Vector3.up; // Fallback if exactly opposite
                
                Vector3 tossDir = combined.normalized;
                rb.AddForce(tossDir * 3f, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
            }
            
            Debug.Log($"[AnimalHealth] {gameObject.name} dropped {meat.name} at {spawnPos}");
        }
        else
        {
            Debug.LogWarning($"[AnimalHealth] {gameObject.name} died but no loot template was found!");
        }
    }
}
