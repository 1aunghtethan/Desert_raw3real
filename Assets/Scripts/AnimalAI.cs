using UnityEngine;
using ithappy.Animals_FREE;

/// <summary>
/// Wandering AI for animals, with optional deer-style player avoidance.
/// </summary>
[RequireComponent(typeof(CreatureMover))]
public class AnimalAI : MonoBehaviour
{
    public AnimalData Data;

    private const float StopDistance = 0.5f;
    private const float FleeRetargetInterval = 0.35f;
    private const float FleeReleaseBuffer = 2f;
    private const float FleeProgressCheckInterval = 0.5f;
    private const float MinFleeDistanceGain = 0.25f;
    private const float PlayerFleeTargetDistance = 40f;
    private const float EatDurationMin = 5f;
    private const float EatDurationMax = 8f;
    private const float EatReachDistance = 1.5f;
    private const float GrassSearchRadius = 30f;
    private const float GrassCollisionIgnoreRadius = 4f;
    private const int GrassSearchFrameInterval = 5;
    private const int GrassCollisionIgnoreFrameInterval = 10;
    private const int GrassSearchHitBufferSize = 64;

    private CreatureMover m_Mover;
    private Transform m_Player;
    private cyclemanager m_DayNight;
    private GameObject m_GrassEatTarget;
    private readonly Collider[] m_GrassSearchHits = new Collider[GrassSearchHitBufferSize];
    private readonly Collider[] m_GrassCollisionHits = new Collider[GrassSearchHitBufferSize];
    private Vector3 m_Target;
    private float m_NextWanderTime;
    private float m_DamageFleeEndTime;
    private float m_NextPlayerSearchTime;
    private float m_NextPlayerFleeRetargetTime;
    private float m_NextFleeProgressCheckTime;
    private float m_LastPlayerDistance;
    private float m_NextAwaySoundTime;
    private float m_NextDayNightSearchTime;
    private float m_EatEndTime;
    private int m_NextGrassSearchFrame;
    private int m_NextGrassCollisionIgnoreFrame;
    private Vector3 m_DamageFleeDirection;
    private AudioSource m_AwayAudioSource;
    private bool m_Initialized;
    private bool m_IgnoredPlayerCollisions;
    private bool m_IsPlayerFleeing;
    private bool m_IsEating;

    private bool UsesAvoidance => Data != null && Data.UsePlayerAvoidance;
    private bool CanEatGrass => Data != null && Data.CanEatGrass;

    private void Awake()
    {
        m_Mover = GetComponent<CreatureMover>();
    }

    private void Start()
    {
        InitializeAI();
    }

    private void InitializeAI()
    {
        if (m_Initialized)
            return;

        ApplyDataSpeeds();
        FindPlayer();

        if (UsesAvoidance)
            PickCalmTarget();
        else
            PickRandomTerrainTarget(GetWanderRadius());

        m_Initialized = true;
    }

    private void Update()
    {
        InitializeAI();
        UpdateAwaySound();
        IgnoreNearbyRealGrassCollisions();

        if (UsesAvoidance)
        {
            UpdateAvoidanceAI();
            return;
        }

        UpdateSimpleWander();
    }

    private void UpdateAwaySound()
    {
        if (Data == null || Data.AwaySound == null)
            return;

        if (m_Player == null && Time.time >= m_NextPlayerSearchTime)
            FindPlayer();

        if (m_Player == null)
            return;

        if (!IsWithinAwaySoundHours())
            return;

        float minDistance = Mathf.Min(Data.AwaySoundMinDistance, Data.AwaySoundMaxDistance);
        float maxDistance = Mathf.Max(Data.AwaySoundMinDistance, Data.AwaySoundMaxDistance);
        float flatDistance = GetFlatPlayerDistance();

        if (flatDistance < minDistance || flatDistance > maxDistance)
            return;

        if (Time.time < m_NextAwaySoundTime)
            return;

        AudioSource source = EnsureAwayAudioSource();
        if (source.isPlaying)
            return;

        source.clip = Data.AwaySound;
        source.volume = Mathf.Max(0f, Data.AwaySoundVolume);
        source.loop = false;
        source.Play();

        m_NextAwaySoundTime = Time.time + Mathf.Max(0f, Data.AwaySoundCooldown);
    }

