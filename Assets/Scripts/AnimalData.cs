using UnityEngine;

[CreateAssetMenu(fileName = "NewAnimalData", menuName = "Survival/Animal Data")]
public class AnimalData : ScriptableObject
{
    public string AnimalName = "Animal";
    public GameObject Prefab;
    public float MaxHealth = 30f;
    public float WanderRadius = 10f;
    public float WanderIntervalMin = 2f;
    public float WanderIntervalMax = 5f;
    public GameObject LootPrefab;
    [Min(1)] public int LootCount = 1;
    [Tooltip("Optional world scale override for spawned loot. Use 0 to keep the prefab's authored scale.")]
    [Min(0f)] public float LootWorldScale = 0f;
    public bool CanEatGrass = false;

    [Header("Audio")]
    public AudioClip PainSound;
    public AudioClip DeathSound;
    public AudioClip AwaySound;
    [Min(0f)] public float PainSoundVolume = 1f;
    [Min(0f)] public float DeathSoundVolume = 1f;
    [Min(0f)] public float AwaySoundVolume = 1f;
    [Min(0f)] public float AwaySoundMinDistance = 100f;
    [Min(0f)] public float AwaySoundMaxDistance = 120f;
    [Range(0f, 24f)] public float AwaySoundStartHour = 0f;
    [Range(0f, 24f)] public float AwaySoundEndHour = 24f;
    [Min(0f)] public float AwaySoundCooldown = 45f;

    [Header("Avoidance AI")]
    public bool UsePlayerAvoidance = false;
    public float PlayerDangerRadius = 10f;
    public float PlayerAwarenessRadius = 40f;
    public float GrassWanderRadius = 15f;
    public float WalkSpeed = 3f;
    public float RunSpeed = 6f;
    public float DamageFleeDuration = 4f;
    public float FleeDistanceMin = 15f;
    public float FleeDistanceMax = 25f;
}
