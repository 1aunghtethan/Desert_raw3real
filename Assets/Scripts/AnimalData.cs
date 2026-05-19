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
    public bool CanEatGrass = false;

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