    private bool IsWithinAwaySoundHours()
    {
        float rawStart = Data.AwaySoundStartHour;
        float rawEnd = Data.AwaySoundEndHour;
        if (Mathf.Approximately(rawStart, rawEnd) || Mathf.Abs(rawEnd - rawStart) >= 23.99f)
            return true;

        if (m_DayNight == null && Time.time >= m_NextDayNightSearchTime)
        {
            m_NextDayNightSearchTime = Time.time + 1f;
            m_DayNight = FindFirstObjectByType<cyclemanager>();
        }

        if (m_DayNight == null)
            return false;

        float hour = Mathf.Repeat(m_DayNight.currentTime, 24f);
        float start = Mathf.Repeat(rawStart, 24f);
        float end = Mathf.Repeat(rawEnd, 24f);

        if (start < end)
            return hour >= start && hour <= end;

        return hour >= start || hour <= end;
    }

    private AudioSource EnsureAwayAudioSource()
    {
        if (m_AwayAudioSource == null)
        {
            Transform sourceTransform = transform.Find("AwaySoundSource");
            if (sourceTransform == null)
            {
                GameObject sourceObject = new GameObject("AwaySoundSource");
                sourceTransform = sourceObject.transform;
                sourceTransform.SetParent(transform, false);
                sourceTransform.localPosition = Vector3.zero;
            }

            m_AwayAudioSource = sourceTransform.GetComponent<AudioSource>();
            if (m_AwayAudioSource == null)
                m_AwayAudioSource = sourceTransform.gameObject.AddComponent<AudioSource>();
        }

        m_AwayAudioSource.playOnAwake = false;
        m_AwayAudioSource.spatialBlend = 1f;
        m_AwayAudioSource.rolloffMode = AudioRolloffMode.Linear;
        m_AwayAudioSource.minDistance = 15f;
        m_AwayAudioSource.maxDistance = Mathf.Max(150f, Mathf.Max(Data.AwaySoundMinDistance, Data.AwaySoundMaxDistance) + 30f);
        return m_AwayAudioSource;
    }

    public void FleeFrom(Vector3 threatPosition, float duration)
    {
        if (!UsesAvoidance)
            return;

        CancelEating();

        Vector3 away = transform.position - threatPosition;
        away.y = 0f;

        if (away.sqrMagnitude < 0.01f)
            away = Random.insideUnitSphere;

        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = transform.forward;

        m_DamageFleeDirection = away.normalized;
        m_DamageFleeEndTime = Time.time + Mathf.Max(0.1f, duration);
        PickDamageFleeTarget();
    }

    private void UpdateAvoidanceAI()
    {
        if (Time.time < m_DamageFleeEndTime)
        {
            CancelEating();

            if (HasReachedTarget())
                PickDamageFleeTarget();

            MoveToTarget(true);
            return;
        }

        if (m_Player == null && Time.time >= m_NextPlayerSearchTime)
            FindPlayer();

        if (m_Player != null)
        {
            float awarenessRadius = Mathf.Max(Data.PlayerAwarenessRadius, Data.PlayerDangerRadius);
            float flatDistance = GetFlatPlayerDistance();
            float sqrDistance = flatDistance * flatDistance;
            if (sqrDistance <= awarenessRadius * awarenessRadius)
            {
                float dangerRadius = Mathf.Max(0f, Data.PlayerDangerRadius);
                float safeDistance = GetPlayerFleeSafeDistance();

                if (flatDistance <= dangerRadius && !m_IsPlayerFleeing)
                {
                    CancelEating();
                    m_IsPlayerFleeing = true;
                    PickPlayerFleeTarget();
                }

                if (m_IsPlayerFleeing)
                {
                    if (!HasReachedTarget())
                    {
                        MoveToTarget(true, false);
                        return;
                    }

                    m_IsPlayerFleeing = false;

                    flatDistance = GetFlatPlayerDistance();
                    if (flatDistance <= dangerRadius)
                    {
                        m_IsPlayerFleeing = true;
                        PickPlayerFleeTarget();
                        MoveToTarget(true, false);
                        return;
                    }

                    ResumeNormalBehavior();
                    return;
                }

                if (flatDistance >= safeDistance)
                    m_IsPlayerFleeing = false;
            }
            else
            {
                m_IsPlayerFleeing = false;
            }
        }
        else
        {
            m_IsPlayerFleeing = false;
        }

        if (UpdateEating())
            return;

        TryPickGrassTarget(false);

        if (Time.time >= m_NextWanderTime || HasReachedTarget())
            PickCalmTarget();

        MoveToTarget(false);
    }

