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
}
