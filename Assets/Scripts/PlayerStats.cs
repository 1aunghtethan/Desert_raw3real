using UnityEngine;
using System.Collections;

/// <summary>
/// Manages player health, hunger, and thirst.
/// Inspired by Minecraft's survival mechanics.
/// </summary>
public class PlayerStats : MonoBehaviour, IDamageable
{
    public static PlayerStats Instance { get; private set; }

    [Header("Current Stats")]
    public float MaxHealth = 20f;
    public float CurrentHealth = 20f;
    
    public float MaxHunger = 20f;
    public float CurrentHunger = 20f;
    public float HungerSaturation = 0f; // Reduces hunger drain when > 0
    
    public float MaxThirst = 20f;
    public float CurrentThirst = 20f;
    
    public float MaxSleep = 20f;
    public float CurrentSleep = 20f;

    [Header("Decay Rates")]
    public float HungerDecayRate = 0.1f;
    public float MovementHungerMultiplier = 2.0f;
    public float BaseSleepDecay = 0.166f; // Takes ~2 in-game days to drain at CycleSpeed=6
    public float SleepDeprivedThirstMultiplier = 2.0f;
    
    [Header("Temperature Influence")]
    public float BaseThirstDecay = 0.15f;
    public float HeatThirstMultiplier = 3.0f; // Thirst drops 3x faster at 50C
    public float CurrentEffectiveTemperature { get; private set; } = 25f;
    private cyclemanager _cycle;
    
    [Header("Effects")]
    public float StarvationDamage = 1f;
    public float DamageInterval = 2f;
    public float RegenerationRate = 1f;
    public float RegenInterval = 4f;

    [Header("Shadow Shelter")]
    public float ShadeTemperatureReduction = 15f; // Reduce temp by 15C in shade
    public bool IsInShadow { get; private set; }
    public bool IsInJoshuaTreeShade { get; private set; }
    [Range(0f, 1f)]
    public float JoshuaShadeCoverageThreshold = 0.9f;
    [Range(0f, 1f)]
    public float JoshuaShadeTemperatureMultiplier = 0.6f;
    public float JoshuaShadeSampleRadius = 0.45f;
    public float JoshuaShadeRayDistance = 100f;

    [Header("Performance Optimization")]
    [Tooltip("How often to check for shadow (in seconds). Higher = better performance.")]
    public float ShadowCheckInterval = 0.15f;
    public LayerMask ShadowLayerMask = ~0; // Default to everything
    private float _shadowCheckTimer;

    [Header("Sleep")]
    [Tooltip("Multiplier for hunger/thirst decay while sleeping (0.25 = 25% of awake rate).")]
    public float SleepDecayMultiplier = 0.25f;

    /// <summary>Set by SleepSystem while the player is sleeping.</summary>
    [HideInInspector] public bool IsSleeping;

    /// <summary>True when the player is starving, dehydrated, or sleep-deprived and taking damage.</summary>
    public bool IsTakingDamage { get; private set; }

    /// <summary>True only when health is reducing from hunger or thirst (not sleep deprivation alone).
    /// Used by SleepSystem to allow sleeping when the only damage source is sleep deprivation.</summary>
    public bool IsTakingNonSleepDamage { get; private set; }

    private Rigidbody _rb;
    private float _damageTimer;
    private float _regenTimer;
    private bool _isDead;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(this);