    private void UpdateSimpleWander()
    {
        if (Time.time >= m_NextWanderTime)
            PickRandomTerrainTarget(GetWanderRadius());

        MoveToTarget(false);
    }

    private void MoveToTarget(bool isRun, bool preventRunningTowardPlayer = true)
    {
        Vector3 direction = m_Target - transform.position;
        direction.y = 0f;

        Vector2 axis = Vector2.zero;
        if (direction.sqrMagnitude >= StopDistance * StopDistance)
        {
            direction.Normalize();
            if (isRun && preventRunningTowardPlayer)
                direction = PreventRunningTowardPlayer(direction);

            axis = new Vector2(direction.x, direction.z);
        }

        m_Mover.SetInput(axis, m_Target, isRun, false);
    }

    private void MoveAwayFromPlayer()
    {
        Vector3 away = GetAwayFromPlayerDirection();
        m_DamageFleeDirection = away;

        float lookDistance = Data != null ? Mathf.Max(Data.PlayerDangerRadius, Data.FleeDistanceMin) : 15f;
        m_Target = transform.position + away * lookDistance;
        SnapTargetToTerrain();

        m_NextPlayerFleeRetargetTime = Time.time + FleeRetargetInterval;
        m_NextFleeProgressCheckTime = Time.time + FleeProgressCheckInterval;
        m_LastPlayerDistance = GetFlatPlayerDistance();

        Vector2 axis = new Vector2(away.x, away.z);
        m_Mover.SetInput(axis, m_Target, true, false);
    }

    private float GetPlayerFleeSafeDistance()
    {
        if (Data == null)
            return 15f;

        return Mathf.Max(Data.PlayerDangerRadius + FleeReleaseBuffer, Data.FleeDistanceMin);
    }

    private void PickCalmTarget()
    {
        if (TryPickGrassTarget(true))
            return;

        m_GrassEatTarget = null;
        PickRandomTerrainTarget(GetWanderRadius());
    }

    private bool TryPickGrassTarget(bool forceSearch)
    {
        if (!CanEatGrass || PlantSpawner.Instance == null || m_GrassEatTarget != null || m_IsEating)
            return false;

        if (!forceSearch && Time.frameCount < m_NextGrassSearchFrame)
            return false;

        m_NextGrassSearchFrame = Time.frameCount + GrassSearchFrameInterval;

        if (!TryFindGrassTarget(out GameObject grass))
            return false;

        m_GrassEatTarget = grass;
        IgnoreCollisionsWithGrassTarget();
        m_Target = grass.transform.position;
        SetNextWanderTime();
        return true;
    }

    private bool TryFindGrassTarget(out GameObject grass)
    {
        grass = null;

        if (PlantSpawner.Instance != null &&
            PlantSpawner.Instance.TryGetRandomTerrainGrassNear(transform.position, GrassSearchRadius, out grass))
        {
            return true;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, GrassSearchRadius, m_GrassSearchHits);
        int matchedCount = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = m_GrassSearchHits[i];
            m_GrassSearchHits[i] = null;

            if (hit == null)
                continue;

            LootItem loot = hit.GetComponentInParent<LootItem>();
            if (loot == null || !IsRealGrass(loot))
                continue;

            Vector3 offset = loot.transform.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > GrassSearchRadius * GrassSearchRadius)
                continue;

