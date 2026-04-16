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
    public List<AnimalData> AnimalPool = new List<AnimalData>();
    public float SpawnRange = 150f; 
    public float DespawnDistance = 250f;
    public float GlobalScaleMultiplier = 1.0f;

    private List<GameObject> _activeAnimals = new List<GameObject>();
    void Start()
    {
        InitializePool();
        
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

    private void InitializePool()
    {
        // Clear explicitly if we want to ensure freshness, or at least remove nulls
        AnimalPool.RemoveAll(item => item == null || item.Prefab == null);

        if (AnimalPool.Count > 0) return;

        Debug.Log("[AnimalSpawner] Initializing pool from assets...");
#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AnimalData", new[] { "Assets/Data/Animals" });
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            AnimalData data = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimalData>(path);
            if (data != null && data.Prefab != null && !AnimalPool.Contains(data))
            {
                AnimalPool.Add(data);
            }
        }
#endif

        if (AnimalPool.Count == 0)
        {
            AnimalData[] allData = Resources.FindObjectsOfTypeAll<AnimalData>();
            foreach(var d in allData) 
            {
                if (d != null && d.Prefab != null && !AnimalPool.Contains(d)) 
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

            // 2. Spawn up to 2 animals per iteration if needed to reach count faster
            int needed = AnimalCount - _activeAnimals.Count;
            int toSpawn = Mathf.Min(needed, 2); 
            
            for(int i = 0; i < toSpawn; i++)
            {
                SpawnAnimal(tm, playerPos);
            }

            yield return new WaitForSeconds(SpawnCheckInterval);
        }
    }

    private void SpawnAnimal(TerrainManager tm, Vector3 center)
    {
        float rx = Random.Range(-SpawnRange, SpawnRange);
        float rz = Random.Range(-SpawnRange, SpawnRange);
        Vector3 spawnPos = center + new Vector3(rx, 0, rz);

        if (Vector3.Distance(spawnPos, center) < 20f) return;

        float y = tm.SampleHeight(new Vector3(spawnPos.x, 0, spawnPos.z));
        spawnPos.y = y;

        if (AnimalPool.Count > 0)
        {
            AnimalData data = AnimalPool[Random.Range(0, AnimalPool.Count)];
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
}