        _rb = GetComponent<Rigidbody>();
        _cycle = FindFirstObjectByType<cyclemanager>();
    }

    private void Update()
    {
        if (_isDead)
            return;

        HandleDecay();
        HandleEffects();
    }

    private void HandleDecay()
    {
        // Sleep decay
        if (!IsSleeping)
        {
            CurrentSleep = Mathf.Max(0, CurrentSleep - BaseSleepDecay * Time.deltaTime);
        }

        // Hunger decay
        float sleepMult = IsSleeping ? SleepDecayMultiplier : 1f;
        float hungerLoss = HungerDecayRate * Time.deltaTime * sleepMult;
        if (!IsSleeping && _rb != null && _rb.linearVelocity.sqrMagnitude > 0.1f)
        {
            hungerLoss *= MovementHungerMultiplier;
        }

        if (HungerSaturation > 0)
        {
            HungerSaturation = Mathf.Max(0, HungerSaturation - hungerLoss);
        }
        else
        {
            CurrentHunger = Mathf.Max(0, CurrentHunger - hungerLoss);
        }

        // Thirst decay (Scales with temperature)
        float currentTemp = (_cycle != null) ? _cycle.CurrentTemperature : 25f;

        // --- Shadow Check (Optimized: only checks every ShadowCheckInterval) ---
        if (_cycle != null && _cycle.TimeOfDay > 0 && _cycle.TimeOfDay < 180) 
        {
            _shadowCheckTimer += Time.deltaTime;
            if (_shadowCheckTimer >= ShadowCheckInterval)
            {
                _shadowCheckTimer = 0f;
                Vector3 sunDir = -_cycle.transform.forward; 
                UpdateShadowState(sunDir);
            }
            
            if (IsInJoshuaTreeShade)
            {
                currentTemp *= Mathf.Clamp01(JoshuaShadeTemperatureMultiplier);
            }
            else if (IsInShadow)
            {
                currentTemp = Mathf.Max(25f, currentTemp - ShadeTemperatureReduction);
            }
        }
        else
        {
            IsInShadow = false;
            IsInJoshuaTreeShade = false;
        }

        CurrentEffectiveTemperature = currentTemp;

        float tempFactor = Mathf.InverseLerp(20f, 50f, currentTemp); 
        float actualThirstDecay = BaseThirstDecay * Mathf.Lerp(1.0f, HeatThirstMultiplier, tempFactor) * sleepMult;
        
        if (CurrentSleep <= 0)
        {
            actualThirstDecay *= SleepDeprivedThirstMultiplier; // Fast thirst drain when sleep deprived
        }

        CurrentThirst = Mathf.Max(0, CurrentThirst - actualThirstDecay * Time.deltaTime);

        // Extreme Temperature Damage
        if (currentTemp > 45f || currentTemp < 2f)
        {
            _damageTimer += Time.deltaTime;
            if (_damageTimer >= DamageInterval * 2f) // Damage slower than starvation
            {
                TakeDamage(0.5f, transform.position, Vector3.zero);
                _damageTimer = 0;
            }
        }
    }

    private void HandleEffects()
    {
        // Damage if starving, thirsty, or severely sleep deprived
        bool starvingOrDehydrated = (CurrentHunger <= 0 || CurrentThirst <= 0);
        bool sleepDeprived = (CurrentSleep <= 0);
        IsTakingDamage = starvingOrDehydrated || sleepDeprived;
        IsTakingNonSleepDamage = starvingOrDehydrated;
        if (IsTakingDamage)
        {
            _damageTimer += Time.deltaTime;
            if (_damageTimer >= DamageInterval)
            {
                TakeDamage(StarvationDamage, transform.position, Vector3.zero);
                _damageTimer = 0;
            }
        }
        else
        {
            _damageTimer = 0;
        }

        // Heal if well fed, watered, and rested
        if (CurrentHunger >= MaxHunger * 0.8f && CurrentThirst >= MaxThirst * 0.8f && CurrentSleep > 0 && CurrentHealth < MaxHealth)
        {
            _regenTimer += Time.deltaTime;
            if (_regenTimer >= RegenInterval)
            {
                CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + RegenerationRate);
                _regenTimer = 0;
            }
        }
        else
        {
            _regenTimer = 0;
        }
    }

    private void UpdateShadowState(Vector3 sunDir)
    {
        if (sunDir.sqrMagnitude < 0.001f)
        {
            IsInShadow = false;
            IsInJoshuaTreeShade = false;
            return;
        }

        sunDir.Normalize();

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

        float sampleRadius = Mathf.Max(0f, JoshuaShadeSampleRadius);
        float rayDistance = Mathf.Max(0.1f, JoshuaShadeRayDistance);
        int blockedCount = 0;
        int joshuaBlockedCount = 0;
        int sampleCount = 0;

        SampleShadePoint(transform.position, sunDir, rayDistance, ref blockedCount, ref joshuaBlockedCount, ref sampleCount);
        SampleShadePoint(transform.position + forward * sampleRadius, sunDir, rayDistance, ref blockedCount, ref joshuaBlockedCount, ref sampleCount);
        SampleShadePoint(transform.position - forward * sampleRadius, sunDir, rayDistance, ref blockedCount, ref joshuaBlockedCount, ref sampleCount);
        SampleShadePoint(transform.position + right * sampleRadius, sunDir, rayDistance, ref blockedCount, ref joshuaBlockedCount, ref sampleCount);
        SampleShadePoint(transform.position - right * sampleRadius, sunDir, rayDistance, ref blockedCount, ref joshuaBlockedCount, ref sampleCount);

        float joshuaCoverage = sampleCount > 0 ? (float)joshuaBlockedCount / sampleCount : 0f;
        IsInShadow = blockedCount > 0;
        IsInJoshuaTreeShade = joshuaCoverage >= Mathf.Clamp01(JoshuaShadeCoverageThreshold);
        if (IsInJoshuaTreeShade)
            IsInShadow = true;
    }

    private void SampleShadePoint(
        Vector3 basePosition,
        Vector3 sunDir,
        float rayDistance,
        ref int blockedCount,
        ref int joshuaBlockedCount,
        ref int sampleCount)
    {
        SampleShadeRay(basePosition + Vector3.up * 0.3f, sunDir, rayDistance, ref blockedCount, ref joshuaBlockedCount, ref sampleCount);
        SampleShadeRay(basePosition + Vector3.up * 1.8f, sunDir, rayDistance, ref blockedCount, ref joshuaBlockedCount, ref sampleCount);
    }

    private void SampleShadeRay(
        Vector3 origin,
        Vector3 sunDir,
        float rayDistance,
        ref int blockedCount,
        ref int joshuaBlockedCount,
        ref int sampleCount)
    {
        sampleCount++;
        if (!Physics.Raycast(origin, sunDir, out RaycastHit hit, rayDistance, ShadowLayerMask, QueryTriggerInteraction.Ignore))
            return;

        if (hit.transform == transform || hit.transform.IsChildOf(transform))
            return;

        blockedCount++;
        if (IsJoshuaTreeShadeBlocker(hit.collider))
            joshuaBlockedCount++;
    }

    private static bool IsJoshuaTreeShadeBlocker(Collider collider)
    {
        if (collider == null)
            return false;

        PlantHealth health = collider.GetComponentInParent<PlantHealth>();
        if (health != null && IsJoshuaPlantData(health.Data))
            return true;

        Transform root = collider.transform.root;
        string rootName = root != null ? root.name : collider.name;
        return ContainsJoshua(rootName) || ContainsJoshua(collider.name);
    }

    private static bool IsJoshuaPlantData(PlantData data)
    {
        if (data == null)
            return false;

        return ContainsJoshua(data.PlantName) || ContainsJoshua(data.name);
    }

    private static bool ContainsJoshua(string value)
    {
        return !string.IsNullOrEmpty(value)
            && value.IndexOf("joshua", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (_isDead)
            return;

        float previousHealth = CurrentHealth;
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        Debug.Log($"[PlayerStats] Took {damage} damage. HP: {CurrentHealth}/{MaxHealth}");

        if (CurrentHealth < previousHealth && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayReduceHeartSound();
        }

        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    public void Consume(ItemData item)
    {
        // These will be added to ItemData.cs next
        // CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + item.HealthRestore);
        // CurrentHunger = Mathf.Min(MaxHunger, CurrentHunger + item.HungerRestore);
        // CurrentThirst = Mathf.Min(MaxThirst, CurrentThirst + item.ThirstRestore);
        
        // Temporarily using hardcoded values or placeholders if I haven't modified ItemData yet
        Debug.Log($"[PlayerStats] Consumed {item.ItemName}");
    }

    private void Die()
    {
        if (_isDead)
            return;

        _isDead = true;
        Debug.Log("[PlayerStats] Player has died!");

        if (!EndGameUIController.TryShowDied())
        {
            _isDead = false;
            Debug.LogWarning("[PlayerStats] EndGameUIController not found. Death panel could not be shown.");
        }
    }
}
