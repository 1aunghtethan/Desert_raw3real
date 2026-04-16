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
    private DayNightCycle _cycle;
    
    [Header("Effects")]
    public float StarvationDamage = 1f;
    public float DamageInterval = 2f;
    public float RegenerationRate = 1f;
    public float RegenInterval = 4f;

    [Header("Shadow Shelter")]
    public float ShadeTemperatureReduction = 15f; // Reduce temp by 15C in shade
    public bool IsInShadow { get; private set; }

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

    /// <summary>True when the player is starving or dehydrated and taking damage.</summary>
    public bool IsTakingDamage { get; private set; }

    private Rigidbody _rb;
    private float _damageTimer;
    private float _regenTimer;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(this);

        _rb = GetComponent<Rigidbody>();
        _cycle = FindFirstObjectByType<DayNightCycle>();
    }

    private void Update()
    {
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
                Vector3 lightDir = _cycle.transform.forward; 
                bool blocked = false;

                // 1. Forward Cast from 3 heights
                float[] heights = { 0.3f, 1.0f, 1.8f };
                foreach (float h in heights)
                {
                    if (Physics.Raycast(transform.position + Vector3.up * h, sunDir, 100f, ShadowLayerMask))
                    {
                        blocked = true;
                        break;
                    }
                }

                // 2. Reverse check (only if not already blocked)
                if (!blocked)
                {
                    Vector3 checkOrigin = transform.position + Vector3.up * 1.0f - lightDir * 50f;
                    if (Physics.Raycast(checkOrigin, lightDir, out RaycastHit revHit, 55f, ShadowLayerMask))
                    {
                        if (revHit.transform != transform && !revHit.transform.IsChildOf(transform))
                        {
                            blocked = true;
                        }
                    }
                }
                IsInShadow = blocked;
            }
            
            if (IsInShadow)
            {
                currentTemp = Mathf.Max(25f, currentTemp - ShadeTemperatureReduction);
            }
        }
        else
        {
            IsInShadow = false;
        }

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
        IsTakingDamage = (CurrentHunger <= 0 || CurrentThirst <= 0 || CurrentSleep <= 0);
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

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        Debug.Log($"[PlayerStats] Took {damage} damage. HP: {CurrentHealth}/{MaxHealth}");

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
        Debug.Log("[PlayerStats] Player has died!");
        // Reload scene or show game over UI
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}
