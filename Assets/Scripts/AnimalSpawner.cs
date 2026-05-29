using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Spawns animals after a delay on the sand terrain.
/// </summary>
public class AnimalSpawner : MonoBehaviour
{
    public float SpawnCheckInterval = 2.0f;
    public int AnimalCount = 10;
    public int DeerCount = 2;
    public int KittyCount = 1;
    public List<AnimalData> AnimalPool = new List<AnimalData>();
    public float SpawnRange = 150f; 
    public float DespawnDistance = 250f;
    public float GlobalScaleMultiplier = 1.0f;

    private const string DeerAnimalName = "Deer";
    private const string KittyAnimalName = "Kitty";
    private List<GameObject> _activeAnimals = new List<GameObject>();

    void Start()
    {
        InitializePool();
        AnimalCount = GetTargetAnimalCount();
        
        if (AnimalPool == null || AnimalPool.Count == 0)
        {
            Debug.LogError("[AnimalSpawner] FATAL: AnimalPool is still empty after initialization!");
        }
        else
        {
            Debug.Log($"[AnimalSpawner] Initialized with {AnimalPool.Count} animals. Target: {AnimalCount}, Range: {SpawnRange}");
        }
        StartCoroutine(ContinuousSpawnLoop());
    }

    private void OnValidate()
    {
        DeerCount = Mathf.Max(0, DeerCount);
        KittyCount = Mathf.Max(0, KittyCount);
        AnimalCount = GetTargetAnimalCount();

        if (AnimalPool != null)
        {
            AnimalPool.RemoveAll(item => item == null || item.Prefab == null || !IsAllowedAnimal(item));
        }
    }

    private void InitializePool()
    {
        if (AnimalPool == null) AnimalPool = new List<AnimalData>();

        // Clear explicitly if we want to ensure freshness, or at least remove nulls
        AnimalPool.RemoveAll(item => item == null || item.Prefab == null || !IsAllowedAnimal(item));

        if (AnimalPool.Count > 0) return;

        Debug.Log("[AnimalSpawner] Initializing pool from assets...");
#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AnimalData", new[] { "Assets/Data/Animals", "Assets/Resources/Animals" });
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            AnimalData data = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimalData>(path);
            if (data != null && data.Prefab != null && IsAllowedAnimal(data) && !AnimalPool.Contains(data))
            {
                AnimalPool.Add(data);
            }
        }
#endif

        if (AnimalPool.Count == 0)
        {
            AnimalData[] allData = Resources.LoadAll<AnimalData>("Animals");
            foreach(var d in allData) 
            {
                if (d != null && d.Prefab != null && IsAllowedAnimal(d) && !AnimalPool.Contains(d)) 
                {
                    AnimalPool.Add(d);
                }
            }
        }
    }

    IEnumerator ContinuousSpawnLoop()
    {
        yield return new WaitForSeconds(2.0f);

        while (true)
        {
            TerrainManager tm = TerrainManager.Instance;
            if (tm == null)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            Vector3 playerPos = tm.Player != null ? tm.Player.position : Vector3.zero;

            // 1. Cleanup and Despawn
            for (int i = _activeAnimals.Count - 1; i >= 0; i--)
            {
                if (_activeAnimals[i] == null)
                {
                    _activeAnimals.RemoveAt(i);
                    continue;
                }

                float dist = Vector3.Distance(_activeAnimals[i].transform.position, playerPos);
                if (dist > DespawnDistance)
                {
                    Destroy(_activeAnimals[i]);
                    _activeAnimals.RemoveAt(i);
                }
            }

            int spawned = 0;
            int deerNeeded = Mathf.Max(0, DeerCount - CountActiveAnimals(DeerAnimalName));
            int kittyNeeded = Mathf.Max(0, KittyCount - CountActiveAnimals(KittyAnimalName));

            while (deerNeeded > 0 && spawned < 2)
            {
                SpawnAnimal(tm, playerPos, DeerAnimalName);
                deerNeeded--;
                spawned++;
            }

            while (kittyNeeded > 0 && spawned < 2)
            {
                SpawnAnimal(tm, playerPos, KittyAnimalName);
                kittyNeeded--;
                spawned++;
            }

            yield return new WaitForSeconds(SpawnCheckInterval);
        }
    }

    private void SpawnAnimal(TerrainManager tm, Vector3 center, string animalName)
    {
        float rx = Random.Range(-SpawnRange, SpawnRange);
        float rz = Random.Range(-SpawnRange, SpawnRange);
        Vector3 spawnPos = center + new Vector3(rx, 0, rz);

        if (Vector3.Distance(spawnPos, center) < 20f) return;

        float y = tm.SampleHeight(new Vector3(spawnPos.x, 0, spawnPos.z));
        spawnPos.y = y;

        if (AnimalPool.Count > 0)
        {
            AnimalData data = GetRandomAnimalData(animalName);
            if (data == null || data.Prefab == null) 
            {
                // This shouldn't happen if InitializePool works, but let's be safe
                return; 
            }

            GameObject prefab = data.Prefab;
            GameObject animal = Instantiate(prefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360f), 0));
            
            AnimalAI ai = animal.GetComponent<AnimalAI>();
            if (ai != null) ai.Data = data;

            AnimalHealth health = animal.GetComponent<AnimalHealth>();
            if (health != null) health.Data = data;

            animal.transform.localScale = Vector3.one * GlobalScaleMultiplier;
            _activeAnimals.Add(animal);
        }
    }

    private int GetTargetAnimalCount()
    {
        return Mathf.Max(0, DeerCount) + Mathf.Max(0, KittyCount);
    }

    private int CountActiveAnimals(string animalName)
    {
        int count = 0;

        foreach (GameObject animal in _activeAnimals)
        {
            if (animal == null) continue;

            AnimalData data = null;
            AnimalAI ai = animal.GetComponent<AnimalAI>();
            if (ai != null) data = ai.Data;

            if (data == null)
            {
                AnimalHealth health = animal.GetComponent<AnimalHealth>();
                if (health != null) data = health.Data;
            }

            if (IsAnimalNamed(data, animalName)) count++;
        }

        return count;
    }

    private AnimalData GetRandomAnimalData(string animalName)
    {
        List<AnimalData> matches = new List<AnimalData>();

        foreach (AnimalData data in AnimalPool)
        {
            if (IsAnimalNamed(data, animalName))
            {
                matches.Add(data);
            }
        }

        if (matches.Count == 0) return null;
        return matches[Random.Range(0, matches.Count)];
    }

    private static bool IsAllowedAnimal(AnimalData data)
    {
        return IsAnimalNamed(data, DeerAnimalName) || IsAnimalNamed(data, KittyAnimalName);
    }

    private static bool IsAnimalNamed(AnimalData data, string animalName)
    {
        if (data == null) return false;

        string currentName = string.IsNullOrWhiteSpace(data.AnimalName) ? data.name : data.AnimalName;
        return string.Equals(currentName, animalName, System.StringComparison.OrdinalIgnoreCase);
    }
}