            matchedCount++;
            if (Random.Range(0, matchedCount) == 0)
                grass = loot.gameObject;
        }

        return grass != null;
    }

    private static bool IsRealGrass(LootItem loot)
    {
        return loot.Data != null &&
            (loot.Data.name == "RealGrass_ItemData" || loot.Data.ItemName == "Real Grass");
    }

    private bool UpdateEating()
    {
        if (m_IsEating)
        {
            StopMoving();
            if (Time.time < m_EatEndTime)
                return true;

            m_IsEating = false;
            SetNextWanderTime();
            PickCalmTarget();
            return false;
        }

        if (m_GrassEatTarget == null)
            return false;

        m_Target = m_GrassEatTarget.transform.position;
        if (GetFlatDistance(transform.position, m_Target) >= EatReachDistance)
        {
            MoveToTarget(false);
            return true;
        }

        Destroy(m_GrassEatTarget);
        m_GrassEatTarget = null;
        m_IsEating = true;
        m_EatEndTime = Time.time + Random.Range(EatDurationMin, EatDurationMax);
        StopMoving();
        return true;
    }

    private void CancelEating()
    {
        m_IsEating = false;
        m_GrassEatTarget = null;
        m_EatEndTime = 0f;
    }

    private void StopMoving()
    {
        if (m_Mover != null)
            m_Mover.SetInput(Vector2.zero, transform.position, false, false);
    }

    private void ResumeNormalBehavior()
    {
        m_IsPlayerFleeing = false;
        m_DamageFleeEndTime = 0f;
        m_Target = transform.position;
        m_NextWanderTime = 0f;
        StopMoving();
        PickCalmTarget();
    }

    private bool HasReachedTarget()
    {
        return GetFlatDistance(transform.position, m_Target) < StopDistance;
    }

    private void IgnoreCollisionsWithGrassTarget()
    {
        if (m_GrassEatTarget == null)
            return;

        IgnoreCollisionsWithGrass(m_GrassEatTarget);
    }

    private void IgnoreNearbyRealGrassCollisions()
    {
        if (!CanEatGrass || Time.frameCount < m_NextGrassCollisionIgnoreFrame)
            return;

        m_NextGrassCollisionIgnoreFrame = Time.frameCount + GrassCollisionIgnoreFrameInterval;

        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, GrassCollisionIgnoreRadius, m_GrassCollisionHits);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = m_GrassCollisionHits[i];
            m_GrassCollisionHits[i] = null;

            if (hit == null)
                continue;

            LootItem loot = hit.GetComponentInParent<LootItem>();
            if (loot == null || !IsRealGrass(loot))
                continue;

            IgnoreCollisionsWithGrass(loot.gameObject);
        }
    }

    private void IgnoreCollisionsWithGrass(GameObject grassObject)
    {
        if (grassObject == null)
            return;

        Collider[] animalColliders = GetComponentsInChildren<Collider>(true);
        Collider[] grassColliders = grassObject.GetComponentsInChildren<Collider>(true);

        foreach (Collider animalCollider in animalColliders)
        {
            if (animalCollider == null)
                continue;

            foreach (Collider grassCollider in grassColliders)
            {
                if (grassCollider == null || animalCollider == grassCollider)
                    continue;

                Physics.IgnoreCollision(animalCollider, grassCollider, true);
            }
        }
    }

    private void PickRandomTerrainTarget(float radius)
    {
        Vector2 randomCircle = Random.insideUnitCircle * Mathf.Max(0f, radius);
        m_Target = transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
        SnapTargetToTerrain();
        SetNextWanderTime();
    }

    private void PickPlayerFleeTarget()
    {
        if (m_Player == null)
            return;

        Vector3 away = GetAwayFromPlayerDirection();
        m_DamageFleeDirection = away.normalized;
        m_Target = transform.position + m_DamageFleeDirection * PlayerFleeTargetDistance;
        SnapTargetToTerrain();
        m_NextPlayerFleeRetargetTime = Time.time + FleeRetargetInterval;
        m_NextFleeProgressCheckTime = Time.time + FleeProgressCheckInterval;
        m_LastPlayerDistance = GetFlatPlayerDistance();
    }

    private void PickTouchFleeTarget()
    {
        Vector3 away = GetAwayFromPlayerDirection();
        m_DamageFleeDirection = away.normalized;

        float minDistance = Data != null ? Mathf.Max(Data.PlayerDangerRadius, Data.FleeDistanceMin) : 15f;
        float maxDistance = Data != null ? Mathf.Max(minDistance, Data.FleeDistanceMax) : 25f;

        Vector3 selectedTarget = transform.position + m_DamageFleeDirection * Random.Range(minDistance, maxDistance);
        float currentPlayerDistance = GetFlatPlayerDistance();

        if (m_Player != null && GetFlatDistance(selectedTarget, m_Player.position) <= currentPlayerDistance + minDistance * 0.75f)
            selectedTarget = m_Player.position + m_DamageFleeDirection * Mathf.Max(Data.PlayerDangerRadius + minDistance, maxDistance);

        m_Target = selectedTarget;
        SnapTargetToTerrain();

        m_NextPlayerFleeRetargetTime = Time.time + FleeRetargetInterval;
        m_NextFleeProgressCheckTime = Time.time + FleeProgressCheckInterval;
        m_LastPlayerDistance = currentPlayerDistance;
    }

    private void PickDamageFleeTarget()
    {
        if (m_DamageFleeDirection.sqrMagnitude < 0.01f)
            m_DamageFleeDirection = transform.forward;

        Vector3 randomOffset = Random.insideUnitSphere;
        randomOffset.y = 0f;
        Vector3 mixedDirection = (m_DamageFleeDirection + randomOffset.normalized * 0.35f).normalized;

        if (mixedDirection.sqrMagnitude < 0.01f)
            mixedDirection = m_DamageFleeDirection;

        PickFleeTarget(mixedDirection, m_Player != null);
    }

    private void PickFleeTarget(Vector3 direction, bool mustIncreasePlayerDistance)
    {
        float minDistance = Data != null ? Data.FleeDistanceMin : 15f;
        float maxDistance = Data != null ? Data.FleeDistanceMax : 25f;
        maxDistance = Mathf.Max(minDistance, maxDistance);

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
            direction = transform.forward;

        direction.Normalize();

        Vector3 selectedTarget = transform.position + direction * Random.Range(minDistance, maxDistance);
        if (mustIncreasePlayerDistance && m_Player != null)
        {
            float currentPlayerDistance = GetFlatPlayerDistance();
            bool foundSafeTarget = false;

            for (int i = 0; i < 8; i++)
            {
                Vector3 candidateDirection = GetRandomizedFleeDirection(direction, i);
                Vector3 candidate = transform.position + candidateDirection * Random.Range(minDistance, maxDistance);
                float candidatePlayerDistance = GetFlatDistance(candidate, m_Player.position);

                if (candidatePlayerDistance > currentPlayerDistance + Mathf.Min(3f, minDistance * 0.5f))
                {
                    selectedTarget = candidate;
                    foundSafeTarget = true;
                    break;
                }
            }

            if (!foundSafeTarget)
            {
                float fallbackDistance = Mathf.Max(currentPlayerDistance + minDistance, Data.PlayerDangerRadius + minDistance);
                selectedTarget = m_Player.position + direction * fallbackDistance;
            }
        }

        m_Target = selectedTarget;
        SnapTargetToTerrain();
    }

    private bool ShouldRetargetPlayerFlee(float sqrDistance)
    {
        if (Time.time >= m_NextPlayerFleeRetargetTime)
            return true;

        if (Vector3.Distance(transform.position, m_Target) < StopDistance)
            return true;

        if (m_Player != null && GetFlatDistance(m_Target, m_Player.position) <= Mathf.Sqrt(sqrDistance) + 1f)
            return true;

        if (Time.time >= m_NextFleeProgressCheckTime)
        {
            float currentDistance = Mathf.Sqrt(sqrDistance);
            m_NextFleeProgressCheckTime = Time.time + FleeProgressCheckInterval;

            if (currentDistance <= m_LastPlayerDistance + MinFleeDistanceGain)
            {
                m_LastPlayerDistance = currentDistance;
                return true;
            }

            m_LastPlayerDistance = currentDistance;
        }

        return false;
    }

    private Vector3 GetAwayFromPlayerDirection()
    {
        if (m_Player == null)
            return transform.forward;

        Vector3 away = transform.position - m_Player.position;
        away.y = 0f;

        if (away.sqrMagnitude >= 0.01f)
            return away.normalized;

        Vector3 playerForward = m_Player.forward;
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude >= 0.01f)
            return -playerForward.normalized;

        Vector3 targetAway = m_Target - m_Player.position;
        targetAway.y = 0f;
        if (targetAway.sqrMagnitude >= 0.01f)
            return targetAway.normalized;

        return transform.forward;
    }

    private Vector3 PreventRunningTowardPlayer(Vector3 runDirection)
    {
        if (m_Player == null || Data == null)
            return runDirection;

        Vector3 away = transform.position - m_Player.position;
        away.y = 0f;

        float awarenessRadius = Mathf.Max(Data.PlayerAwarenessRadius, Data.PlayerDangerRadius);
        if (away.sqrMagnitude > awarenessRadius * awarenessRadius)
            return runDirection;

        if (away.sqrMagnitude < 0.01f)
            away = GetAwayFromPlayerDirection();
        else
            away.Normalize();

        if (Vector3.Dot(runDirection, away) >= 0.1f)
            return runDirection;

        m_Target = transform.position + away * Mathf.Max(Data.PlayerDangerRadius, Data.FleeDistanceMin);
        SnapTargetToTerrain();
        return away;
    }

    private Vector3 GetRandomizedFleeDirection(Vector3 baseDirection, int attempt)
    {
        if (attempt == 0)
            return baseDirection.normalized;

        float angle = Random.Range(-55f, 55f);
        Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * baseDirection;
        candidate.y = 0f;

        if (candidate.sqrMagnitude < 0.01f)
            return baseDirection.normalized;

        return candidate.normalized;
    }

    private float GetFlatPlayerDistance()
    {
        if (m_Player == null)
            return 0f;

        return GetFlatDistance(transform.position, m_Player.position);
    }

    private static float GetFlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void SnapTargetToTerrain()
    {
        if (TerrainManager.Instance != null && GetComponent<OasisFishAI>() == null)
            m_Target.y = TerrainManager.Instance.SampleHeight(new Vector3(m_Target.x, 0f, m_Target.z));
        else
            m_Target.y = transform.position.y;
    }

    private void SetNextWanderTime()
    {
        float minInterval = Data != null ? Data.WanderIntervalMin : 2f;
        float maxInterval = Data != null ? Data.WanderIntervalMax : 5f;
        m_NextWanderTime = Time.time + Random.Range(minInterval, Mathf.Max(minInterval, maxInterval));
    }

    private float GetWanderRadius()
    {
        return Data != null ? Data.WanderRadius : 10f;
    }

    private void ApplyDataSpeeds()
    {
        if (m_Mover == null || Data == null || !Data.UsePlayerAvoidance)
            return;

        m_Mover.SetMovementSpeeds(Data.WalkSpeed, Data.RunSpeed);
    }

    private void FindPlayer()
    {
        m_NextPlayerSearchTime = Time.time + 1f;

        if (TerrainManager.Instance != null && TerrainManager.Instance.Player != null)
        {
            m_Player = TerrainManager.Instance.Player;
            IgnorePlayerCollisions();
            return;
        }

        PlayerController playerController = FindFirstObjectByType<PlayerController>();
        if (playerController != null)
        {
            m_Player = playerController.transform;
            IgnorePlayerCollisions();
        }
    }

    private void IgnorePlayerCollisions()
    {
        if (m_IgnoredPlayerCollisions || !UsesAvoidance || m_Player == null)
            return;

        Collider[] animalColliders = GetComponentsInChildren<Collider>(true);
        Collider[] playerColliders = m_Player.GetComponentsInChildren<Collider>(true);

        foreach (Collider animalCollider in animalColliders)
        {
            if (animalCollider == null)
                continue;

            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider == null || animalCollider == playerCollider)
                    continue;

                Physics.IgnoreCollision(animalCollider, playerCollider, true);
            }
        }

        m_IgnoredPlayerCollisions = true;
    }
}
